using System.Text;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public sealed class LoggerServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "DropSendTo_LoggerServiceTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_Should_NotRotate_WhenLogLengthEqualsThreshold()
    {
        var logger = CreateLogger(maxBytes: 8);
        var logPath = Path.Combine(_tempRoot, "app.log");
        File.WriteAllBytes(logPath, new byte[8]);

        logger.Info("boundary");

        Directory.GetFiles(_tempRoot, "app-*.log").Should().BeEmpty();
        new FileInfo(logPath).Length.Should().BeGreaterThan(8);
    }

    [Fact]
    public void Write_Should_Rotate_WhenLogLengthExceedsThreshold()
    {
        var logger = CreateLogger(maxBytes: 8);
        var logPath = Path.Combine(_tempRoot, "app.log");
        File.WriteAllBytes(logPath, new byte[9]);

        logger.Info("rotated");

        var archive = Directory.GetFiles(_tempRoot, "app-*.log").Should().ContainSingle().Subject;
        File.ReadAllBytes(archive).Should().HaveCount(9);
        File.ReadAllText(logPath, Encoding.UTF8).Should().Contain("rotated");
    }

    [Fact]
    public void CleanupOldLogs_Should_KeepExactlySevenDaysOld_AndDeleteOlderArchive()
    {
        var utcNow = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(_tempRoot);
        var exactlySevenDays = Path.Combine(_tempRoot, "app-exact.log");
        var older = Path.Combine(_tempRoot, "app-older.log");
        File.WriteAllText(exactlySevenDays, "keep");
        File.WriteAllText(older, "delete");
        File.SetLastWriteTimeUtc(exactlySevenDays, utcNow.AddDays(-7));
        File.SetLastWriteTimeUtc(older, utcNow.AddDays(-7).AddTicks(-1));

        _ = CreateLogger(utcNow: utcNow);

        File.Exists(exactlySevenDays).Should().BeTrue();
        File.Exists(older).Should().BeFalse();
    }

    [Fact]
    public void Write_Should_CreateUniqueArchives_ForRepeatedSameSecondRotations()
    {
        var now = new DateTime(2026, 9, 16, 12, 34, 56, DateTimeKind.Local);
        var logger = CreateLogger(localNow: now, maxBytes: 1);
        var logPath = Path.Combine(_tempRoot, "app.log");

        File.WriteAllText(logPath, "first-over-threshold");
        logger.Info("first-current-message");
        File.AppendAllText(logPath, "force-second-rotation");
        logger.Info("second-current-message");

        var archives = Directory.GetFiles(_tempRoot, "app-*.log")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        archives.Should().HaveCount(2);
        archives.Select(Path.GetFileName).Should().OnlyHaveUniqueItems();
        File.ReadAllText(logPath, Encoding.UTF8).Should().Contain("second-current-message");
    }

    [Fact]
    public void Write_Should_AvoidPreExistingArchiveNameCollision()
    {
        var now = new DateTime(2026, 9, 16, 12, 34, 56, DateTimeKind.Local);
        Directory.CreateDirectory(_tempRoot);
        var existingArchive = Path.Combine(_tempRoot, "app-20260916123456.log");
        File.WriteAllText(existingArchive, "pre-existing");
        File.WriteAllText(Path.Combine(_tempRoot, "app.log"), "over-threshold");
        var logger = CreateLogger(localNow: now, maxBytes: 1);

        logger.Info("current-message");

        File.ReadAllText(existingArchive).Should().Be("pre-existing");
        Directory.GetFiles(_tempRoot, "app-*.log").Should().HaveCount(2);
        File.ReadAllText(Path.Combine(_tempRoot, "app.log"), Encoding.UTF8).Should().Contain("current-message");
    }

    [Fact]
    public void Write_Should_AppendCurrentMessage_WhenRotationFailsButLogRemainsWritable()
    {
        var logger = CreateLogger(maxBytes: 1);
        var logPath = Path.Combine(_tempRoot, "app.log");
        File.WriteAllText(logPath, "over-threshold");

        using (new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var action = () => logger.Info("message-after-rotation-failure");

            action.Should().NotThrow();
        }

        File.ReadAllText(logPath, Encoding.UTF8).Should().Contain("message-after-rotation-failure");
        Directory.GetFiles(_tempRoot, "app-*.log").Should().BeEmpty();
    }

    [Fact]
    public void Write_Should_SwallowAppendFailure()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "app.log"));
        var logger = CreateLogger();

        var action = () => logger.Warn("cannot-append-to-directory");

        action.Should().NotThrow();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Test cleanup must not hide assertion failures.
        }
    }

    private LoggerService CreateLogger(
        DateTime? localNow = null,
        DateTime? utcNow = null,
        long maxBytes = 1_000_000)
    {
        var fixedLocalNow = localNow ?? new DateTime(2026, 9, 16, 21, 0, 0, DateTimeKind.Local);
        var fixedUtcNow = utcNow ?? new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        return new LoggerService(_tempRoot, () => fixedLocalNow, () => fixedUtcNow, maxBytes);
    }
}
