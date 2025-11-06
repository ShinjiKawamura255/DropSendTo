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

public class ShortcutServicePrefixToggleTests
{
    private const uint VkTab = 0x09;
    private const ushort VkControl = 0x11;
    private const ushort VkQ = 0x51;

    [Fact]
    public void ProcessKeyDown_WithPrefixTab_ShouldReturnToggleAction()
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
                SetField(instance, "_modifierLastPressedUtc", new Dictionary<ushort, DateTime>
                {
                    [VkControl] = DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(500))
                });
                SetField(instance, "_suppressedKeyUps", new Dictionary<ushort, int>());
                SetField(instance, "_prefixChord", new KeyChord(VkQ, "Q", new[] { ModifierKind.Control }, "CTRL+Q"));
                SetField(instance, "_prefixText", "CTRL+Q");
                SetField(instance, "_prefixModifiers", new List<ModifierKind> { ModifierKind.Control });
                SetField(instance, "_prefixArmed", true);
                SetField(instance, "_prefixArmedAtUtc", DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(200)));
                SetField(instance, "_availableShortcuts", new List<KeyChord>());
                SetField(instance, "_disposed", false);

                var processKeyDown = serviceType.GetMethod(
                    "ProcessKeyDown",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                var args = new object?[] { VkTab, false };
                var action = processKeyDown.Invoke(instance, args)!;
                var suppress = (bool)args[1]!;

                suppress.Should().BeTrue();
                var actionType = GetActionTypeName(serviceType, action);
                actionType.Should().Be("PrefixTogglePosition");
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
}
