using System;
using System.IO;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class SlotDropRegistrationHelperTests
{
    [Fact]
    public void TryCreate_ShouldCreateRegistration_ForDirectory()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N")));

        var ok = SlotDropRegistrationHelper.TryCreate(new[] { tempDir.FullName }, null, out var registration);

        ok.Should().BeTrue();
        registration.Title.Should().Be(Path.GetFileName(tempDir.FullName));
        registration.Command.Should().Be(tempDir.FullName.TrimEnd(Path.DirectorySeparatorChar));
        registration.ArgumentsTemplate.Should().BeEmpty();
        registration.ExecutionMode.Should().Be(SlotExecutionMode.Command);
    }

    [Fact]
    public void TryCreate_ShouldUseArgsForExecutable()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"), "tool.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(tempFile)!);
        File.WriteAllText(tempFile, "placeholder");

        var ok = SlotDropRegistrationHelper.TryCreate(new[] { tempFile }, null, out var registration);

        ok.Should().BeTrue();
        registration.Title.Should().Be("tool");
        registration.Command.Should().Be(tempFile);
        registration.ArgumentsTemplate.Should().Be("{args}");
    }

    [Fact]
    public void TryCreate_ShouldFallbackToText_WhenNoPaths()
    {
        const string text = "notepad.exe\nextra";

        var ok = SlotDropRegistrationHelper.TryCreate(null, text, out var registration);

        ok.Should().BeTrue();
        registration.Title.Should().Be("notepad.exe");
        registration.Command.Should().Be("notepad.exe");
        registration.ArgumentsTemplate.Should().BeEmpty();
    }
}
