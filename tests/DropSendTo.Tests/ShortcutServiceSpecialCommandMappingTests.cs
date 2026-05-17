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

public class ShortcutServiceSpecialCommandMappingTests
{
    private const uint VkTab = 0x09;
    private const uint VkReturn = 0x0D;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const uint VkSpace = 0x20;
    private const uint VkD = 0x44;
    private const ushort VkQ = 0x51;

    [Theory]
    [InlineData(VkReturn, "", "PrefixActivate")]
    [InlineData(VkSpace, "Alt", "PrefixSearch")]
    [InlineData(VkReturn, "Shift", "PrefixMinimize")]
    [InlineData(VkD, "Ctrl", "PrefixDropCapture")]
    public void ProcessKeyDown_ShouldMapResolverResultToShortcutAction(uint key, string modifier, string expectedAction)
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
                var activeModifiers = BuildModifiers(modifier);
                SetField(instance, "_stateLock", new object());
                SetField(instance, "_dispatcher", dispatcher);
                SetField(instance, "_prefixTimeoutTimer", timer);
                SetField(instance, "_activeModifiers", activeModifiers);
                SetField(instance, "_modifierLastPressedUtc", BuildPressedTimes(activeModifiers));
                SetField(instance, "_suppressedKeyUps", new Dictionary<ushort, int>());
                SetField(instance, "_remoteSessionDetector", (Func<bool>)(() => false));
                SetField(instance, "_prefixChord", new KeyChord(VkQ, "Q", new[] { ModifierKind.Control }, "CTRL+Q"));
                SetField(instance, "_prefixText", "CTRL+Q");
                SetField(instance, "_prefixModifiers", new List<ModifierKind> { ModifierKind.Control });
                SetField(instance, "_prefixArmed", true);
                SetField(instance, "_prefixArmedAtUtc", DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(200)));
                SetField(instance, "_prefixDropCaptureEnabled", true);
                SetField(instance, "_availableSequences", new List<ShortcutSequence>());
                SetListField(instance, "_sequenceCandidates");
                SetListField(instance, "_sequenceCandidatesBuffer");
                SetField(instance, "_awaitingFirstShortcutKey", false);
                SetField(instance, "_disposed", false);

                var processKeyDown = serviceType.GetMethod(
                    "ProcessKeyDown",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                var args = new object?[] { key, false };
                var action = processKeyDown.Invoke(instance, args)!;
                var suppress = (bool)args[1]!;

                suppress.Should().BeTrue();
                GetActionTypeName(serviceType, action).Should().Be(expectedAction);
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

    private static HashSet<ushort> BuildModifiers(string modifier)
    {
        return modifier switch
        {
            "Alt" => new HashSet<ushort> { VkMenu },
            "Shift" => new HashSet<ushort> { VkShift },
            "Ctrl" => new HashSet<ushort> { VkControl },
            _ => new HashSet<ushort>()
        };
    }

    private static Dictionary<ushort, DateTime> BuildPressedTimes(IEnumerable<ushort> modifiers)
    {
        var now = DateTime.UtcNow;
        var result = new Dictionary<ushort, DateTime>
        {
            [VkControl] = now.Subtract(TimeSpan.FromMilliseconds(500))
        };
        foreach (var modifier in modifiers)
        {
            result[modifier] = now;
        }

        return result;
    }

    private static string GetActionTypeName(Type serviceType, object action)
    {
        var actionType = action.GetType();
        var typeProperty = actionType.GetProperty("Type", BindingFlags.Instance | BindingFlags.Public);
        typeProperty.Should().NotBeNull();
        var enumValue = typeProperty!.GetValue(action);
        enumValue.Should().NotBeNull();
        var enumType = serviceType.GetNestedType("ShortcutActionType", BindingFlags.NonPublic);
        enumType.Should().NotBeNull();
        return Enum.GetName(enumType!, enumValue!)!;
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
}
