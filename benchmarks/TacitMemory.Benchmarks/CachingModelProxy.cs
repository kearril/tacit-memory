using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TacitMemory.Core;

namespace TacitMemory.Benchmarks;

/// <summary>
/// Loopback HTTP proxy sitting between the benchmark <see cref="MemoryService"/> and the real upstream embedding and
/// reranker endpoints. Caches per <c>(model, text)</c> for embeddings and per <c>(model, query, passage)</c> for reranking
/// in a local SQLite database, forwarding only cache misses to upstream and preserving exact ordering.
/// </summary>
internal sealed class CachingModelProxy : IAsyncDisposable
{
    private readonly ModelEndpoint? _embeddingUpstream;
    private readonly ModelEndpoint? _rerankerUpstream;
    private readonly bool _noCache;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly SqliteConnection? _db;
    private readonly Lock _dbGate = new();
    private readonly WebApplication _app;

    private int _embeddingHits;
    private int _embeddingMisses;
    private int _rerankHits;
    private int _rerankMisses;

    private CachingModelProxy(
        ModelEndpoint? embeddingUpstream,
        ModelEndpoint? rerankerUpstream,
        string cacheDir,
        bool noCache)
    {
        _embeddingUpstream = embeddingUpstream;
        _rerankerUpstream = rerankerUpstream;
        _noCache = noCache;

        if (!noCache)
        {
            Directory.CreateDirectory(cacheDir);
            var dbPath = Path.Combine(cacheDir, "model-cache.db");
            _db = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            _db.Open();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS embedding_cache (
                    cache_key TEXT PRIMARY KEY,
                    vector BLOB NOT NULL
                );
                CREATE TABLE IF NOT EXISTS rerank_cache (
                    cache_key TEXT PRIMARY KEY,
                    score REAL NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.MapPost("/v1/embeddings", HandleEmbeddingsAsync);
        _app.MapPost("/v1/rerank", HandleRerankAsync);
    }

    public ModelEndpoint? EmbeddingProxyEndpoint { get; private set; }

    public ModelEndpoint? RerankerProxyEndpoint { get; private set; }

    public (int EmbeddingHits, int EmbeddingMisses, int RerankHits, int RerankMisses) Stats =>
        (_embeddingHits, _embeddingMisses, _rerankHits, _rerankMisses);

    public static async Task<CachingModelProxy> StartAsync(
        ModelEndpoint? embeddingUpstream,
        ModelEndpoint? rerankerUpstream,
        string cacheDir,
        bool noCache,
        CancellationToken cancellationToken)
    {
        var proxy = new CachingModelProxy(embeddingUpstream, rerankerUpstream, cacheDir, noCache);
        await proxy._app.StartAsync(cancellationToken);
        var root = new Uri(proxy._app.Urls.Single());
        if (embeddingUpstream is not null)
        {
            proxy.EmbeddingProxyEndpoint = new ModelEndpoint(
                new Uri(root, "/v1/embeddings"),
                embeddingUpstream.Model,
                ApiKey: null,
                ModelProvider.Generic);
        }

        if (rerankerUpstream is not null)
        {
            proxy.RerankerProxyEndpoint = new ModelEndpoint(
                new Uri(root, "/v1/rerank"),
                rerankerUpstream.Model,
                ApiKey: null,
                ModelProvider.Generic);
        }

        return proxy;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _http.Dispose();
        lock (_dbGate)
            _db?.Dispose();
    }

    private async Task<IResult> HandleEmbeddingsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (_embeddingUpstream is null)
            return Results.StatusCode(404);

        var body = (await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken))?.AsObject();
        var inputs = body?["input"]?.AsArray().Select(node => node!.GetValue<string>()).ToList();
        if (inputs is null)
            return Results.BadRequest();

        var vectors = new float[inputs.Count][];
        var missedTexts = new List<string>();
        var missedIndexByText = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new string[inputs.Count];

        for (var i = 0; i < inputs.Count; i++)
        {
            keys[i] = CacheKey(_embeddingUpstream.Model, inputs[i]);
            if (!_noCache && TryGetEmbedding(keys[i]) is { } cached)
            {
                vectors[i] = cached;
                Interlocked.Increment(ref _embeddingHits);
            }
            else if (missedIndexByText.TryAdd(inputs[i], missedTexts.Count))
            {
                missedTexts.Add(inputs[i]);
            }
        }

        if (missedTexts.Count > 0)
        {
            Interlocked.Add(ref _embeddingMisses, missedTexts.Count);
            var fetched = await FetchEmbeddingsUpstreamAsync(_embeddingUpstream, missedTexts, cancellationToken);
            if (fetched is null)
                return Results.StatusCode(502);

            if (!_noCache)
            {
                var toStore = new List<(string Key, float[] Vector)>(missedTexts.Count);
                for (var i = 0; i < missedTexts.Count; i++)
                    toStore.Add((CacheKey(_embeddingUpstream.Model, missedTexts[i]), fetched[i]));
                StoreEmbeddings(toStore);
            }

            for (var i = 0; i < inputs.Count; i++)
            {
                if (vectors[i] is null)
                    vectors[i] = fetched[missedIndexByText[inputs[i]]];
            }
        }

        var data = new JsonArray();
        for (var i = 0; i < vectors.Length; i++)
        {
            var embeddingArray = new JsonArray(vectors[i].Select(value => JsonValue.Create(value)).ToArray());
            data.Add(new JsonObject { ["index"] = i, ["embedding"] = embeddingArray });
        }

        return Results.Json(new JsonObject { ["data"] = data });
    }

    private async Task<IResult> HandleRerankAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (_rerankerUpstream is null)
            return Results.StatusCode(404);

        var body = (await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken))?.AsObject();
        var query = body?["query"]?.GetValue<string>();
        var documents = body?["documents"]?.AsArray().Select(node => node!.GetValue<string>()).ToList();
        if (query is null || documents is null)
            return Results.BadRequest();

        var scores = new double?[documents.Count];
        var missedDocs = new List<string>();
        var missedIndexByDoc = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < documents.Count; i++)
        {
            var key = CacheKey(_rerankerUpstream.Model, query, documents[i]);
            if (!_noCache && TryGetRerankScore(key) is { } cached)
            {
                scores[i] = cached;
                Interlocked.Increment(ref _rerankHits);
            }
            else if (missedIndexByDoc.TryAdd(documents[i], missedDocs.Count))
            {
                missedDocs.Add(documents[i]);
            }
        }

        if (missedDocs.Count > 0)
        {
            Interlocked.Add(ref _rerankMisses, missedDocs.Count);
            var fetched = await FetchRerankUpstreamAsync(_rerankerUpstream, query, missedDocs, cancellationToken);
            if (fetched is null)
                return Results.StatusCode(502);

            if (!_noCache)
            {
                var toStore = new List<(string Key, double Score)>(missedDocs.Count);
                for (var i = 0; i < missedDocs.Count; i++)
                    toStore.Add((CacheKey(_rerankerUpstream.Model, query, missedDocs[i]), fetched[i]));
                StoreRerankScores(toStore);
            }

            for (var i = 0; i < documents.Count; i++)
            {
                if (scores[i] is null)
                    scores[i] = fetched[missedIndexByDoc[documents[i]]];
            }
        }

        var results = new JsonArray(
            scores
                .Select((score, index) => (index, score: score!.Value))
                .OrderByDescending(item => item.score)
                .Select(item => (JsonNode)new JsonObject
                {
                    ["index"] = item.index,
                    ["relevance_score"] = item.score,
                })
                .ToArray());

        return Results.Json(new JsonObject { ["results"] = results });
    }

    private async Task<float[][]?> FetchEmbeddingsUpstreamAsync(
        ModelEndpoint upstream,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = upstream.Model,
            ["input"] = new JsonArray(texts.Select(text => JsonValue.Create(text)).ToArray()),
        };
        using var response = await SendUpstreamAsync(upstream, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var root = (await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken))?.AsObject();
        var data = root?["data"]?.AsArray();
        if (data is null || data.Count != texts.Count)
            return null;

        var result = new float[texts.Count][];
        foreach (var item in data)
        {
            var index = item?["index"]?.GetValue<int>() ?? -1;
            var values = item?["embedding"]?.AsArray();
            if (index < 0 || index >= texts.Count || values is null)
                return null;
            var vector = new float[values.Count];
            for (var i = 0; i < values.Count; i++)
                vector[i] = values[i]!.GetValue<float>();
            result[index] = vector;
        }
        return result.Any(vector => vector is null) ? null : result;
    }

    private async Task<double[]?> FetchRerankUpstreamAsync(
        ModelEndpoint upstream,
        string query,
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken)
    {
        JsonObject payload = upstream.Provider == ModelProvider.Cloudflare
            ? new JsonObject
            {
                ["query"] = query,
                ["contexts"] = new JsonArray(documents.Select(doc => (JsonNode)new JsonObject { ["text"] = doc }).ToArray()),
            }
            : new JsonObject
            {
                ["model"] = upstream.Model,
                ["query"] = query,
                ["documents"] = new JsonArray(documents.Select(doc => JsonValue.Create(doc)).ToArray()),
            };

        using var response = await SendUpstreamAsync(upstream, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var root = (await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken))?.AsObject();
        var (items, indexKey, scoreKey) = upstream.Provider == ModelProvider.Cloudflare
            ? (root?["result"]?["response"]?.AsArray(), "id", "score")
            : (root?["results"]?.AsArray(), "index", "relevance_score");
        return ParseScores(items, documents.Count, indexKey, scoreKey);
    }

    private static double[]? ParseScores(JsonArray? items, int count, string indexKey, string scoreKey)
    {
        if (items is null)
            return null;
        var scores = new double[count];
        var filled = new bool[count];
        foreach (var item in items)
        {
            var index = item?[indexKey]?.GetValue<int>() ?? -1;
            var score = item?[scoreKey]?.GetValue<double>();
            if (index < 0 || index >= count || score is null || filled[index])
                return null;
            scores[index] = score.Value;
            filled[index] = true;
        }

        return filled.Any(done => !done) ? null : scores;
    }

    private async Task<HttpResponseMessage> SendUpstreamAsync(ModelEndpoint upstream, JsonObject payload, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, upstream.Url)
        {
            Content = new StringContent(payload.ToJsonString(JsonSerializerOptions.Default), new UTF8Encoding(false), "application/json"),
        };
        if (!string.IsNullOrEmpty(upstream.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", upstream.ApiKey);
        return await _http.SendAsync(message, cancellationToken);
    }

    private float[]? TryGetEmbedding(string key)
    {
        lock (_dbGate)
        {
            if (_db is null)
                return null;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT vector FROM embedding_cache WHERE cache_key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            if (cmd.ExecuteScalar() is not byte[] bytes)
                return null;
            return MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
        }
    }

    private void StoreEmbeddings(IReadOnlyList<(string Key, float[] Vector)> items) =>
        StoreBatch(
            "INSERT INTO embedding_cache (cache_key, vector) VALUES ($key, $val) ON CONFLICT(cache_key) DO UPDATE SET vector = excluded.vector",
            SqliteType.Blob,
            items.Select(item => (item.Key, (object)MemoryMarshal.AsBytes(item.Vector.AsSpan()).ToArray())));

    private double? TryGetRerankScore(string key)
    {
        lock (_dbGate)
        {
            if (_db is null)
                return null;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT score FROM rerank_cache WHERE cache_key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() is double score ? score : null;
        }
    }

    private void StoreRerankScores(IReadOnlyList<(string Key, double Score)> items) =>
        StoreBatch(
            "INSERT INTO rerank_cache (cache_key, score) VALUES ($key, $val) ON CONFLICT(cache_key) DO UPDATE SET score = excluded.score",
            SqliteType.Real,
            items.Select(item => (item.Key, (object)item.Score)));

    private void StoreBatch(string sql, SqliteType valueType, IEnumerable<(string Key, object Value)> items)
    {
        lock (_dbGate)
        {
            if (_db is null)
                return;
            using var tx = _db.BeginTransaction();
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            var keyParam = cmd.Parameters.Add("$key", SqliteType.Text);
            var valParam = cmd.Parameters.Add("$val", valueType);
            foreach (var (key, value) in items)
            {
                keyParam.Value = key;
                valParam.Value = value;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    private static string CacheKey(params string[] parts)
    {
        var joined = string.Join('\0', parts);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }
}
