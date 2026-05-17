using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutSpecialCommandResolverTests
{
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkSpace = 0x20;
    private const ushort VkD = 0x44;
    private const ushort VkQ = 0x51;

    [Fact]
    public void Resolve_ShouldReturnTogglePosition_ForBareTab()
    {
        Resolve(VkTab).Should().Be(ShortcutSpecialCommandType.PrefixTogglePosition);
    }

    [Fact]
    public void Resolve_ShouldReturnActivate_ForBareEnter()
    {
        Resolve(VkReturn).Should().Be(ShortcutSpecialCommandType.PrefixActivate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_ShouldReturnSearch_ForAltSpace(bool altAsResidue)
    {
        var modifiers = altAsResidue ? new HashSet<ushort>() : new HashSet<ushort> { VkMenu };
        var residue = altAsResidue ? new[] { VkMenu } : Array.Empty<ushort>();

        Resolve(VkSpace, modifiers, residue).Should().Be(ShortcutSpecialCommandType.PrefixSearch);
    }

    [Fact]
    public void Resolve_ShouldReturnCancelMacro_ForAltEnterWithoutResidue()
    {
        Resolve(VkReturn, new HashSet<ushort> { VkMenu }).Should().Be(ShortcutSpecialCommandType.PrefixCancelMacro);
    }

    [Fact]
    public void Resolve_ShouldReturnMinimize_ForShiftEnterWithoutResidue()
    {
        Resolve(VkReturn, new HashSet<ushort> { VkShift }).Should().Be(ShortcutSpecialCommandType.PrefixMinimize);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_ShouldReturnDropCapture_ForCtrlD_WhenEnabled(bool ctrlAsResidue)
    {
        var modifiers = ctrlAsResidue ? new HashSet<ushort>() : new HashSet<ushort> { VkControl };
        var residue = ctrlAsResidue ? new[] { VkControl } : Array.Empty<ushort>();

        Resolve(VkD, modifiers, residue, prefixDropCaptureEnabled: true)
            .Should().Be(ShortcutSpecialCommandType.PrefixDropCapture);
    }

    [Fact]
    public void Resolve_ShouldReturnNone_ForCtrlD_WhenDropCaptureDisabled()
    {
        Resolve(VkD, new HashSet<ushort> { VkControl }, prefixDropCaptureEnabled: false)
            .Should().Be(ShortcutSpecialCommandType.None);
    }

    [Theory]
    [InlineData(VkTab)]
    [InlineData(VkReturn)]
    public void Resolve_ShouldRejectResidue_ForBareCommands(ushort vk)
    {
        Resolve(vk, prefixResidue: new[] { VkControl }).Should().Be(ShortcutSpecialCommandType.None);
    }

    [Theory]
    [InlineData(VkMenu)]
    [InlineData(VkShift)]
    public void Resolve_ShouldRejectResidue_ForEnterModifierCommands(ushort activeModifier)
    {
        Resolve(VkReturn, new HashSet<ushort> { activeModifier }, new[] { VkControl })
            .Should().Be(ShortcutSpecialCommandType.None);
    }

    [Theory]
    [InlineData(VkSpace, VkMenu)]
    [InlineData(VkD, VkControl)]
    public void Resolve_ShouldRejectExtraModifiers_ForResidueCommands(ushort vk, ushort expectedModifier)
    {
        Resolve(vk, new HashSet<ushort> { expectedModifier, VkShift })
            .Should().Be(ShortcutSpecialCommandType.None);
    }

    [Theory]
    [InlineData(VkSpace, VkMenu)]
    [InlineData(VkD, VkControl)]
    public void Resolve_ShouldRejectExtraResidue_ForResidueCommands(ushort vk, ushort expectedModifier)
    {
        Resolve(vk, prefixResidue: new[] { expectedModifier, VkShift })
            .Should().Be(ShortcutSpecialCommandType.None);
    }

    [Fact]
    public void Resolve_ShouldReturnNone_ForUnrecognizedKey()
    {
        Resolve(VkQ).Should().Be(ShortcutSpecialCommandType.None);
    }

    private static ShortcutSpecialCommandType Resolve(
        ushort vk,
        HashSet<ushort>? modifiers = null,
        IReadOnlyCollection<ushort>? prefixResidue = null,
        bool prefixDropCaptureEnabled = true)
    {
        return ShortcutSpecialCommandResolver.Resolve(
            vk,
            modifiers ?? new HashSet<ushort>(),
            prefixResidue ?? Array.Empty<ushort>(),
            prefixDropCaptureEnabled);
    }
}
