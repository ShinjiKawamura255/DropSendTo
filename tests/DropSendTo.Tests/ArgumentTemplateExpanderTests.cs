using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class ArgumentTemplateExpanderTests
{
    [Fact]
    public void Expand_ReplacesArgsWithQuotedPaths()
    {
        var paths = new[] { @"C:\One", @"C:\Two Folder\file.txt" };
        var result = ArgumentTemplateExpander.Expand("{args}", paths, () => ClipboardSnapshot.Empty);
        Assert.Equal(@"C:\One ""C:\Two Folder\file.txt""", result);
    }

    [Fact]
    public void Expand_ReplacesClipboardRawText()
    {
        var template = "copy {clipboard}";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => Snapshot("  sample text  ", new[] { "sample text" }));
        Assert.Equal("copy sample text", result);
    }

    [Fact]
    public void Expand_ReplacesClipboardArgs_WithQuoting()
    {
        var template = "{clipboard_args}";
        var clipboard = "C:\\Data One\\file.txt\n\"D:\\Work\\note.txt\"";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => Snapshot(clipboard, new[] { @"C:\Data One\file.txt", @"D:\Work\note.txt" }));
        Assert.Equal(@"""C:\Data One\file.txt"" D:\Work\note.txt", result);
    }

    [Fact]
    public void Expand_IgnoresMissingClipboard()
    {
        var template = "{clipboard_args}";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => ClipboardSnapshot.Empty);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Expand_ReplacesClipboardArgs_WithLimit()
    {
        var template = "{clipboard_args:2}";
        var clipboard = "C:\\Data One\\file.txt\nD:\\Work\\note.txt\nE:\\Archive\\doc.txt";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => Snapshot(clipboard, new[] { @"C:\Data One\file.txt", @"D:\Work\note.txt", @"E:\Archive\doc.txt" }, new[] { @"C:\Data One\file.txt", @"D:\Work\note.txt", @"E:\Archive\doc.txt" }));
        Assert.Equal(@"D:\Work\note.txt E:\Archive\doc.txt", result);
    }

    [Fact]
    public void Expand_ReplacesMixedClipboardArgsTokens()
    {
        var template = "{clipboard_args:1} -- {clipboard_args}";
        var clipboard = "\"C:\\Data One\\file.txt\"\nD:\\Work\\note.txt";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => Snapshot(clipboard, new[] { @"C:\Data One\file.txt", @"D:\Work\note.txt" }, new[] { @"C:\Data One\file.txt", @"D:\Work\note.txt" }));
        Assert.Equal(@"D:\Work\note.txt -- ""C:\Data One\file.txt"" D:\Work\note.txt", result);
    }

    [Fact]
    public void Expand_InvalidClipboardArgsLimit_ReturnsEmpty()
    {
        var template = "{clipboard_args:0}";
        var clipboard = "C:\\Data One\\file.txt";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(), () => Snapshot(clipboard, new[] { @"C:\Data One\file.txt" }, new[] { @"C:\Data One\file.txt" }));
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Expand_ClipboardArgs_UsesLatestEntriesOnly()
    {
        var template = "{clipboard_args}";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(),
            () => new ClipboardSnapshot("Line1\nLine2", new[] { "Old1", "Old2", "Old3" }, new[] { "Line1", "Line2" }));
        Assert.Equal("Line1 Line2", result);
    }

    [Fact]
    public void Expand_ClipboardArgs_EmptyLatestReturnsEmpty()
    {
        var template = "{clipboard_args}";
        var result = ArgumentTemplateExpander.Expand(template, Array.Empty<string>(),
            () => new ClipboardSnapshot(string.Empty, new[] { "Old1" }, Array.Empty<string>()));
        Assert.Equal(string.Empty, result);
    }

    private static ClipboardSnapshot Snapshot(string raw, string[] latestEntries, string[]? history = null)
    {
        history ??= latestEntries;
        return new ClipboardSnapshot(raw, history, latestEntries);
    }
}
