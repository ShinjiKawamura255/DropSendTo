using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutServiceExternalMacroTests
{
    private const ushort Control = 0x11;
    private const ushort Q = 0x51;
    private const ushort Enter = 0x0D;
    private const uint Injected = 0x10;

    [Theory]
    [InlineData(false, 0x10u, false)]
    [InlineData(false, 0x12u, false)]
    [InlineData(true, 0x10u, true)]
    [InlineData(true, 0x12u, true)]
    [InlineData(false, 0u, true)]
    [InlineData(true, 0u, true)]
    public void Hook_ShouldRespectOptIn_WhenPrefixComesFromExternalOrPhysicalInput(bool enabled, uint flags, bool expected)
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(enabled);
            fixture.Key(Control, flags: flags);
            Assert.Equal(expected, fixture.Key(Q, flags: flags));
            Assert.Equal(expected, fixture.Service.IsPrefixArmed);
            Assert.Equal(expected, fixture.Key(Q, keyUp: true, flags: flags));
            fixture.Key(Control, keyUp: true, flags: flags);
            Assert.Empty(fixture.Modifiers);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hook_ShouldPreserveOwnMacroTags_RegardlessOfOptIn(bool enabled)
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(enabled);
            fixture.Key(Control, extraInfo: InputExtraInfo.MacroPassthroughTag);
            Assert.False(fixture.Key(Q, extraInfo: InputExtraInfo.MacroPassthroughTag));
            Assert.False(fixture.Service.IsPrefixArmed);
            Assert.Empty(fixture.Modifiers);

            fixture.Key(Control, extraInfo: InputExtraInfo.MacroInjectionTag);
            Assert.True(fixture.Key(Q, extraInfo: InputExtraInfo.MacroInjectionTag));
            Assert.True(fixture.Service.IsPrefixArmed);
            fixture.Key(Q, keyUp: true, extraInfo: InputExtraInfo.MacroInjectionTag);
            fixture.Key(Control, keyUp: true, extraInfo: InputExtraInfo.MacroInjectionTag);
            Assert.True(fixture.Key(Enter, extraInfo: InputExtraInfo.MacroPassthroughTag));
            Assert.False(fixture.Service.IsPrefixArmed);

            // Prefix passthrough must never arm Prefix again after the action completes.
            fixture.Key(Control, extraInfo: InputExtraInfo.MacroPassthroughTag);
            Assert.False(fixture.Key(Q, extraInfo: InputExtraInfo.MacroPassthroughTag));
            Assert.False(fixture.Service.IsPrefixArmed);
        });
    }

    [Fact]
    public void Hook_ShouldRespectRemotePreferenceAndDisabledPrefix_WhenExternalInputEnabled()
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(true);
            fixture.Set("_preferRemoteSessions", true);
            fixture.Set("_remoteSessionDetector", (Func<bool>)(() => true));
            fixture.Key(Control);
            Assert.False(fixture.Key(Q));
            Assert.False(fixture.Service.IsPrefixArmed);
            fixture.Set("_preferRemoteSessions", false);
            fixture.Service.UpdatePrefix("CTRL+Q", prefixDisabled: true);
            Assert.False(fixture.Key(Q));
            Assert.False(fixture.Service.IsPrefixArmed);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Hook_ShouldPassExplicitPrefixThroughWithoutChangingState_WhenPrefixIsArmedOrIdle(bool enabled, bool armed)
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(enabled);
            int passthroughs = 0;
            fixture.Service.PrefixPassthroughRequested += (_, _) => passthroughs++;
            if (armed)
            {
                fixture.Key(Control, flags: 0);
                Assert.True(fixture.Key(Q, flags: 0));
                // Keep the physical release pending while the macro sends the same key.
                fixture.Key(Control, keyUp: true, flags: 0);
            }

            foreach (var input in BuildPrefixInputs(passthrough: true))
                Assert.False(fixture.Key(input.Key, input.KeyUp, extraInfo: input.ExtraInfo));

            Assert.Equal(armed, fixture.Service.IsPrefixArmed);
            Assert.Empty(fixture.Modifiers);
            fixture.Drain();
            Assert.Equal(0, passthroughs);
            if (armed)
            {
                Assert.True(fixture.Key(Q, keyUp: true, flags: 0));
                Assert.True(fixture.Key(Enter, extraInfo: InputExtraInfo.MacroPassthroughTag));
                Assert.False(fixture.Service.IsPrefixArmed);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hook_ShouldArmAndAcceptOwnShortcut_WhenPrefixCommandIsNotPassthrough(bool enabled)
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(enabled);
            foreach (var input in BuildPrefixInputs(passthrough: false))
                Assert.Equal(input.Key == Q, fixture.Key(input.Key, input.KeyUp, extraInfo: input.ExtraInfo));

            Assert.True(fixture.Service.IsPrefixArmed);
            Assert.True(fixture.Key(Enter, extraInfo: InputExtraInfo.MacroPassthroughTag));
            Assert.False(fixture.Service.IsPrefixArmed);
        });
    }

    private static IEnumerable<(ushort Key, bool KeyUp, uint ExtraInfo)> BuildPrefixInputs(bool passthrough)
    {
        using var macro = new KeyboardMacroService();
        Assert.True(KeyChordParser.TryParse("CTRL+Q", out var chord, out _));
        macro.SetPrefixStateAccessors(() => chord, () => false, () => { });
        var inputType = typeof(KeyboardMacroService).GetNestedType("INPUT", BindingFlags.NonPublic)!;
        var buffer = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(inputType))!;
        var method = typeof(KeyboardMacroService).GetMethod("TryAppendPrefixSequence", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = new object?[] { buffer, passthrough, false, null };
        Assert.True((bool)method.Invoke(macro, args)!);
        Assert.Null(args[3]);
        Assert.Equal(4, buffer.Count);
        ushort[] keys = { Control, Q, Q, Control };
        for (int i = 0; i < buffer.Count; i++)
        {
            var union = inputType.GetField("u")!.GetValue(buffer[i])!;
            var keyboard = union.GetType().GetField("ki")!.GetValue(union)!;
            var extraInfo = (IntPtr)keyboard.GetType().GetField("dwExtraInfo")!.GetValue(keyboard)!;
            yield return (keys[i], i >= 2, checked((uint)extraInfo.ToInt64()));
        }
    }

    [Fact]
    public void Hook_ShouldTriggerRegisteredSequenceAndPassthrough_WhenExternalInputEnabled()
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(true);
            fixture.Service.UpdateAvailableShortcuts(new[] { "A B" });
            int shortcuts = 0;
            int passthroughs = 0;
            fixture.Service.ShortcutTriggered += (_, e) => { Assert.Equal("A B", e.RegisteredText); shortcuts++; };
            fixture.Service.PrefixPassthroughRequested += (_, _) => passthroughs++;
            fixture.Prefix();
            Assert.True(fixture.Key(0x41));
            Assert.True(fixture.Key(0x41, keyUp: true));
            Assert.True(fixture.Key(0x42));
            Assert.True(fixture.Key(0x42, keyUp: true));
            Assert.False(fixture.Service.IsPrefixArmed);
            fixture.Prefix();
            fixture.Prefix();
            Assert.False(fixture.Service.IsPrefixArmed);
            fixture.Drain();
            Assert.Equal(1, shortcuts);
            Assert.Equal(1, passthroughs);
        });
    }

    [Fact]
    public void Disable_ShouldClearTrackedStateAndDrainSuppressedRelease_WithoutAcceptingNewExternalKeys()
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(true);
            fixture.Key(Control);
            Assert.True(fixture.Key(Q));
            fixture.SetEnabled(false);
            Assert.False(fixture.Service.IsPrefixArmed);
            Assert.Empty(fixture.Modifiers);
            Assert.True(fixture.Key(Q, keyUp: true));
            Assert.False(fixture.Key(Q, keyUp: true));
            fixture.Key(Control, keyUp: true);
            fixture.Key(Control);
            Assert.False(fixture.Key(Q));
            Assert.False(fixture.Service.IsPrefixArmed);
            Assert.Empty(fixture.Modifiers);
        });
    }

    [Theory]
    [InlineData(0u, Injected)]
    [InlineData(Injected, 0u)]
    public void Hook_ShouldKeepSuppressedReleasesSeparate_WhenInputSourcesAreMixed(uint downFlags, uint unrelatedUpFlags)
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(true);
            fixture.Key(Control, flags: downFlags);
            Assert.True(fixture.Key(Q, flags: downFlags));
            Assert.False(fixture.Key(Q, keyUp: true, flags: unrelatedUpFlags));
            Assert.True(fixture.Key(Q, keyUp: true, flags: downFlags));
            fixture.Key(Control, keyUp: true, flags: downFlags);
        });
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(Injected)]
    public void Setting_ShouldNotResetArmedState_WhenValueIsUnchanged(uint flags)
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(true);
            fixture.Key(Control, flags: flags);
            Assert.True(fixture.Key(Q, flags: flags));
            fixture.SetEnabled(true);
            Assert.True(fixture.Service.IsPrefixArmed);
            Assert.Contains(Control, fixture.Modifiers);
            fixture.SetEnabled(false);
            Assert.True(fixture.Key(Q, keyUp: true, flags: flags));
        });
    }

    [Fact]
    public void Hook_ShouldSupportSearchHotkeyAndSystemKeyMessages_WhenExternalInputEnabled()
    {
        RunSta(() =>
        {
            using var fixture = new HookFixture();
            fixture.SetEnabled(true);
            fixture.Service.UpdateSearchHotkey("ALT+SPACE", enabled: true);
            int search = 0;
            fixture.Service.SearchHotkeyRequested += (_, _) => search++;
            fixture.Key(0x12, system: true);
            Assert.True(fixture.Key(0x20, system: true));
            Assert.True(fixture.Key(0x20, keyUp: true, system: true));
            fixture.Key(0x12, keyUp: true, system: true);
            fixture.Drain();
            Assert.Equal(1, search);
            Assert.Empty(fixture.Modifiers);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Hook test must finish without UI focus or a global hook.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class HookFixture : IDisposable
    {
        internal ShortcutService Service { get; } = (ShortcutService)RuntimeHelpers.GetUninitializedObject(typeof(ShortcutService));
        internal HashSet<ushort> Modifiers { get; } = new();
        private readonly Timer _timer = new(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        internal HookFixture()
        {
            Set("_stateLock", new object());
            Set("_dispatcher", _dispatcher);
            Set("_prefixTimeoutTimer", _timer);
            Set("_activeModifiers", Modifiers);
            Set("_modifierLastPressedUtc", new Dictionary<ushort, DateTime>());
            Set("_suppressedKeyUps", new Dictionary<ushort, int>());
            foreach (string name in new[] { "_availableSequences", "_sequenceCandidates", "_sequenceCandidatesBuffer" })
                Set(name, Activator.CreateInstance(Field(name).FieldType));
            Set("_remoteSessionDetector", (Func<bool>)(() => false));
            Service.UpdatePrefix("CTRL+Q", prefixDisabled: false);
        }

        internal void SetEnabled(bool enabled) => Service.SetAcceptExternalMacroInput(enabled);

        internal void Prefix()
        {
            Key(Control);
            Assert.True(Key(Q));
            Assert.True(Key(Q, keyUp: true));
            Key(Control, keyUp: true);
        }

        internal bool Key(ushort vk, bool keyUp = false, uint flags = Injected, uint extraInfo = 0, bool system = false)
        {
            var info = new HookInfo { VirtualKey = vk, Flags = flags, ExtraInfo = new UIntPtr(extraInfo) };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<HookInfo>());
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                var method = typeof(ShortcutService).GetMethod("KeyboardHookProc", BindingFlags.NonPublic | BindingFlags.Instance)!;
                int message = system ? (keyUp ? 0x105 : 0x104) : (keyUp ? 0x101 : 0x100);
                return (IntPtr)method.Invoke(Service, new object[] { 0, new IntPtr(message), ptr })! != IntPtr.Zero;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        internal void Set(string name, object? value) => Field(name).SetValue(Service, value);
        private static FieldInfo Field(string name) => typeof(ShortcutService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        internal void Drain() => _dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        public void Dispose() { Drain(); _timer.Dispose(); }

        [StructLayout(LayoutKind.Sequential)]
        private struct HookInfo
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }
    }
}
