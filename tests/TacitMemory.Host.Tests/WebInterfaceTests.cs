using System.Net;
using System.Text;
using TacitMemory.Core;
using TacitMemory.Core.Tests;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The web interface under <c>/ui</c>: files from the web directory beside the executable, the entry page for every other
/// path under it, a 404 for a missing file with an extension, the redirect from <c>/</c>, and the cache headers. A test passes
/// a directory of its own as the web directory, so no real build is needed.
/// </summary>
public sealed class WebInterfaceTests : IAsyncDisposable, IDisposable
{
    private const string EntryPage = "<!doctype html><title>entry</title>";
    private const string Password = "correct horse";

    private readonly TestHome _home = new();
    private readonly ModelDouble _models = new() { EmbeddingDown = true, RerankDown = true };
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "tacit-web-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];
    private readonly List<IAsyncDisposable> _asyncDisposables = [];

    private string WebRoot => Path.Combine(_sandbox, "web");

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _models.Dispose();
        _home.Dispose();
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _asyncDisposables)
            await disposable.DisposeAsync();
        Dispose();
    }

    /// <summary>Writes a built web directory: an entry page, one hashed asset and one plain file.</summary>
    private void BuildWebDirectory()
    {
        Directory.CreateDirectory(Path.Combine(WebRoot, "assets"));
        File.WriteAllText(Path.Combine(WebRoot, "index.html"), EntryPage, Encoding.UTF8);
        File.WriteAllText(Path.Combine(WebRoot, "assets", "app-4f3c2b.js"), "console.log('app');", Encoding.UTF8);
        File.WriteAllText(Path.Combine(WebRoot, "favicon.txt"), "icon", Encoding.UTF8);
    }

    /// <summary>
    /// Starts a service on a free port. <paramref name="extraConfig"/> may name a <c>server.host</c>, which needs an access
    /// password first, the same as in the listener tests.
    /// </summary>
    private async Task<ServiceHost> StartAsync(string extraConfig = "")
    {
        _home.InitReady(extraConfig, models: _models);
        var service = _home.Tacit().OpenService(_ => { }, NullLoggerFactory.Instance);
        _disposables.Add(service);
        if (extraConfig.Length > 0)
            service.Access.SetPassword(Password, current: null, local: true, IPAddress.Loopback);
        var host = await ServiceHost.TryStartAsync(
            service, port: 0, NullLoggerProvider.Instance, PeerClients.OnlyLoopbackIsLocal, webRoot: WebRoot)
            ?? throw new InvalidOperationException("Port 0 is never in use.");
        _asyncDisposables.Add(host);
        return host;
    }

    private static Uri Url(ServiceHost host, string path) => new($"http://127.0.0.1:{host.Endpoint.Port}{path}");

    [Fact]
    public async Task A_built_directory_serves_its_files_under_ui_with_their_content_type()
    {
        BuildWebDirectory();
        var host = await StartAsync();
        using var client = PeerClients.Local();

        using var response = await client.GetAsync(Url(host, "/ui/assets/app-4f3c2b.js"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("console.log('app');", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_ui_root_and_paths_without_a_file_return_the_entry_page()
    {
        BuildWebDirectory();
        var host = await StartAsync();
        using var client = PeerClients.Local();

        foreach (var path in new[] { "/ui", "/ui/", "/ui/settings", "/ui/settings/access" })
        {
            using var response = await client.GetAsync(Url(host, path));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}");
            Assert.Equal(EntryPage, await response.Content.ReadAsStringAsync());
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task A_missing_file_with_an_extension_is_404_and_not_the_entry_page()
    {
        BuildWebDirectory();
        var host = await StartAsync();
        using var client = PeerClients.Local();

        using var response = await client.GetAsync(Url(host, "/ui/assets/missing-0000.js"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("entry", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/ui/..%2Fsecret.txt")]
    [InlineData("/ui/assets/..%2F..%2Fsecret.txt")]
    [InlineData("/ui/..%5Csecret.txt")]
    [InlineData("/ui/..%2Fweb-sibling%2Fleak.txt")]
    [InlineData("/ui/..%2Fweb%2F..%2Fsecret.txt")]
    public async Task Paths_outside_the_web_directory_are_never_read(string path)
    {
        BuildWebDirectory();
        // A file beside the web directory, and a sibling directory whose name starts with the web directory's name.
        File.WriteAllText(Path.Combine(_sandbox, "secret.txt"), "TOP SECRET");
        Directory.CreateDirectory(Path.Combine(_sandbox, "web-sibling"));
        File.WriteAllText(Path.Combine(_sandbox, "web-sibling", "leak.txt"), "TOP SECRET");
        var host = await StartAsync();
        using var client = PeerClients.Local();

        using var response = await client.GetAsync(Url(host, path));
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("TOP SECRET", body, StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_built_directory_every_ui_path_says_the_web_interface_is_not_built()
    {
        // The web directory is never created: the state of a service run from the source tree.
        var host = await StartAsync();
        using var client = PeerClients.Local();

        foreach (var path in new[] { "/ui", "/ui/", "/ui/settings", "/ui/app.js" })
        {
            using var response = await client.GetAsync(Url(host, path));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("Web 界面未构建", await response.Content.ReadAsStringAsync());
        }

        // The rest of the service carries on.
        using var status = await client.GetAsync(Url(host, "/api/status"));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    [Fact]
    public async Task The_root_redirects_to_the_ui_root()
    {
        BuildWebDirectory();
        var host = await StartAsync();
        using var client = PeerClients.Local(followRedirects: false);

        using var response = await client.GetAsync(Url(host, "/"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/ui/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task The_entry_page_is_never_cached_and_hashed_assets_are_cached_for_a_long_time()
    {
        BuildWebDirectory();
        var host = await StartAsync();
        using var client = PeerClients.Local();

        using var entry = await client.GetAsync(Url(host, "/ui/"));
        using var asset = await client.GetAsync(Url(host, "/ui/assets/app-4f3c2b.js"));

        Assert.True(entry.Headers.CacheControl?.NoStore, "the entry page must say no-store");
        var cache = asset.Headers.CacheControl;
        Assert.NotNull(cache);
        Assert.True(cache.Public);
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
    }

    [Fact]
    public async Task The_entry_page_is_no_store_whether_it_is_asked_for_directly_or_as_a_fallback()
    {
        BuildWebDirectory();
        var host = await StartAsync();
        using var client = PeerClients.Local();

        foreach (var path in new[] { "/ui/", "/ui", "/ui/index.html", "/ui/settings" })
        {
            using var response = await client.GetAsync(Url(host, path));
            Assert.True(response.Headers.CacheControl?.NoStore == true, $"{path} must say no-store");
        }
    }

    [Fact]
    public async Task The_ui_prefix_and_the_assets_folder_match_whatever_their_letter_case()
    {
        BuildWebDirectory();
        var host = await StartAsync();
        using var client = PeerClients.Local();

        using var entry = await client.GetAsync(Url(host, "/UI/"));
        using var asset = await client.GetAsync(Url(host, "/Ui/ASSETS/app-4f3c2b.js"));

        Assert.Equal(EntryPage, await entry.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(TimeSpan.FromDays(365), asset.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task A_remote_connection_opens_the_web_interface_and_the_root_without_logging_in()
    {
        BuildWebDirectory();
        var host = await StartAsync("server:\n  host: 127.0.0.2\n");
        using var remote = PeerClients.Remote(followRedirects: false);
        var port = host.Endpoint.Port;

        using var entry = await remote.GetAsync(new Uri($"http://127.0.0.2:{port}/ui"));
        using var root = await remote.GetAsync(new Uri($"http://127.0.0.2:{port}/"));

        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.Equal(EntryPage, await entry.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Found, root.StatusCode);
    }
}
