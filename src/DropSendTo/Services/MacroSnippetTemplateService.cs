namespace DropSendTo.Services;

internal static class MacroSnippetTemplateService
{
    internal static string? TryCreateWithoutSampleArguments(string snippetContent)
    {
        if (string.IsNullOrWhiteSpace(snippetContent))
        {
            return null;
        }

        var normalized = snippetContent.Trim();
        if (normalized.IndexOfAny(['\r', '\n']) >= 0)
        {
            return null;
        }

        if (normalized.StartsWith("{{", System.StringComparison.Ordinal))
        {
            return null;
        }

        var separatorIndex = normalized.IndexOf(' ');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var command = normalized[..separatorIndex];
        return string.IsNullOrWhiteSpace(command) ? null : command;
    }
}
