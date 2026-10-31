using System.Diagnostics;
using System.Globalization;

namespace TacitMemory.Core.Tests;

/// <summary>A throwaway user home and <c>TACIT_HOME</c> under %TEMP%.</summary>
internal sealed class TestHome : IDisposable
{
    public TestHome()
    {
        Root = Path.Combine(Path.GetTempPath(), "tacit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(UserHome);
    }

    public string Root { get; }

    public string UserHome => Path.Combine(Root, "home");

    public string TacitHome => Path.Combine(UserHome, ".tacit");

    /// <summary>Where <c>init</c> puts the memory repository unless told otherwise.</summary>
    public string DefaultMemory => Path.Combine(TacitHome, "memory");

    public string PathFor(string name) => Path.Combine(Root, name);

    public Tacit Tacit(string uiCulture = "zh-CN") =>
        new(new TacitOptions { TacitHome = TacitHome, UserHome = UserHome, UiCulture = CultureInfo.GetCultureInfo(uiCulture) });

    /// <summary>Runs <c>git</c> in <paramref name="directory"/> and returns its standard output.</summary>
    public static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in (string[])["-c", "core.quotepath=off", .. args])
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
        return output;
    }

    public void Dispose()
    {
        try
        {
            // Git marks its object files read-only, which Directory.Delete refuses.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the system to clean up.
        }
    }
}
