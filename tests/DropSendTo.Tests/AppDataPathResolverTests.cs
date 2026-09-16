using System;
using System.IO;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class AppDataPathResolverTests
{
    [Fact]
    public void ResolveBaseDirectory_ShouldUseAbsoluteProbeRoot_WhenProbeIsExplicitlyEnabled()
    {
        var probeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DropSendTo-probe"));
        var fallback = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DropSendTo-fallback"));

        var result = AppDataPathResolver.ResolveBaseDirectory("1", probeRoot, fallback);

        result.Should().Be(probeRoot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    public void ResolveBaseDirectory_ShouldUseFallback_WhenProbeIsNotExplicitlyEnabled(string? probeFlag)
    {
        var probeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DropSendTo-probe"));
        var fallback = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DropSendTo-fallback"));

        var result = AppDataPathResolver.ResolveBaseDirectory(probeFlag, probeRoot, fallback);

        result.Should().Be(fallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-path")]
    public void ResolveBaseDirectory_ShouldUseFallback_WhenProbeRootIsMissingOrRelative(string? probeRoot)
    {
        var fallback = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DropSendTo-fallback"));

        var result = AppDataPathResolver.ResolveBaseDirectory("1", probeRoot, fallback);

        result.Should().Be(fallback);
    }
}
