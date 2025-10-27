using System;
using System.Collections.Generic;
using System.Linq;

namespace DropSendTo.Services;

internal enum ModifierKind
{
    Control,
    Shift,
    Alt,
    Win,
    LeftWin,
    RightWin
}

internal sealed class KeyChord
{
    public KeyChord(ushort mainKey, string mainToken, IReadOnlyList<ModifierKind> modifiers, string normalizedString)
    {
        MainKey = mainKey;
        MainToken = mainToken;
        Modifiers = modifiers;
        NormalizedString = normalizedString;
    }

    public ushort MainKey { get; }
    public string MainToken { get; }
    public IReadOnlyList<ModifierKind> Modifiers { get; }
    public string NormalizedString { get; }

    public bool HasModifiers => Modifiers.Count > 0;
}

internal static class KeyChordParser
{
    private static readonly Dictionary<string, ushort> KeyMap = CreateKeyMap();
    private static readonly Dictionary<ushort, string> CanonicalKeyNames = CreateCanonicalNames();
    private static readonly Dictionary<ModifierKind, string> ModifierDisplayNames = new()
    {
        { ModifierKind.Control, "Ctrl" },
        { ModifierKind.Shift, "Shift" },
        { ModifierKind.Alt, "Alt" },
        { ModifierKind.Win, "Win" },
        { ModifierKind.LeftWin, "LWin" },
        { ModifierKind.RightWin, "RWin" }
    };
    private static readonly ModifierKind[] ModifierOrder =
    {
        ModifierKind.Control,
        ModifierKind.Shift,
        ModifierKind.Alt,
        ModifierKind.Win,
        ModifierKind.LeftWin,
        ModifierKind.RightWin
    };

    public static bool TryParse(string? expression, out KeyChord chord, out string? error)
    {
        chord = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "キー指定が空です。";
            return false;
        }

        var parts = expression.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "キー指定の書式が不正です。";
            return false;
        }

        var modifierKinds = new List<ModifierKind>();
        var seenKinds = new HashSet<ModifierKind>();

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var token = parts[i].Trim();
            if (!TryResolveModifier(token, out var kind))
            {
                error = $"修飾キーの指定が不正です: \"{token}\"";
                return false;
            }
            if (!CanAddModifier(kind, seenKinds, out var conflict))
            {
                error = conflict ?? $"修飾キーの指定が重複しています: \"{token}\"";
                return false;
            }
            seenKinds.Add(kind);
            modifierKinds.Add(kind);
        }

        var mainTokenRaw = parts[^1].Trim();
        if (!TryResolveKey(mainTokenRaw, out var mainKey))
        {
            error = $"キーの指定が不正です: \"{mainTokenRaw}\"";
            return false;
        }
        if (!TryGetCanonicalToken(mainKey, out var mainToken))
        {
            mainToken = parts[^1].Trim().ToUpperInvariant();
        }
        modifierKinds = modifierKinds.OrderBy(k => Array.IndexOf(ModifierOrder, k)).ToList();
        var normalized = BuildNormalizedString(modifierKinds, mainToken);
        chord = new KeyChord(mainKey, mainToken, modifierKinds, normalized);
        return true;
    }

    public static bool TryResolveKeyToken(string? token, out ushort key)
    {
        key = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;
        return TryResolveKey(token.Trim(), out key);
    }

    public static bool TryGetCanonicalToken(ushort vk, out string token)
    {
        if (CanonicalKeyNames.TryGetValue(vk, out token!))
        {
            return true;
        }

        if (vk >= 'A' && vk <= 'Z')
        {
            token = ((char)vk).ToString();
            return true;
        }

        if (vk >= '0' && vk <= '9')
        {
            token = ((char)vk).ToString();
            return true;
        }

        token = string.Empty;
        return false;
    }

    public static bool TryFormat(ushort mainKey, IReadOnlyCollection<ModifierKind> modifiers, out string normalized)
    {
        normalized = string.Empty;
        if (!TryGetCanonicalToken(mainKey, out var mainToken))
        {
            return false;
        }
        var ordered = modifiers.OrderBy(k => Array.IndexOf(ModifierOrder, k)).ToArray();
        normalized = BuildNormalizedString(ordered, mainToken);
        return true;
    }

    public static bool TryGetModifierVirtualKey(ModifierKind kind, out ushort vk)
    {
        vk = kind switch
        {
            ModifierKind.Control => VK_CONTROL,
            ModifierKind.Shift => VK_SHIFT,
            ModifierKind.Alt => VK_MENU,
            ModifierKind.Win or ModifierKind.LeftWin => VK_LWIN,
            ModifierKind.RightWin => VK_RWIN,
            _ => (ushort)0
        };
        return vk != 0;
    }

    public static IReadOnlyList<ushort> GetCandidateModifierVirtualKeys(ModifierKind kind) =>
        kind switch
        {
            ModifierKind.Control => new[] { VK_CONTROL, VK_LCONTROL, VK_RCONTROL },
            ModifierKind.Shift => new[] { VK_SHIFT, VK_LSHIFT, VK_RSHIFT },
            ModifierKind.Alt => new[] { VK_MENU, VK_LMENU, VK_RMENU },
            ModifierKind.Win => new[] { VK_LWIN, VK_RWIN },
            ModifierKind.LeftWin => new[] { VK_LWIN },
            ModifierKind.RightWin => new[] { VK_RWIN },
            _ => Array.Empty<ushort>()
        };

    private static bool TryResolveModifier(string token, out ModifierKind kind)
    {
        kind = token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => ModifierKind.Control,
            "SHIFT" => ModifierKind.Shift,
            "ALT" or "MENU" => ModifierKind.Alt,
            "WIN" => ModifierKind.Win,
            "LWIN" => ModifierKind.LeftWin,
            "RWIN" => ModifierKind.RightWin,
            _ => (ModifierKind)(-1)
        };
        return Enum.IsDefined(typeof(ModifierKind), kind) && kind != (ModifierKind)(-1);
    }

    private static bool TryResolveKey(string token, out ushort key)
    {
        key = 0;
        if (KeyMap.TryGetValue(token, out key!))
        {
            return true;
        }
        if (TryResolveModifier(token, out var modifierKind) && TryGetModifierVirtualKey(modifierKind, out key))
        {
            return true;
        }
        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = ch;
                return true;
            }
            if (SingleCharKeyMap.TryGetValue(token[0], out key!))
            {
                return true;
            }
        }
        return false;
    }

    private static bool CanAddModifier(ModifierKind kind, HashSet<ModifierKind> seenKinds, out string? error)
    {
        error = null;
        if (seenKinds.Contains(kind))
        {
            return false;
        }

        if (kind == ModifierKind.Win)
        {
            if (seenKinds.Contains(ModifierKind.LeftWin) || seenKinds.Contains(ModifierKind.RightWin))
            {
                error = "WIN と LWIN/RWIN を同時に指定することはできません。";
                return false;
            }
        }

        if (kind is ModifierKind.LeftWin or ModifierKind.RightWin)
        {
            if (seenKinds.Contains(ModifierKind.Win))
            {
                error = "WIN と LWIN/RWIN を同時に指定することはできません。";
                return false;
            }
        }

        return true;
    }

    private static string BuildNormalizedString(IEnumerable<ModifierKind> modifiers, string mainToken)
    {
        var modifierTokens = modifiers.Select(GetModifierDisplayToken).ToList();
        modifierTokens.Add(mainToken);
        return string.Join('+', modifierTokens);
    }

    private static string GetModifierDisplayToken(ModifierKind kind) =>
        ModifierDisplayNames.TryGetValue(kind, out var token) ? token : kind.ToString();

    private static Dictionary<string, ushort> CreateKeyMap()
    {
        var dict = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["ENTER"] = VK_RETURN,
            ["ESC"] = VK_ESCAPE,
            ["TAB"] = VK_TAB,
            ["SPACE"] = VK_SPACE,
            ["BACK"] = VK_BACK,
            ["UP"] = VK_UP,
            ["DOWN"] = VK_DOWN,
            ["LEFT"] = VK_LEFT,
            ["RIGHT"] = VK_RIGHT,
            ["HOME"] = VK_HOME,
            ["END"] = VK_END,
            ["PAGEUP"] = VK_PRIOR,
            ["PAGEDOWN"] = VK_NEXT,
            ["DELETE"] = VK_DELETE,
            ["INSERT"] = VK_INSERT,
            ["F1"] = VK_F1,
            ["F2"] = VK_F2,
            ["F3"] = VK_F3,
            ["F4"] = VK_F4,
            ["F5"] = VK_F5,
            ["F6"] = VK_F6,
            ["F7"] = VK_F7,
            ["F8"] = VK_F8,
            ["F9"] = VK_F9,
            ["F10"] = VK_F10,
            ["F11"] = VK_F11,
            ["F12"] = VK_F12,
            ["F13"] = VK_F13,
            ["F14"] = VK_F14,
            ["F15"] = VK_F15,
            ["F16"] = VK_F16,
            ["F17"] = VK_F17,
            ["F18"] = VK_F18,
            ["F19"] = VK_F19,
            ["F20"] = VK_F20,
            ["F21"] = VK_F21,
            ["F22"] = VK_F22,
            ["F23"] = VK_F23,
            ["F24"] = VK_F24,
            ["CAPSLOCK"] = VK_CAPITAL,
            ["SCROLLLOCK"] = VK_SCROLL,
            ["PAUSE"] = VK_PAUSE,
            ["PRINTSCREEN"] = VK_SNAPSHOT,
            ["APPS"] = VK_APPS,
            ["NUMLOCK"] = VK_NUMLOCK,
            ["NUM0"] = VK_NUMPAD0,
            ["NUM1"] = VK_NUMPAD1,
            ["NUM2"] = VK_NUMPAD2,
            ["NUM3"] = VK_NUMPAD3,
            ["NUM4"] = VK_NUMPAD4,
            ["NUM5"] = VK_NUMPAD5,
            ["NUM6"] = VK_NUMPAD6,
            ["NUM7"] = VK_NUMPAD7,
            ["NUM8"] = VK_NUMPAD8,
            ["NUM9"] = VK_NUMPAD9,
            ["MULTIPLY"] = VK_MULTIPLY,
            ["ADD"] = VK_ADD,
            ["SUBTRACT"] = VK_SUBTRACT,
            ["DIVIDE"] = VK_DIVIDE,
            ["DECIMAL"] = VK_DECIMAL,
            ["OEM1"] = VK_OEM_1,
            ["OEMPLUS"] = VK_OEM_PLUS,
            ["OEMCOMMA"] = VK_OEM_COMMA,
            ["OEMMINUS"] = VK_OEM_MINUS,
            ["OEMPERIOD"] = VK_OEM_PERIOD,
            ["OEM2"] = VK_OEM_2,
            ["OEM3"] = VK_OEM_3,
            ["OEM4"] = VK_OEM_4,
            ["OEM5"] = VK_OEM_5,
            ["OEM6"] = VK_OEM_6,
            ["OEM7"] = VK_OEM_7
        };

        dict["RETURN"] = VK_RETURN;
        dict["ESCAPE"] = VK_ESCAPE;
        dict["BACKSPACE"] = VK_BACK;
        dict["PGUP"] = VK_PRIOR;
        dict["PGDN"] = VK_NEXT;
        dict["DELETE"] = VK_DELETE;
        dict["DEL"] = VK_DELETE;
        dict["INSERT"] = VK_INSERT;
        dict["INS"] = VK_INSERT;
        dict["MINUS"] = VK_OEM_MINUS;
        dict["SEPARATOR"] = VK_SEPARATOR;

        for (char c = 'A'; c <= 'Z'; c++)
        {
            dict[c.ToString()] = c;
        }

        for (char c = '0'; c <= '9'; c++)
        {
            dict[c.ToString()] = c;
        }

        return dict;
    }

    private static readonly Dictionary<char, ushort> SingleCharKeyMap = new()
    {
        [';'] = VK_OEM_1,
        [':'] = VK_OEM_1,
        ['='] = VK_OEM_PLUS,
        ['+'] = VK_OEM_PLUS,
        [','] = VK_OEM_COMMA,
        ['-'] = VK_OEM_MINUS,
        ['_'] = VK_OEM_MINUS,
        ['.'] = VK_OEM_PERIOD,
        ['/'] = VK_OEM_2,
        ['?'] = VK_OEM_2,
        ['`'] = VK_OEM_3,
        ['~'] = VK_OEM_3,
        ['['] = VK_OEM_4,
        ['{'] = VK_OEM_4,
        ['\\'] = VK_OEM_5,
        ['|'] = VK_OEM_5,
        [']'] = VK_OEM_6,
        ['}'] = VK_OEM_6,
        ['\''] = VK_OEM_7,
        ['"'] = VK_OEM_7
    };

    private static Dictionary<ushort, string> CreateCanonicalNames()
    {
        var reverse = new Dictionary<ushort, string>();
        foreach (var kv in KeyMap)
        {
            if (!reverse.ContainsKey(kv.Value))
            {
                reverse[kv.Value] = kv.Key;
            }
        }
        return reverse;
    }

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_UP = 0x26;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_HOME = 0x24;
    private const ushort VK_END = 0x23;
    private const ushort VK_PRIOR = 0x21;
    private const ushort VK_NEXT = 0x22;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_INSERT = 0x2D;
    private const ushort VK_F1 = 0x70;
    private const ushort VK_F2 = 0x71;
    private const ushort VK_F3 = 0x72;
    private const ushort VK_F4 = 0x73;
    private const ushort VK_F5 = 0x74;
    private const ushort VK_F6 = 0x75;
    private const ushort VK_F7 = 0x76;
    private const ushort VK_F8 = 0x77;
    private const ushort VK_F9 = 0x78;
    private const ushort VK_F10 = 0x79;
    private const ushort VK_F11 = 0x7A;
    private const ushort VK_F12 = 0x7B;
    private const ushort VK_F13 = 0x7C;
    private const ushort VK_F14 = 0x7D;
    private const ushort VK_F15 = 0x7E;
    private const ushort VK_F16 = 0x7F;
    private const ushort VK_F17 = 0x80;
    private const ushort VK_F18 = 0x81;
    private const ushort VK_F19 = 0x82;
    private const ushort VK_F20 = 0x83;
    private const ushort VK_F21 = 0x84;
    private const ushort VK_F22 = 0x85;
    private const ushort VK_F23 = 0x86;
    private const ushort VK_F24 = 0x87;
    private const ushort VK_CAPITAL = 0x14;
    private const ushort VK_SCROLL = 0x91;
    private const ushort VK_PAUSE = 0x13;
    private const ushort VK_SNAPSHOT = 0x2C;
    private const ushort VK_APPS = 0x5D;
    private const ushort VK_NUMLOCK = 0x90;
    private const ushort VK_NUMPAD0 = 0x60;
    private const ushort VK_NUMPAD1 = 0x61;
    private const ushort VK_NUMPAD2 = 0x62;
    private const ushort VK_NUMPAD3 = 0x63;
    private const ushort VK_NUMPAD4 = 0x64;
    private const ushort VK_NUMPAD5 = 0x65;
    private const ushort VK_NUMPAD6 = 0x66;
    private const ushort VK_NUMPAD7 = 0x67;
    private const ushort VK_NUMPAD8 = 0x68;
    private const ushort VK_NUMPAD9 = 0x69;
    private const ushort VK_MULTIPLY = 0x6A;
    private const ushort VK_ADD = 0x6B;
    private const ushort VK_SEPARATOR = 0x6C;
    private const ushort VK_SUBTRACT = 0x6D;
    private const ushort VK_DECIMAL = 0x6E;
    private const ushort VK_DIVIDE = 0x6F;
    private const ushort VK_OEM_1 = 0xBA;
    private const ushort VK_OEM_PLUS = 0xBB;
    private const ushort VK_OEM_COMMA = 0xBC;
    private const ushort VK_OEM_MINUS = 0xBD;
    private const ushort VK_OEM_PERIOD = 0xBE;
    private const ushort VK_OEM_2 = 0xBF;
    private const ushort VK_OEM_3 = 0xC0;
    private const ushort VK_OEM_4 = 0xDB;
    private const ushort VK_OEM_5 = 0xDC;
    private const ushort VK_OEM_6 = 0xDD;
    private const ushort VK_OEM_7 = 0xDE;
}
