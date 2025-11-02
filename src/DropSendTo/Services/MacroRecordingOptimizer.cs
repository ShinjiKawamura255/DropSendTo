using System;
using System.Collections.Generic;
using System.Linq;

namespace DropSendTo.Services;

internal static class MacroRecordingOptimizer
{
    public static IReadOnlyList<string> Optimize(IReadOnlyList<MacroRecordingEvent> events)
    {
        if (events.Count == 0)
        {
            return Array.Empty<string>();
        }

        var optimizer = new Optimizer(events);
        return optimizer.Run();
    }

    private sealed class Optimizer
    {
        private readonly IReadOnlyList<MacroRecordingEvent> _events;
        private readonly List<EventData> _eventData;
        private readonly List<ModifierState> _allModifiers;
        private readonly Dictionary<string, ModifierState> _activeModifiers;
        private readonly Dictionary<string, KeyTap> _activeKeys;

        public Optimizer(IReadOnlyList<MacroRecordingEvent> events)
        {
            _events = events;
            _eventData = new List<EventData>(events.Count);
            _allModifiers = new List<ModifierState>();
            _activeModifiers = new Dictionary<string, ModifierState>(StringComparer.OrdinalIgnoreCase);
            _activeKeys = new Dictionary<string, KeyTap>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> Run()
        {
            BuildEventData();
            DecideModifierStrategies();
            return MaterializeCommands();
        }

        private void BuildEventData()
        {
            for (int index = 0; index < _events.Count; index++)
            {
                var current = _events[index];
                switch (current.Kind)
                {
                    case MacroRecordingEventKind.RawCommand:
                        _eventData.Add(EventData.CreateRaw(index, current.Value));
                        break;
                    case MacroRecordingEventKind.KeyDown:
                        HandleKeyDown(index, current);
                        break;
                    case MacroRecordingEventKind.KeyUp:
                        HandleKeyUp(index, current);
                        break;
                }
            }

            foreach (var tap in _activeKeys.Values)
            {
                tap.IsHold = true;
            }

            foreach (var modifier in _activeModifiers.Values)
            {
                modifier.Decision = ModifierDecision.Hold;
            }
        }

        private void HandleKeyDown(int index, MacroRecordingEvent current)
        {
            var data = EventData.CreateKeyboard(index, current);
            if (current.ModifierKind.HasValue)
            {
                var state = new ModifierState(current.Value, current.ModifierKind.Value, index, data);
                _activeModifiers[current.Value] = state;
                _allModifiers.Add(state);
                data.Modifier = state;
            }
            else
            {
                var tap = new KeyTap(current.Value, index, data);
                tap.Modifiers.AddRange(_activeModifiers.Values);
                _activeKeys[current.Value] = tap;
                data.Tap = tap;
            }
            _eventData.Add(data);
        }

        private void HandleKeyUp(int index, MacroRecordingEvent current)
        {
            var data = EventData.CreateKeyboard(index, current);
            if (current.ModifierKind.HasValue)
            {
                if (_activeModifiers.TryGetValue(current.Value, out var modifier))
                {
                    modifier.UpIndex = index;
                    modifier.UpEvent = data;
                    _activeModifiers.Remove(current.Value);
                    data.Modifier = modifier;
                }
                else
                {
                    data.MarkAsRawFallback($"KEYUP {current.Value}");
                }
            }
            else
            {
                if (_activeKeys.TryGetValue(current.Value, out var tap))
                {
                    tap.UpIndex = index;
                    tap.UpEvent = data;
                    _activeKeys.Remove(current.Value);
                    data.Tap = tap;
                    foreach (var modifier in tap.Modifiers)
                    {
                        modifier.UsageCount++;
                    }
                }
                else
                {
                    data.MarkAsRawFallback($"KEYUP {current.Value}");
                }
            }
            _eventData.Add(data);
        }

        private void DecideModifierStrategies()
        {
            foreach (var modifier in _allModifiers)
            {
                if (modifier.Decision == ModifierDecision.Hold)
                {
                    continue;
                }

                if (modifier.UpIndex is null)
                {
                    modifier.Decision = ModifierDecision.Hold;
                }
                else if (modifier.UsageCount == 0)
                {
                    modifier.Decision = ModifierDecision.ToKey;
                }
                else if (modifier.UsageCount == 1)
                {
                    modifier.Decision = ModifierDecision.Combine;
                }
                else
                {
                    modifier.Decision = ModifierDecision.Hold;
                }
            }
        }

        private IReadOnlyList<string> MaterializeCommands()
        {
            var results = new List<string>(_eventData.Count);
            foreach (var data in _eventData)
            {
                if (data.IsRawCommand)
                {
                    results.Add(data.RawCommand!);
                    continue;
                }

                if (data.RawFallback)
                {
                    results.Add(data.RawFallbackLine!);
                    continue;
                }

                if (data.Event.Kind == MacroRecordingEventKind.KeyDown)
                {
                    if (data.Modifier != null)
                    {
                        if (data.Modifier.Decision == ModifierDecision.Hold)
                        {
                            results.Add($"KEYDOWN {data.Modifier.Token}");
                        }
                    }
                    else if (data.Tap != null && data.Tap.IsHold)
                    {
                        results.Add($"KEYDOWN {data.Tap.KeyToken}");
                    }
                }
                else if (data.Event.Kind == MacroRecordingEventKind.KeyUp)
                {
                    if (data.Modifier != null)
                    {
                        switch (data.Modifier.Decision)
                        {
                            case ModifierDecision.Hold:
                                results.Add($"KEYUP {data.Modifier.Token}");
                                break;
                            case ModifierDecision.ToKey:
                                results.Add($"KEY {data.Modifier.Token}");
                                break;
                        }
                    }
                    else if (data.Tap != null)
                    {
                        if (data.Tap.IsHold)
                        {
                            results.Add($"KEYUP {data.Tap.KeyToken}");
                        }
                        else
                        {
                            var modifiers = data.Tap.Modifiers
                                .Where(m => m.Decision == ModifierDecision.Combine)
                                .ToList();
                            var chord = modifiers.Count == 0
                                ? data.Tap.KeyToken
                                : BuildChordString(data.Tap.KeyToken, modifiers);
                            results.Add($"KEY {chord}");
                        }
                    }
                    else
                    {
                        results.Add($"KEYUP {data.Event.Value}");
                    }
                }
            }
            return results;
        }

        private static string BuildChordString(string mainToken, IReadOnlyCollection<ModifierState> modifiers)
        {
            if (modifiers.Count == 0)
            {
                return mainToken;
            }

            var ordered = modifiers
                .OrderBy(m => Array.IndexOf(ModifierOrder, m.Kind))
                .Select(m => m.Token)
                .ToList();
            ordered.Add(mainToken);
            return string.Join('+', ordered);
        }
    }

    private sealed class EventData
    {
        private EventData(int index, MacroRecordingEvent @event)
        {
            Index = index;
            Event = @event;
        }

        public int Index { get; }
        public MacroRecordingEvent Event { get; }
        public ModifierState? Modifier { get; set; }
        public KeyTap? Tap { get; set; }
        public bool RawFallback { get; private set; }
        public string? RawFallbackLine { get; private set; }

        public bool IsRawCommand => Event.Kind == MacroRecordingEventKind.RawCommand;
        public string? RawCommand => IsRawCommand ? Event.Value : null;

        public static EventData CreateRaw(int index, string command) =>
            new(index, MacroRecordingEvent.Raw(command));

        public static EventData CreateKeyboard(int index, MacroRecordingEvent @event) =>
            new(index, @event);

        public void MarkAsRawFallback(string line)
        {
            RawFallback = true;
            RawFallbackLine = line;
        }
    }

    private sealed class ModifierState
    {
        public ModifierState(string token, ModifierKind kind, int downIndex, EventData downEvent)
        {
            Token = token;
            Kind = kind;
            DownIndex = downIndex;
            DownEvent = downEvent;
        }

        public string Token { get; }
        public ModifierKind Kind { get; }
        public int DownIndex { get; }
        public EventData DownEvent { get; }
        public int? UpIndex { get; set; }
        public EventData? UpEvent { get; set; }
        public int UsageCount { get; set; }
        public ModifierDecision Decision { get; set; } = ModifierDecision.Unknown;
    }

    private sealed class KeyTap
    {
        public KeyTap(string keyToken, int downIndex, EventData downEvent)
        {
            KeyToken = keyToken;
            DownIndex = downIndex;
            DownEvent = downEvent;
        }

        public string KeyToken { get; }
        public int DownIndex { get; }
        public EventData DownEvent { get; }
        public int? UpIndex { get; set; }
        public EventData? UpEvent { get; set; }
        public bool IsHold { get; set; }
        public List<ModifierState> Modifiers { get; } = new();
    }

    private enum ModifierDecision
    {
        Unknown,
        Hold,
        Combine,
        ToKey
    }

    private static readonly ModifierKind[] ModifierOrder =
    {
        ModifierKind.Control,
        ModifierKind.Shift,
        ModifierKind.Alt,
        ModifierKind.Win,
        ModifierKind.LeftWin,
        ModifierKind.RightWin
    };
}
