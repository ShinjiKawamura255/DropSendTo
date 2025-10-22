using System;

namespace DropSendTo.Services;

internal static class InputExtraInfo
{
    public const uint MacroInjectionTag = 0x4453544F;

    public static readonly IntPtr MacroInjectionPointer =
        new(unchecked((int)MacroInjectionTag));

    public static readonly UIntPtr MacroInjectionPointerUnsigned =
        new(MacroInjectionTag);

    public static bool IsMacroInjection(UIntPtr value) =>
        value == MacroInjectionPointerUnsigned;
}
