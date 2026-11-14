using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TacitMemory.Core.Tests;

/// <summary>
/// The three auxiliary models on 127.0.0.1, answering from what each test sets: <c>/v1/embeddings</c>,
/// <c>/v1/rerank</c> and <c>/v1/systemone</c>, and Cloudflare's format of those two at <c>/ai/run/&lt;model&gt;</c>.
/// Configuration gives each model its full URL, so these are the URLs a test
/// writes. A model that is down answers <see cref="DownStatus"/> at once; that is a client error, which the service does
/// not retry, so tests that are not about retries never wait on the clock. The scripts make the next requests of one
/// model answer otherwise, in order, before it answers as set. Every request is recorded with its path, its
/// Authorization header and its JSON body.
/// </summary>
internal sealed class ModelDouble : IDisposable
{
    /// <summary>What a model that is down answers: a client error, which is not retried.</summary>
    public const int DownStatus = 404;

    /// <summary>The model Cloudflare's reranker answers for, in the route <c>/ai/run/&lt;model&gt;</c>.</summary>
    public const string CloudflareRerankModel = "@cf/baai/bge-reranker-base";

    /// <summary>The model Cloudflare's decision model answers for, in the route <c>/ai/run/&lt;model&gt;</c>.</summary>
    public const string CloudflareDecisionModel = "clef-flash";

    private static readonly Lazy<ModelDouble> SharedQuiet = new(() => new ModelDouble(shared: true) { EmbeddingDown = true, RerankDown = true });

    private readonly WebApplication _app;
    private readonly bool _shared;
    private readonly CancellationTokenSource _closing = new();
    private readonly ConcurrentQueue<string> _rerankedDocuments = new();
    private readonly ConcurrentQueue<ModelReply> _embeddingScript = new();
    private readonly ConcurrentQueue<ModelReply> _rerankScript = new();
    private readonly ConcurrentQueue<ModelReply> _decisionScript = new();
    private readonly ConcurrentQueue<ReceivedRequest> _embeddingReceived = new();
    private readonly ConcurrentQueue<ReceivedRequest> _rerankReceived = new();
    private readonly ConcurrentQueue<ReceivedRequest> _decisionReceived = new();
    private readonly RequestCount _embeddingRequests = new();
    private readonly RequestCount _rerankRequests = new();
    private readonly RequestCount _decisionRequests = new();
    private readonly Lock _decisionGate = new();
    private TaskCompletionSource? _decisionHold;
    private readonly Lock _embeddingGate = new();
    private TaskCompletionSource? _embeddingHold;
    private int _decisionsInFlight;
    private int _peakDecisionsInFlight;
    private int _abandonedRequests;

    public ModelDouble()
        : this(shared: false)
    {
    }

    private ModelDouble(bool shared)
    {
        _shared = shared;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            // A request the client gave up on before the double answered: its connection failed or closed mid-request.
            var answered = new[] { false };
            context.Response.OnCompleted(() =>
            {
                Volatile.Write(ref answered[0], true);
                return Task.CompletedTask;
            });
            using var abandoned = context.RequestAborted.Register(() =>
            {
                if (!Volatile.Read(ref answered[0]))
                    Interlocked.Increment(ref _abandonedRequests);
            });
            await next(context);
        });
        _app.MapPost("/v1/embeddings", EmbeddingsAsync);
        _app.MapPost("/v1/rerank", (HttpRequest request) => RerankAsync(request, cloudflare: false));
        _app.MapPost("/v1/systemone", (HttpRequest request) => DecideAsync(request, cloudflare: false));
        _app.MapPost("/ai/run/" + CloudflareRerankModel, (HttpRequest request) => RerankAsync(request, cloudflare: true));
        _app.MapPost("/ai/run/" + CloudflareDecisionModel, (HttpRequest request) => DecideAsync(request, cloudflare: true));
        _app.StartAsync().GetAwaiter().GetResult();
        var root = new Uri(_app.Urls.Single());
        EmbeddingUrl = new Uri(root, "/v1/embeddings");
        RerankUrl = new Uri(root, "/v1/rerank");
        DecisionUrl = new Uri(root, "/v1/systemone");
        CloudflareRerankUrl = new Uri(root, "/ai/run/" + CloudflareRerankModel);
        CloudflareDecisionUrl = new Uri(root, "/ai/run/" + CloudflareDecisionModel);
    }

    /// <summary>
    /// The default for tests that do not exercise the models: the embedding model and the reranker are down, and the
    /// decision model answers "none" to every choice, so writes are never duplicates.
    /// </summary>
    public static ModelDouble Quiet => SharedQuiet.Value;

    /// <summary>The full URL the embedding model answers at; what configuration writes as <c>url</c>.</summary>
    public Uri EmbeddingUrl { get; }

    /// <summary>The full URL the reranker answers at.</summary>
    public Uri RerankUrl { get; }

    /// <summary>The full URL the decision model answers at.</summary>
    public Uri DecisionUrl { get; }

    /// <summary>The full URL of the reranker in Cloudflare's format; configuration writes it as <c>url</c> with <c>provider: cloudflare</c>.</summary>
    public Uri CloudflareRerankUrl { get; }

    /// <summary>The full URL of the decision model in Cloudflare's format.</summary>
    public Uri CloudflareDecisionUrl { get; }

    /// <summary>
    /// When set, the Cloudflare routes answer 200 with <c>success</c> false and no <c>result</c>, which is what Cloudflare
    /// sends when it refuses a request.
    /// </summary>
    public bool CloudflareReportsFailure { get; set; }

    public bool EmbeddingDown { get; set; }

    public bool RerankDown { get; set; }

    /// <summary>The relevance of a document to a query; by default every document is fully relevant.</summary>
    public Func<string, string, double> Score { get; set; } = (_, _) => 1.0;

    /// <summary>Every document the reranker was sent, in order.</summary>
    public IReadOnlyCollection<string> RerankedDocuments => _rerankedDocuments;

    public int RerankCalls => _rerankRequests.Count;

    /// <summary>The vector for a text; by default a bag of its character pairs, so similar wording is near.</summary>
    public Func<string, float[]> Embed { get; set; } = BagOfPairs;

    public int EmbeddingCalls => _embeddingRequests.Count;

    public bool DecisionDown { get; set; }

    /// <summary>Probabilities for a choice question, given the state and the options by key; by default the last option, "none".</summary>
    public Func<string, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, double>> Choose { get; set; } =
        (_, options) => options.Keys.ToDictionary(key => key, key => key == options.Keys.Last() ? 1.0 : 0.0);

    public int DecisionCalls => _decisionRequests.Count;

    /// <summary>
    /// Requests the client gave up on before the double answered: its connection failed or closed mid-request. A test
    /// that advances the clock asserts this is 0, so a dropped connection fails it by name instead of by a second call.
    /// </summary>
    public int AbandonedRequests => Volatile.Read(ref _abandonedRequests);

    /// <summary>The most decision requests that were being answered at the same time.</summary>
    public int PeakDecisionConcurrency
    {
        get
        {
            lock (_decisionGate)
                return _peakDecisionsInFlight;
        }
    }

    /// <summary>Every request the embedding model received, in order, scripted ones included.</summary>
    public IReadOnlyList<ReceivedRequest> EmbeddingRequests => _embeddingReceived.ToArray();

    /// <summary>Every request the reranker received, in order, scripted ones included.</summary>
    public IReadOnlyList<ReceivedRequest> RerankRequests => _rerankReceived.ToArray();

    /// <summary>Every request the decision model received, in order, scripted ones included.</summary>
    public IReadOnlyList<ReceivedRequest> DecisionRequests => _decisionReceived.ToArray();

    /// <summary>Completes once the decision model has received <paramref name="number"/> requests, the count including the scripted ones.</summary>
    public Task DecisionRequestsReached(int number) => _decisionRequests.Reached(number);

    /// <summary>Holds every decision answer, as a slow model would, until <see cref="ReleaseDecisions"/>.</summary>
    public void HoldDecisions()
    {
        lock (_decisionGate)
            _decisionHold ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Lets the held decision answers through, and every later one.</summary>
    public void ReleaseDecisions()
    {
        TaskCompletionSource? hold;
        lock (_decisionGate)
        {
            hold = _decisionHold;
            _decisionHold = null;
        }
        hold?.TrySetResult();
    }

    /// <summary>Holds every embedding answer, as a slow model would, until <see cref="ReleaseEmbeddings"/>.</summary>
    public void HoldEmbeddings()
    {
        lock (_embeddingGate)
            _embeddingHold ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Lets the held embedding answers through, and every later one.</summary>
    public void ReleaseEmbeddings()
    {
        TaskCompletionSource? hold;
        lock (_embeddingGate)
        {
            hold = _embeddingHold;
            _embeddingHold = null;
        }
        hold?.TrySetResult();
    }

    /// <summary>The body and the Authorization header of the latest decision request.</summary>
    public (JsonObject Body, string? Authorization)? LastDecision =>
        DecisionRequests.LastOrDefault() is { } last ? (last.Body, last.Authorization) : null;

    /// <summary>Completes once the embedding model has received <paramref name="number"/> requests, the count including the scripted ones.</summary>
    public Task EmbeddingRequestsReached(int number) => _embeddingRequests.Reached(number);

    /// <summary>Replies to the next embedding requests, in order, before the model answers as set.</summary>
    public void ScriptEmbedding(params ModelReply[] replies) => Enqueue(_embeddingScript, replies);

    /// <summary>Replies to the next reranker requests, in order, before the model answers as set.</summary>
    public void ScriptRerank(params ModelReply[] replies) => Enqueue(_rerankScript, replies);

    /// <summary>Replies to the next decision requests, in order, before the model answers as set.</summary>
    public void ScriptDecision(params ModelReply[] replies) => Enqueue(_decisionScript, replies);

    /// <summary>
    /// A <c>models</c> section pointing every model at this double's full URL, or at Cloudflare's URL for a reranker or
    /// decision model set to <c>cloudflare</c>. The decision model's key is given as <paramref name="decisionKey"/>; an
    /// empty one writes no key. The embedding and reranker keys are written only when given, so by default those models
    /// send no authentication.
    /// </summary>
    public string ModelsYaml(
        string embeddingModel = "double-embedding", string decisionKey = "test-key", string? embeddingKey = null, string? rerankerKey = null,
        bool embeddingCloudflare = false, bool rerankerCloudflare = false, bool decisionCloudflare = false) =>
        $$"""
        models:
          embedding: { url: '{{EmbeddingUrl}}', model: {{embeddingModel}}{{ProviderPart(embeddingCloudflare)}}{{KeyPart(embeddingKey)}} }
          reranker: { url: '{{(rerankerCloudflare ? CloudflareRerankUrl : RerankUrl)}}', model: double-reranker{{ProviderPart(rerankerCloudflare)}}{{KeyPart(rerankerKey)}} }
          decision: { url: '{{(decisionCloudflare ? CloudflareDecisionUrl : DecisionUrl)}}', model: double-decision{{ProviderPart(decisionCloudflare)}}{{KeyPart(decisionKey)}} }

        """;

    public void Dispose()
    {
        if (_shared)
            return;
        _closing.Cancel();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _closing.Dispose();
    }

    public static float[] BagOfPairs(string text)
    {
        var vector = new float[64];
        for (var i = 0; i + 1 < text.Length; i++)
            vector[(int)((uint)HashCode.Combine(text[i], text[i + 1]) % 64)] += 1;
        var length = MathF.Sqrt(vector.Sum(value => value * value));
        return length == 0 ? [1, .. new float[63]] : vector.Select(value => value / length).ToArray();
    }

    private static string KeyPart(string? key) => string.IsNullOrEmpty(key) ? "" : $", apiKey: {key}";

    private static string ProviderPart(bool cloudflare) => cloudflare ? ", provider: cloudflare" : "";

    private static void Enqueue(ConcurrentQueue<ModelReply> script, IEnumerable<ModelReply> replies)
    {
        foreach (var reply in replies)
            script.Enqueue(reply);
    }

    /// <summary>Records one request the model received, with its path, its Authorization header and its body.</summary>
    private static async Task<JsonObject> RecordAsync(HttpRequest request, ConcurrentQueue<ReceivedRequest> received)
    {
        var body = (await JsonNode.ParseAsync(request.Body))!.AsObject();
        string? authorization = request.Headers.Authorization;
        received.Enqueue(new ReceivedRequest(request.Path.Value ?? "", authorization, body));
        return body;
    }

    private async Task<IResult?> ScriptedAsync(ConcurrentQueue<ModelReply> script, HttpContext http) =>
        script.TryDequeue(out var reply) ? await reply.ServeAsync(http, _closing.Token) : null;

    private async Task<IResult> EmbeddingsAsync(HttpRequest request)
    {
        _embeddingRequests.Increment();
        var body = await RecordAsync(request, _embeddingReceived);
        Task? hold;
        lock (_embeddingGate)
            hold = _embeddingHold?.Task;
        if (hold is not null)
            await hold.WaitAsync(_closing.Token);
        if (await ScriptedAsync(_embeddingScript, request.HttpContext) is { } scripted)
            return scripted;
        if (EmbeddingDown)
            return Results.StatusCode(DownStatus);
        var inputs = body["input"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        var data = new JsonArray();
        for (var i = 0; i < inputs.Count; i++)
            data.Add(new JsonObject { ["index"] = i, ["embedding"] = new JsonArray(Embed(inputs[i]).Select(value => (JsonNode)value).ToArray()) });
        return Results.Json(new JsonObject { ["data"] = data });
    }

    /// <summary>The reranker: the generic protocol, or Cloudflare's format, which names its documents as contexts and its scores by id.</summary>
    private async Task<IResult> RerankAsync(HttpRequest request, bool cloudflare)
    {
        _rerankRequests.Increment();
        var body = await RecordAsync(request, _rerankReceived);
        if (await ScriptedAsync(_rerankScript, request.HttpContext) is { } scripted)
            return scripted;
        if (RerankDown)
            return Results.StatusCode(DownStatus);
        var query = body["query"]!.GetValue<string>();
        var documents = (cloudflare
            ? body["contexts"]!.AsArray().Select(node => node!["text"]!.GetValue<string>())
            : body["documents"]!.AsArray().Select(node => node!.GetValue<string>())).ToList();
        foreach (var document in documents)
            _rerankedDocuments.Enqueue(document);
        // Like the real reranker: one result per document, best first, each naming its document by index.
        var ranked = documents
            .Select((document, index) => (index, score: Score(query, document)))
            .OrderByDescending(item => item.score)
            .ToList();
        if (cloudflare)
        {
            var response = new JsonArray(ranked.Select(item => (JsonNode)new JsonObject { ["id"] = item.index, ["score"] = item.score }).ToArray());
            return CloudflareAnswer(new JsonObject { ["response"] = response });
        }
        var results = ranked.Select(item => (JsonNode)new JsonObject { ["index"] = item.index, ["relevance_score"] = item.score });
        return Results.Json(new JsonObject { ["results"] = new JsonArray(results.ToArray()) });
    }

    private async Task<IResult> DecideAsync(HttpRequest request, bool cloudflare)
    {
        _decisionRequests.Increment();
        lock (_decisionGate)
        {
            _decisionsInFlight++;
            _peakDecisionsInFlight = Math.Max(_peakDecisionsInFlight, _decisionsInFlight);
        }
        try
        {
            return await AnswerDecisionAsync(request, cloudflare);
        }
        finally
        {
            lock (_decisionGate)
                _decisionsInFlight--;
        }
    }

    private async Task<IResult> AnswerDecisionAsync(HttpRequest request, bool cloudflare)
    {
        var body = await RecordAsync(request, _decisionReceived);
        Task? hold;
        lock (_decisionGate)
            hold = _decisionHold?.Task;
        if (hold is not null)
            await hold.WaitAsync(_closing.Token);
        if (await ScriptedAsync(_decisionScript, request.HttpContext) is { } scripted)
            return scripted;
        if (DecisionDown)
            return Results.StatusCode(DownStatus);
        var answers = new JsonObject();
        foreach (var (id, node) in body["questions"]!.AsObject())
        {
            var question = node!.AsObject();
            var state = body["state"]!.GetValue<string>();
            answers[id] = question["type"]!.GetValue<string>() switch
            {
                "choice" => Choice(state, question["criteria"]!.AsObject()),
                var type => throw new InvalidOperationException($"The double does not answer {type} questions."),
            };
        }

        var reply = new JsonObject { ["model"] = "double-decision", ["answers"] = answers };
        return cloudflare ? CloudflareAnswer(reply) : Results.Json(reply);
    }

    /// <summary>Cloudflare's envelope around <paramref name="result"/>; when the double is set to fail, a refusal with no result.</summary>
    private IResult CloudflareAnswer(JsonObject result) => CloudflareReportsFailure
        ? Results.Json(new JsonObject
        {
            ["success"] = false,
            ["errors"] = new JsonArray(new JsonObject { ["code"] = 7000, ["message"] = "The double refuses the request." }),
            ["messages"] = new JsonArray(),
        })
        : Results.Json(new JsonObject { ["result"] = result, ["success"] = true, ["errors"] = new JsonArray(), ["messages"] = new JsonArray() });

    private JsonObject Choice(string state, JsonObject criteria)
    {
        var options = criteria.ToDictionary(option => option.Key, option => option.Value!.GetValue<string>());
        var probabilities = new JsonObject();
        foreach (var (key, probability) in Choose(state, options))
            probabilities[key] = probability;
        return new JsonObject { ["type"] = "choice", ["probabilities"] = probabilities };
    }

    /// <summary>One request a model received: its path, its Authorization header when it sent one, and its JSON body.</summary>
    public sealed record ReceivedRequest(string Path, string? Authorization, JsonObject Body);

    /// <summary>Counts the requests a model received, and lets a test wait for the n-th one.</summary>
    private sealed class RequestCount
    {
        private readonly Lock _gate = new();
        private readonly List<(int Number, TaskCompletionSource Reached)> _waiters = [];
        private int _count;

        public int Count
        {
            get
            {
                lock (_gate)
                    return _count;
            }
        }

        public void Increment()
        {
            lock (_gate)
            {
                _count++;
                foreach (var waiter in _waiters.Where(waiter => waiter.Number <= _count).ToList())
                {
                    _waiters.Remove(waiter);
                    waiter.Reached.SetResult();
                }
            }
        }

        public Task Reached(int number)
        {
            lock (_gate)
            {
                if (_count >= number)
                    return Task.CompletedTask;
                var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((number, reached));
                return reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }
}

/// <summary>
/// One scripted answer of a model: a status, with an optional <c>Retry-After</c> in seconds; a body; a dropped
/// connection, which the client sees as a network error; or a hang, which ends only when the client gives up.
/// </summary>
internal sealed class ModelReply
{
    private readonly Func<HttpContext, CancellationToken, Task<IResult>> _serve;

    private ModelReply(Func<HttpContext, CancellationToken, Task<IResult>> serve) => _serve = serve;

    public static ModelReply Status(int code, int? retryAfterSeconds = null) => new(async (http, _) =>
    {
        if (retryAfterSeconds is { } seconds)
            http.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
        return Results.StatusCode(code);
    });

    /// <summary>A 200 answer with this body, for answers that are not what was asked for.</summary>
    public static ModelReply Body(string json) => new((_, _) => Task.FromResult<IResult>(Results.Content(json, "application/json")));

    /// <summary>An answer with this status and this body, for a request that fails and whose body the probe reports.</summary>
    public static ModelReply StatusWithBody(int code, string body) =>
        new((_, _) => Task.FromResult<IResult>(Results.Content(body, "application/json", statusCode: code)));

    public static ModelReply Drop() => new((http, _) =>
    {
        http.Abort();
        return Task.FromResult<IResult>(Results.Empty);
    });

    public static ModelReply Hang() => new(async (http, closing) =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, closing);
        try
        {
            await Task.Delay(Timeout.Infinite, linked.Token);
        }
        catch (OperationCanceledException)
        {
            // The client gave up or the double is closing.
        }
        return Results.Empty;
    });

    internal Task<IResult> ServeAsync(HttpContext http, CancellationToken closing) => _serve(http, closing);
}
