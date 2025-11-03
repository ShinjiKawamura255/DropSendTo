using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutServiceResetTests
{
    [Fact]
    public void ResetPrefixState_WithClearModifiers_ShouldClearModifierTrackingCollections()
    {
        var serviceType = typeof(DropSendTo.Services.LoggerService).Assembly
            .GetType("DropSendTo.Services.ShortcutService", throwOnError: true)!;

        object instance = RuntimeHelpers.GetUninitializedObject(serviceType);
        var timer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);

        try
        {
            SetField(instance, "_stateLock", new object());
            SetField(instance, "_prefixTimeoutTimer", timer);
            SetField(instance, "_activeModifiers", new HashSet<ushort> { 0x11 });
            SetField(instance, "_modifierLastPressedUtc", new Dictionary<ushort, DateTime>
            {
                [0x11] = DateTime.UtcNow
            });
            SetField(instance, "_suppressedKeyUps", new Dictionary<ushort, int>
            {
                [0x41] = 2
            });
            SetField(instance, "_prefixArmed", false);
            SetField(instance, "_disposed", false);

            var resetMethod = serviceType.GetMethod(
                "ResetPrefixState",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null)!;

            resetMethod.Invoke(instance, new object[] { true });

            GetField<HashSet<ushort>>(instance, "_activeModifiers").Should().BeEmpty();
            GetField<Dictionary<ushort, DateTime>>(instance, "_modifierLastPressedUtc").Should().BeEmpty();
            GetField<Dictionary<ushort, int>>(instance, "_suppressedKeyUps").Should().BeEmpty();
        }
        finally
        {
            timer.Dispose();
        }
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {name} should exist");
        field!.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {name} should exist");
        return (T)field!.GetValue(target)!;
    }
}
