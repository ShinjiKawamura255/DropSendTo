using System.Diagnostics;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class LoggingPrivacyTests
{
    private const string Sentinel = "__DROP_SEND_TO_PRIVATE_SENTINEL__";

    [Fact]
    public void Launcher_Should_Not_Log_User_Controlled_Command_Title_Or_Arguments()
    {
        var logger = new CaptureLogger();
        var service = new TestableLauncherService(logger);
        var slot = new SlotModel
        {
            Title = $"title-{Sentinel}",
            Command = $@"C:\private\{Sentinel}.exe",
            ArgumentsTemplate = $"--token {Sentinel} {{args}}"
        };

        var result = service.Launch(slot, [$@"C:\drop\{Sentinel}.txt"]);

        result.Success.Should().BeTrue();
        logger.Messages.Should().NotContain(message => message.Contains(Sentinel, StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeyboardMacro_Should_Not_Log_Expanded_Variable_Or_Return_Values()
    {
        var logger = new CaptureLogger();
        using var service = new KeyboardMacroService(logger);

        var script = $"SET Secret {Sentinel}\nAPPEND Secret -suffix\nRETURN \"{{{{Secret}}}}\"";
        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain(Sentinel);
        logger.Messages.Should().NotContain(message => message.Contains(Sentinel, StringComparison.Ordinal));
    }

    private sealed class CaptureLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }

    private sealed class TestableLauncherService(IAppLogger logger) : LauncherService(logger)
    {
        internal override Process? StartProcess(ProcessStartInfo startInfo) => null;
        internal override int GetProcessId(Process? process) => 0;
    }
}
