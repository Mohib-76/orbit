using System.IO;
using Orbit.Core;
using Xunit;

namespace Orbit.Tests;

public class LinkTests
{
    [Theory]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("  http://example.com  ", "http://example.com/")]
    [InlineData("file:///C:/Windows/System32/calc.exe", null)]
    [InlineData("ms-settings:privacy", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("example.com", null)]
    [InlineData("", null)]
    public void OnlyWebLinksAreAccepted(string input, string? expected) => Assert.Equal(expected, Links.Normalize(input));

    [Fact]
    public void ClipboardMustBeJustALink()
    {
        Assert.Equal("https://maps.example/x", Links.FromClipboard(" https://maps.example/x\n"));
        Assert.Null(Links.FromClipboard("see https://maps.example/x"));
        Assert.Null(Links.FromClipboard("hello"));
    }

    [Fact]
    public void FindsTheLinkInSharedText()
    {
        Assert.Equal("https://longreads.example/local-first",
            Links.Find("The quiet rise of local-first software https://longreads.example/local-first"));
        Assert.Equal("https://example.com/a", Links.Find("(see https://example.com/a)."));
        Assert.Null(Links.Find("no link here"));
    }

    [Fact]
    public void ShortFormDropsSchemeAndWww() =>
        Assert.Equal("example.com/path", Links.Short("https://www.example.com/path/"));

    [Fact]
    public void LongLinksAreRefused() => Assert.Null(Links.Normalize("https://example.com/" + new string('a', Links.MaxLength)));
}

public class RemoteCommandTests
{
    [Fact]
    public void CommandLinesRunHiddenThroughCmd()
    {
        var psi = OrbitCore.CommandStartInfo("start notepad");
        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal("/d /s /c \"start notepad\"", psi.Arguments);
        Assert.True(psi.CreateNoWindow);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void PowerShellScriptsRunInPowerShell()
    {
        var script = Path.Combine(Path.GetTempPath(), $"orbit-test-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, "exit 0");
        try
        {
            var psi = OrbitCore.CommandStartInfo($"\"{script}\"");
            Assert.Equal("powershell.exe", psi.FileName);
            Assert.Contains($"-File \"{script}\"", psi.Arguments);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public void OtherFilesOpenWithTheirApp()
    {
        var file = Path.Combine(Path.GetTempPath(), $"orbit-test-{Guid.NewGuid():N}.bat");
        File.WriteAllText(file, "@echo off");
        try
        {
            var psi = OrbitCore.CommandStartInfo(file);
            Assert.Equal(file, psi.FileName);
            Assert.True(psi.UseShellExecute);
        }
        finally { File.Delete(file); }
    }
}

public class InputTests
{
    [Theory]
    [InlineData("next", "right")]
    [InlineData("prev", "left")]
    [InlineData("start", "f5")]
    [InlineData("end", "esc")]
    [InlineData("black", "b")]
    public void SlidesUseTheStandardPresentationKeys(string op, string key) => Assert.Equal(key, OrbitCore.SlidesKey(op)?.key);

    [Fact]
    public void UnknownSlidesOpsAreIgnored() => Assert.Null(OrbitCore.SlidesKey("shutdown"));

    [Theory]
    [InlineData("esc", 0x1B)]
    [InlineData("Delete", 0x2E)]
    [InlineData("left", 0x25)]
    [InlineData("c", 'C')]
    [InlineData("7", '7')]
    [InlineData("f5", 0x74)]
    public void KeyNamesMapToVirtualKeys(string name, int vk) => Assert.Equal((ushort)vk, InputInjector.KeyCode(name));

    [Theory]
    [InlineData("ctrl")]
    [InlineData("é")]
    [InlineData("rm -rf")]
    public void OtherKeyNamesAreRefused(string name) => Assert.Null(InputInjector.KeyCode(name));
}

public class SlidesStartTests
{
    [Fact]
    public void StartIsCtrlF5InABrowser()
    {
        Assert.Empty(OrbitCore.SlidesKey("start")!.Value.mods);
        Assert.Equal(new[] { "ctrl" }, OrbitCore.SlidesKey("start", browser: true)!.Value.mods);
    }
}
