using Microsoft.Extensions.Logging.Abstractions;

namespace TacitMemory.Cli.Tests;

/// <summary>
/// With an access password set, the commands that go through the management API still work with no credentials, since they
/// run on this machine. <c>serve</c> refuses to start a <c>server.host</c> that is not loopback when no password is set,
/// and exits 3 the way any other config refusal does.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class AccessPasswordCommandTests : IDisposable
{
    private const string Password = "correct horse";

    private readonly CliHarness _cli = new();

    public AccessPasswordCommandTests() => _cli.InitReady();

    public void Dispose() => _cli.Dispose();

    /// <summary>Sets the access password in the state file, as the service's own API would from this machine.</summary>
    private void SetPassword() =>
        _cli.Home.Tacit().OpenAccess(NullLoggerFactory.Instance).SetPassword(Password, current: null, local: true);

    [Fact]
    public async Task Status_works_with_an_access_password_set_and_needs_no_credentials()
    {
        SetPassword();
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["status"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("运行中", result.Stdout);
        Assert.DoesNotContain(Password, result.Stdout + result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reindex_works_with_an_access_password_set()
    {
        SetPassword();
        await _cli.StartServiceAsync();

        var result = await _cli.RunAsync(["reindex"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task Backup_works_with_an_access_password_set_and_the_backup_leaves_the_state_file_out()
    {
        SetPassword();
        await _cli.StartServiceAsync();
        var directory = _cli.Home.PathFor("backups-out");

        var result = await _cli.RunAsync(["backup", "--to", directory]);

        Assert.Equal(0, result.ExitCode);
        var zip = Assert.Single(Directory.GetFiles(directory, "*.zip"));
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("access.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_works_with_an_access_password_set_and_keeps_the_password()
    {
        SetPassword();
        await _cli.StartServiceAsync();
        var directory = _cli.Home.PathFor("backups-out");
        Assert.Equal(0, (await _cli.RunAsync(["backup", "--to", directory])).ExitCode);
        var zip = Directory.GetFiles(directory, "*.zip").Single();

        var result = await _cli.RunAsync(["restore", "--from", zip, "--yes"]);

        Assert.Equal(0, result.ExitCode);
        Assert.True(_cli.Home.Tacit().OpenAccess(NullLoggerFactory.Instance).PasswordSet);
    }

    [Fact]
    public async Task Serve_with_server_host_beside_loopback_and_no_access_password_exits_3_and_names_the_key()
    {
        _cli.Home.InitReady(extraConfig: $"server:\n  port: {_cli.Port}\n  host: 127.0.0.2\n");

        var result = await _cli.RunAsync(["serve"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("server.host", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, result.Stdout + result.Stderr, StringComparison.Ordinal);
    }
}
