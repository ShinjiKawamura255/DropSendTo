using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutServicePrefixValidationTests
{
    [Fact]
    public void UpdatePrefix_ShouldFallbackToDefault_WhenPrefixUsesDisallowedKey()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var serviceType = typeof(LoggerService).Assembly
                .GetType("DropSendTo.Services.ShortcutService", throwOnError: true)!;

            object instance = RuntimeHelpers.GetUninitializedObject(serviceType);
            var timer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                SetField(instance, "_stateLock", new object());
                SetField(instance, "_dispatcher", dispatcher);
                SetField(instance, "_prefixTimeoutTimer", timer);
                SetField(instance, "_activeModifiers", new HashSet<ushort>());
                SetField(instance, "_modifierLastPressedUtc", new Dictionary<ushort, DateTime>());
                SetField(instance, "_suppressedKeyUps", new Dictionary<ushort, int>());
                SetField(instance, "_remoteSessionDetector", (Func<bool>)(() => false));
                SetField(instance, "_availableSequences", new List<ShortcutSequence>());
                SetListField(instance, "_sequenceCandidates");
                SetListField(instance, "_sequenceCandidatesBuffer");
                SetField(instance, "_disposed", false);
                SetField(instance, "_logger", LoggerService.Instance);

                KeyChordParser.TryParsePrefix("CTRL+Q", out var defaultChord, out var parseError)
                    .Should().BeTrue($"fallback prefix should parse: {parseError}");

                var updatePrefix = serviceType.GetMethod(
                    "UpdatePrefix",
                    BindingFlags.Instance | BindingFlags.Public)!;

                updatePrefix.Invoke(instance, new object?[] { "Enter", false });

                GetField<bool>(instance, "_usingFallbackPrefix").Should().BeTrue();
                GetField<string>(instance, "_prefixText").Should().Be(defaultChord.NormalizedString);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                timer.Dispose();
                dispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {name} should exist");
        field!.SetValue(target, value);
    }

    private static void SetListField(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {name} should exist");
        var value = Activator.CreateInstance(field!.FieldType);
        value.Should().NotBeNull($"field {name} could not be instantiated");
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {name} should exist");
        return (T)field!.GetValue(target)!;
    }
}
