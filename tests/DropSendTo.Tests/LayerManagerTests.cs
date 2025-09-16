using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class LayerManagerTests
{
    [Fact]
    public void NextPrev_Should_Cycle_0_3()
    {
        var lm = new LayerManager(0);
        lm.Current.Should().Be(0);
        lm.Next(); lm.Current.Should().Be(1);
        lm.Next(); lm.Current.Should().Be(2);
        lm.Next(); lm.Current.Should().Be(3);
        lm.Next(); lm.Current.Should().Be(0);
        lm.Prev(); lm.Current.Should().Be(3);
    }

    [Fact]
    public void Set_Should_Clamp_To_Range()
    {
        var lm = new LayerManager(0);
        lm.Set(-2); lm.Current.Should().Be(0);
        lm.Set(10); lm.Current.Should().Be(3);
        lm.Set(2); lm.Current.Should().Be(2);
    }
}

