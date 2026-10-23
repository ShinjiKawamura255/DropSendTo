using System;

namespace DropSendTo.Services;

internal static class InputExtraInfo
{
    public const uint MacroInjectionTag = 0x4453544F;
    public const uint MacroPassthroughTag = 0x44535450;
    public const uint MacroExplicitPassthroughTag = 0x44535451;

    public static readonly IntPtr MacroInjectionPointer =
        new(unchecked((int)MacroInjectionTag));

    public static readonly UIntPtr MacroInjectionPointerUnsigned =
        new(MacroInjectionTag);

    public static readonly IntPtr MacroPassthroughPointer =
        new(unchecked((int)MacroPassthroughTag));

    public static readonly UIntPtr MacroPassthroughPointerUnsigned =
        new(MacroPassthroughTag);

    public static readonly IntPtr MacroExplicitPassthroughPointer =
        new(unchecked((int)MacroExplicitPassthroughTag));

    public static readonly UIntPtr MacroExplicitPassthroughPointerUnsigned =
        new(MacroExplicitPassthroughTag);

    public static bool IsMacroInjection(UIntPtr value) =>
        value == MacroInjectionPointerUnsigned;

    public static bool IsMacroPassthrough(UIntPtr value) =>
        value == MacroPassthroughPointerUnsigned;

    public static bool IsMacroExplicitPassthrough(UIntPtr value) =>
        value == MacroExplicitPassthroughPointerUnsigned;

    public static bool IsKnownMacro(UIntPtr value) =>
        IsMacroInjection(value) || IsMacroPassthrough(value) || IsMacroExplicitPassthrough(value);
}
