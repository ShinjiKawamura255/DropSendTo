using System.Runtime.InteropServices;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;
using Adapter = DropSendTo.Services.NativeMacroDisplayAdapter;

namespace DropSendTo.Tests;

public class NativeMacroDisplayAdapterTests
{
    [Fact]
    public void NativeStructures_ShouldMatchWin32Layout()
    {
        Marshal.SizeOf<Adapter.DisplaySource>().Should().Be(20);
        Marshal.SizeOf<Adapter.DisplayTarget>().Should().Be(48);
        Marshal.SizeOf<Adapter.DisplayPath>().Should().Be(72);
        Marshal.SizeOf<Adapter.DisplayMode>().Should().Be(64);
        Marshal.OffsetOf<Adapter.DisplayPath>(nameof(Adapter.DisplayPath.Target)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<Adapter.DisplayPath>(nameof(Adapter.DisplayPath.Flags)).ToInt32().Should().Be(68);
        Marshal.OffsetOf<Adapter.DisplayMode>(nameof(Adapter.DisplayMode.X)).ToInt32().Should().Be(28);
    }

    [Fact]
    public void TrySelectBounds_ShouldFollowCcdNumbering_WithoutSortingByPositionOrPrimary()
    {
        var (paths, modes) = CreateTopology();

        Adapter.TrySelectBounds(1, paths, 2, modes, 3, out var first, out _).Should().BeTrue();
        Adapter.TrySelectBounds(2, paths, 2, modes, 3, out var second, out _).Should().BeTrue();

        first.Should().Be(new MacroDisplayBounds(1920, 0, 3840, 2160));
        second.Should().Be(new MacroDisplayBounds(-1920, -1080, 1920, 1080));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void TrySelectBounds_ShouldFailWithoutFallback_WhenNumberIsAbsent(int number)
    {
        var (paths, modes) = CreateTopology();

        Adapter.TrySelectBounds(number, paths, 2, modes, 3, out _, out var error).Should().BeFalse();

        error.Should().Contain("見つかりません");
    }

    [Theory]
    [InlineData(uint.MaxValue)]
    [InlineData(3u)]
    public void TrySelectBounds_ShouldRejectInvalidSourceModeIndex(uint index)
    {
        var (paths, modes) = CreateTopology();
        paths[0].Source.ModeIndex = index;

        Adapter.TrySelectBounds(1, paths, 2, modes, 3, out _, out var error).Should().BeFalse();

        error.Should().Contain("座標");
    }

    [Fact]
    public void TrySelectBounds_ShouldRejectModeBelongingToAnotherAdapter()
    {
        var (paths, modes) = CreateTopology();
        modes[2].Adapter.Low = 99;

        Adapter.TrySelectBounds(1, paths, 2, modes, 3, out _, out var error).Should().BeFalse();

        error.Should().Contain("領域");
    }

    [Fact]
    public void TrySelectBounds_ShouldShareCoordinates_ForMirroredDisplays()
    {
        var (paths, modes) = CreateTopology();
        paths[1].Source = paths[0].Source;

        Adapter.TrySelectBounds(1, paths, 2, modes, 3, out var first, out _).Should().BeTrue();
        Adapter.TrySelectBounds(2, paths, 2, modes, 3, out var second, out _).Should().BeTrue();

        second.Should().Be(first);
    }

    private static (Adapter.DisplayPath[], Adapter.DisplayMode[]) CreateTopology()
    {
        var adapter = new Adapter.AdapterId { Low = 42, High = 1 };
        return (
            new[]
            {
                new Adapter.DisplayPath { Source = new() { Adapter = adapter, Id = 7, ModeIndex = 2 } },
                new Adapter.DisplayPath { Source = new() { Adapter = adapter, Id = 3, ModeIndex = 0 } }
            },
            new[]
            {
                new Adapter.DisplayMode { Type = 1, Adapter = adapter, Id = 3, Width = 1920, Height = 1080, X = -1920, Y = -1080 },
                new Adapter.DisplayMode { Type = 2 },
                new Adapter.DisplayMode { Type = 1, Adapter = adapter, Id = 7, Width = 3840, Height = 2160, X = 1920, Y = 0 }
            });
    }
}
