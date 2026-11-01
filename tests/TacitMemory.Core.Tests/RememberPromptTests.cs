using System.Security.Cryptography;
using System.Text;

namespace TacitMemory.Core.Tests;

public sealed class RememberPromptTests : IDisposable
{
    private readonly TestHome _home = new();
    private readonly MemoryService _service;

    public RememberPromptTests()
    {
        _home.InitReady();
        _service = _home.Tacit().OpenService(_ => { });
    }

    public void Dispose()
    {
        _service.Dispose();
        _home.Dispose();
    }

    private static string ShortOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n").Trim())))[..6].ToLowerInvariant();

    private static string VersionOf(params string[] items) => ShortOf(string.Join("\n", items));

    [Fact]
    public async Task The_prompt_lists_today_s_activity_with_its_code_and_the_mainline_with_its_version()
    {
        await _service.WriteRecentAsync(new WriteRecentRequest { Op = "add_activity", Text = "想清楚了画像里缺的是人的倾向" });
        await _service.WriteRecentAsync(new WriteRecentRequest
        {
            Op = "update_mainline",
            Items = ["主线一", "主线二"],
            Hash = VersionOf(),
        });

        var prompt = _service.RememberPrompt();

        Assert.Contains($"[{ShortOf("想清楚了画像里缺的是人的倾向")}] 想清楚了画像里缺的是人的倾向", prompt);
        Assert.Contains($"Mainline, version [{VersionOf("主线一", "主线二")}]:\n- 主线一\n- 主线二", prompt);
    }

    [Fact]
    public void The_focus_is_included_when_given_and_absent_otherwise()
    {
        Assert.Contains("检索策略调整", _service.RememberPrompt("检索策略调整"));
        Assert.DoesNotContain("检索策略调整", _service.RememberPrompt(null));
    }

    [Fact]
    public void The_prompt_includes_the_persona_write_step()
    {
        var prompt = _service.RememberPrompt();

        Assert.Contains("write_persona", prompt);
        Assert.Contains("propose", prompt);
    }
}
