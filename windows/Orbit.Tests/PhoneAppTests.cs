using Orbit.Core;
using Xunit;

namespace Orbit.Tests;

public class PhoneAppTests
{
    [Theory]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.2.0", "1.1.0", false)]
    [InlineData("1.9.0", "1.10.0", true)]      // numeric, not text, comparison
    [InlineData("1.0.0-beta", "1.0.1", true)]
    [InlineData("1.1.0", "1.1.0+abc123", false)]
    [InlineData("", "1.1.0", false)]            // unknown never counts as older
    [InlineData("garbage", "1.1.0", false)]
    public void ComparesVersions(string phone, string pc, bool older) =>
        Assert.Equal(older, OrbitCore.IsOlder(phone, pc));

    [Theory]
    [InlineData("adb: failed to install x.apk: Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: ...]", "differently signed")]
    [InlineData("Failure [INSTALL_FAILED_VERSION_DOWNGRADE]", "newer Orbit")]
    [InlineData("Failure [INSTALL_FAILED_USER_RESTRICTED: Install canceled by user]", "Install via USB")]
    [InlineData("Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]", "out of storage")]
    [InlineData("timeout", "tap Allow")]
    [InlineData("Performing Streamed Install\nadb: something odd", "(unknown)")]
    public void ExplainsInstallErrors(string output, string expected) =>
        Assert.Contains(expected, OrbitCore.InstallError(output));

    [Fact]
    public void FailureCodeLeavesOutPaths()
    {
        var o = @"adb: failed to install C:\Program Files\Orbit\phone\Orbit.apk: Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]";
        Assert.Equal("INSTALL_FAILED_INSUFFICIENT_STORAGE", OrbitCore.FailureCode(o));
        Assert.Equal("unknown", OrbitCore.FailureCode(@"error: C:\Program Files\Orbit\x"));
        Assert.DoesNotContain("Program Files", OrbitCore.InstallError(@"error: C:\Program Files\Orbit\x"));
    }
}
