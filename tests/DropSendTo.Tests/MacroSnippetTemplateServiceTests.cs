using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public sealed class MacroSnippetTemplateServiceTests
{
    [Theory]
    [InlineData("WAIT 250", "WAIT")]
    [InlineData("COMMAND {{drop_args}}", "COMMAND")]
    [InlineData("COMMAND_APP \"C:\\\\apps\\\\custom.exe\"", "COMMAND_APP")]
    public void TryCreateWithoutSampleArguments_ShouldReturnCommand_WhenSampleArgumentsExist(string snippet, string expected)
    {
        var actual = MacroSnippetTemplateService.TryCreateWithoutSampleArguments(snippet);

        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("MOUSELEFTCLICK")]
    [InlineData("{{drop_args}}")]
    [InlineData("REPEAT 3\nENDREPEAT")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreateWithoutSampleArguments_ShouldReturnNull_WhenSnippetIsNotConvertible(string snippet)
    {
        var actual = MacroSnippetTemplateService.TryCreateWithoutSampleArguments(snippet);

        actual.Should().BeNull();
    }
}
