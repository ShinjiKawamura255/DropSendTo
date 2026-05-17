using System;

namespace DropSendTo.Services;

internal static class ShortcutRemoteSessionMatcher
{
    private static readonly string[] RemoteWindowClassNames =
    {
        "TscShellContainerClass",
        "TscShellContainerClass2",
        "TSSHELLWND",
        "TscShellWindowClass",
        "TransparentWndClass",
        "CitrixHDXClientWindowClass",
        "CitrixWorkspaceDesktop",
        "CtxGPCClass",
        "WFICATopLevelWindow",
        "WFICATopLevel"
    };

    private static readonly string[] RemoteWindowClassWildcards =
    {
        "citrix",
        "ctx",
        "wfica",
        "hdx"
    };

    private static readonly string[] RemoteProcessNames =
    {
        "mstsc",
        "mstsc64",
        "wfica32",
        "wfcrun32",
        "citrixworkspace",
        "citrixviewer",
        "selfserviceplugin",
        "receiver",
        "cdviewer",
        "hdxengine"
    };

    private static readonly string[] RemoteProcessWildcards =
    {
        "citrix",
        "wfica",
        "wfcrun",
        "hdx"
    };

    public static bool IsRemoteClassName(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        foreach (var candidate in RemoteWindowClassNames)
        {
            if (string.Equals(className, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var fragment in RemoteWindowClassWildcards)
        {
            if (className.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsRemoteProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        foreach (var candidate in RemoteProcessNames)
        {
            if (string.Equals(processName, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var fragment in RemoteProcessWildcards)
        {
            if (processName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
