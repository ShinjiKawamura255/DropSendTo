using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class ArgumentTemplateExpanderTests
{
    [Fact]
    public void Expand_ReplacesArgsWithQuotedPaths()
    {
        var paths = new[] { @"C:\One", @"C:\Two Folder\file.txt" };
        var result = ArgumentTemplateExpander.Expand("{args}", paths, () => null);
        Assert.Equal(@"C:\One ""C:\Two Folder\file.txt""", result);
    }

    [Fact]
    public void Expand_ReplacesClipboardRawText()
    {
        var template = "copy {clipboard}";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => "  sample text  ");
        Assert.Equal("copy sample text", result);
    }

    [Fact]
    public void Expand_ReplacesClipboardArgs_WithQuoting()
    {
        var template = "{clipboard_args}";
        var clipboard = "C:\\Data One\\file.txt\n\"D:\\Work\\note.txt\"";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => clipboard);
        Assert.Equal(@"""C:\Data One\file.txt"" D:\Work\note.txt", result);
    }

    [Fact]
    public void Expand_IgnoresMissingClipboard()
    {
        var template = "{clipboard_args}";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => null);
        Assert.Equal(string.Empty, result);
    }
}
