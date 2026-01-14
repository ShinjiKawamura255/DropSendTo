using System;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal static class ThemeService
{
    private static readonly Uri DarkThemeUri = new("Themes/Colors.xaml", UriKind.Relative);
    private static readonly Uri LightThemeUri = new("Themes/Colors.Light.xaml", UriKind.Relative);

    public static AppTheme GetCurrentTheme()
    {
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource("Theme.UseDarkTitleBar") is bool useDark)
        {
            return useDark ? AppTheme.Dark : AppTheme.Light;
        }

        return AppTheme.Dark;
    }

    public static void ApplyTheme(AppTheme theme)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            if (IsThemeDictionary(dictionaries[i]))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Insert(0, new System.Windows.ResourceDictionary
        {
            Source = theme == AppTheme.Light ? LightThemeUri : DarkThemeUri
        });
    }

    private static bool IsThemeDictionary(System.Windows.ResourceDictionary dictionary)
    {
        return IsThemeUri(dictionary.Source, DarkThemeUri) || IsThemeUri(dictionary.Source, LightThemeUri);
    }

    private static bool IsThemeUri(Uri? source, Uri expected)
    {
        if (source == null)
        {
            return false;
        }

        return source.OriginalString.EndsWith(expected.OriginalString, StringComparison.OrdinalIgnoreCase);
    }
}
