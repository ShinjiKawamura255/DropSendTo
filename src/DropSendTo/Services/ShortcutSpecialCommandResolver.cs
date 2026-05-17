using System.Collections.Generic;

namespace DropSendTo.Services;

internal enum ShortcutSpecialCommandType
{
    None,
    PrefixActivate,
    PrefixMinimize,
    PrefixCancelMacro,
    PrefixTogglePosition,
    PrefixSearch,
    PrefixDropCapture
}

internal static class ShortcutSpecialCommandResolver
{
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_D = 0x44;

    public static ShortcutSpecialCommandType Resolve(
        ushort vk,
        HashSet<ushort> modifiers,
        IReadOnlyCollection<ushort> prefixResidue,
        bool prefixDropCaptureEnabled)
    {
        if (vk == VK_TAB && modifiers.Count == 0 && prefixResidue.Count == 0)
        {
            return ShortcutSpecialCommandType.PrefixTogglePosition;
        }

        if (vk == VK_SPACE)
        {
            bool altFromModifiers = modifiers.Contains(VK_MENU);
            bool altFromResidue = ContainsVirtualKey(prefixResidue, VK_MENU);
            if ((altFromModifiers || altFromResidue) &&
                !HasModifiersOtherThan(modifiers, altFromModifiers ? VK_MENU : (ushort)0) &&
                !HasModifiersOtherThan(prefixResidue, altFromResidue ? VK_MENU : (ushort)0))
            {
                return ShortcutSpecialCommandType.PrefixSearch;
            }
        }

        if (vk == VK_RETURN && modifiers.Count == 1 && modifiers.Contains(VK_MENU) && prefixResidue.Count == 0)
        {
            return ShortcutSpecialCommandType.PrefixCancelMacro;
        }

        if (vk == VK_RETURN && modifiers.Count == 1 && modifiers.Contains(VK_SHIFT) && prefixResidue.Count == 0)
        {
            return ShortcutSpecialCommandType.PrefixMinimize;
        }

        if (prefixDropCaptureEnabled)
        {
            bool ctrlFromModifiers = modifiers.Contains(VK_CONTROL);
            bool ctrlFromResidue = ContainsVirtualKey(prefixResidue, VK_CONTROL);
            bool ctrlActive = ctrlFromModifiers || ctrlFromResidue;
            if (ctrlActive &&
                !HasModifiersOtherThan(modifiers, ctrlFromModifiers ? VK_CONTROL : (ushort)0) &&
                !HasModifiersOtherThan(prefixResidue, ctrlFromResidue ? VK_CONTROL : (ushort)0))
            {
                if (vk == VK_D)
                {
                    return ShortcutSpecialCommandType.PrefixDropCapture;
                }
            }
        }

        if (vk == VK_RETURN && modifiers.Count == 0 && prefixResidue.Count == 0)
        {
            return ShortcutSpecialCommandType.PrefixActivate;
        }

        return ShortcutSpecialCommandType.None;
    }

    private static bool ContainsVirtualKey(IReadOnlyCollection<ushort> source, ushort value)
    {
        if (source == null || source.Count == 0) return false;
        foreach (var entry in source)
        {
            if (entry == value)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasModifiersOtherThan(HashSet<ushort> source, ushort allowed)
    {
        if (source.Count == 0) return false;
        if (allowed == 0)
        {
            return source.Count > 0;
        }
        foreach (var entry in source)
        {
            if (entry != allowed)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasModifiersOtherThan(IReadOnlyCollection<ushort> source, ushort allowed)
    {
        if (source == null || source.Count == 0) return false;
        if (allowed == 0)
        {
            return source.Count > 0;
        }
        foreach (var entry in source)
        {
            if (entry != allowed)
            {
                return true;
            }
        }
        return false;
    }
}
