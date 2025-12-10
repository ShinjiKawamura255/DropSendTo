using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal sealed class ConfigTransferService
{
    private const int PackageVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 200_000;

    public string CreateExportPayload(AppConfig config, string password)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("パスワードを指定してください。", nameof(password));

        var snapshot = ExportConfigSnapshot.FromAppConfig(config);
        var json = JsonSerializer.Serialize(snapshot);
        var package = Encrypt(json, password);
        return JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
    }

    public AppConfig ImportConfig(string payload, string password)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("インポートデータが空です。", nameof(payload));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("パスワードを指定してください。", nameof(password));

        ConfigExportPackage package;
        try
        {
            package = JsonSerializer.Deserialize<ConfigExportPackage>(payload) ?? throw new InvalidOperationException("エクスポートファイルの形式が不正です。");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("エクスポートファイルの読み込みに失敗しました。", ex);
        }

        string plaintext;
        try
        {
            plaintext = Decrypt(package, password);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("パスワードが正しくありません。", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("エクスポートファイルの復号に失敗しました。", ex);
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<ExportConfigSnapshot>(plaintext) ?? throw new InvalidOperationException("コンフィグデータの形式が不正です。");
            return snapshot.ToAppConfig();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("コンフィグデータの解析に失敗しました。", ex);
        }
    }

    private static ConfigExportPackage Encrypt(string plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(password, salt, DefaultIterations);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return new ConfigExportPackage
        {
            PackageVersion = PackageVersion,
            KdfIterations = DefaultIterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            CipherText = Convert.ToBase64String(cipherBytes)
        };
    }

    private static string Decrypt(ConfigExportPackage package, string password)
    {
        if (package.PackageVersion != PackageVersion)
        {
            throw new InvalidOperationException("サポートされていないエクスポートバージョンです。");
        }

        var salt = Convert.FromBase64String(package.Salt ?? throw new InvalidOperationException("Salt が不足しています。"));
        var nonce = Convert.FromBase64String(package.Nonce ?? throw new InvalidOperationException("Nonce が不足しています。"));
        var tag = Convert.FromBase64String(package.Tag ?? throw new InvalidOperationException("Tag が不足しています。"));
        var cipher = Convert.FromBase64String(package.CipherText ?? throw new InvalidOperationException("暗号データが不足しています。"));
        var iterations = package.KdfIterations > 0 ? package.KdfIterations : DefaultIterations;
        var key = DeriveKey(password, salt, iterations);

        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
    }

    private sealed class ConfigExportPackage
    {
        public int PackageVersion { get; set; }
        public int KdfIterations { get; set; }
        public string? Salt { get; set; }
        public string? Nonce { get; set; }
        public string? Tag { get; set; }
        public string? CipherText { get; set; }
    }

    private sealed class ExportConfigSnapshot
    {
        public int Version { get; set; }
        public int CurrentLayer { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public bool AlwaysOnTop { get; set; }
        public StartupWindowBehavior StartupBehavior { get; set; }
        public WindowVisibilityState LastWindowVisibility { get; set; }
        public string ShortcutPrefix { get; set; } = string.Empty;
        public bool ShortcutPrefixDisabled { get; set; }
        public bool EnablePrefixLayerShortcuts { get; set; }
        public bool EnableEmacsNavigation { get; set; }
        public bool EnableViNavigation { get; set; }
        public bool HideEmptySlotNames { get; set; }
        public bool EnableMouseGestures { get; set; }
        public int MouseGestureClockwiseTurnsToShow { get; set; }
        public int MouseGestureCounterClockwiseTurnsToHide { get; set; }
        public bool MouseGestureInvertDirections { get; set; }
        public bool MouseGestureRequireCtrl { get; set; }
        public bool MouseGestureSuppressDuringPresentation { get; set; }
        public bool MouseGestureEnforceRadiusLimit { get; set; }
        public int MouseGestureMaxRadiusPixels { get; set; }
        public int MouseGestureShowLayerWhenVisible { get; set; }
        public int MouseGestureShowLayerWhenHidden { get; set; }
        public int PrefixShowLayerWhenVisible { get; set; }
        public int PrefixShowLayerWhenHidden { get; set; }
        public ExportMinimizeOptions? DefaultMinimizeOptions { get; set; }
        public bool SearchHotkeyEnabled { get; set; }
        public string SearchHotkey { get; set; } = string.Empty;
        public int SlotRows { get; set; }
        public int SlotColumns { get; set; }
        public SlotSize SlotSize { get; set; }
        public CustomSlotSizeOptions? CustomSlotSize { get; set; }
        public bool PreferRemoteSessions { get; set; } = true;
        public List<ExportLayerSnapshot> Layers { get; set; } = new();

        public static ExportConfigSnapshot FromAppConfig(AppConfig config)
        {
            return new ExportConfigSnapshot
            {
                Version = config.Version,
                CurrentLayer = config.CurrentLayer,
                WindowLeft = config.WindowLeft,
                WindowTop = config.WindowTop,
                AlwaysOnTop = config.AlwaysOnTop,
                StartupBehavior = config.StartupBehavior,
                LastWindowVisibility = config.LastWindowVisibility,
                ShortcutPrefix = config.ShortcutPrefix,
                ShortcutPrefixDisabled = config.ShortcutPrefixDisabled,
                EnablePrefixLayerShortcuts = config.EnablePrefixLayerShortcuts,
                EnableEmacsNavigation = config.EnableEmacsNavigation,
                EnableViNavigation = config.EnableViNavigation,
                HideEmptySlotNames = config.HideEmptySlotNames,
                EnableMouseGestures = config.EnableMouseGestures,
                MouseGestureClockwiseTurnsToShow = config.MouseGestureClockwiseTurnsToShow,
                MouseGestureCounterClockwiseTurnsToHide = config.MouseGestureCounterClockwiseTurnsToHide,
                MouseGestureInvertDirections = config.MouseGestureInvertDirections,
                MouseGestureRequireCtrl = config.MouseGestureRequireCtrl,
                MouseGestureSuppressDuringPresentation = config.MouseGestureSuppressDuringPresentation,
                MouseGestureEnforceRadiusLimit = config.MouseGestureEnforceRadiusLimit,
                MouseGestureMaxRadiusPixels = config.MouseGestureMaxRadiusPixels,
                MouseGestureShowLayerWhenVisible = config.MouseGestureShowLayerWhenVisible,
                MouseGestureShowLayerWhenHidden = config.MouseGestureShowLayerWhenHidden,
                PrefixShowLayerWhenVisible = config.PrefixShowLayerWhenVisible,
                PrefixShowLayerWhenHidden = config.PrefixShowLayerWhenHidden,
                DefaultMinimizeOptions = ExportMinimizeOptions.FromOptions(config.DefaultMinimizeOptions ?? SlotMinimizeOptions.CreateDefault()),
                SearchHotkeyEnabled = config.SearchHotkeyEnabled,
                SearchHotkey = config.SearchHotkey ?? string.Empty,
                PreferRemoteSessions = config.PreferRemoteSessions,
                SlotRows = config.SlotRows,
                SlotColumns = config.SlotColumns,
                SlotSize = config.SlotSize,
                CustomSlotSize = config.CustomSlotSize?.Clone(),
                Layers = config.Layers.Select(ExportLayerSnapshot.FromLayer).ToList()
            };
        }

        public AppConfig ToAppConfig()
        {
            var layers = Layers ?? new List<ExportLayerSnapshot>();
            var normalizedLayers = new List<Layer>();
            for (int i = 0; i < 4; i++)
            {
                var snapshot = i < layers.Count ? layers[i] : null;
                normalizedLayers.Add(snapshot?.ToLayer() ?? new Layer());
            }

            return new AppConfig
            {
                Version = Version,
                CurrentLayer = CurrentLayer,
                WindowLeft = WindowLeft,
                WindowTop = WindowTop,
                AlwaysOnTop = AlwaysOnTop,
                StartupBehavior = StartupBehavior,
                LastWindowVisibility = LastWindowVisibility,
                ShortcutPrefix = ShortcutPrefix ?? string.Empty,
                ShortcutPrefixDisabled = ShortcutPrefixDisabled,
                EnablePrefixLayerShortcuts = EnablePrefixLayerShortcuts,
                EnableEmacsNavigation = EnableEmacsNavigation,
                EnableViNavigation = EnableViNavigation,
                HideEmptySlotNames = HideEmptySlotNames,
                EnableMouseGestures = EnableMouseGestures,
                MouseGestureClockwiseTurnsToShow = MouseGestureClockwiseTurnsToShow,
                MouseGestureCounterClockwiseTurnsToHide = MouseGestureCounterClockwiseTurnsToHide,
                MouseGestureInvertDirections = MouseGestureInvertDirections,
                MouseGestureRequireCtrl = MouseGestureRequireCtrl,
                MouseGestureSuppressDuringPresentation = MouseGestureSuppressDuringPresentation,
                MouseGestureEnforceRadiusLimit = MouseGestureEnforceRadiusLimit,
                MouseGestureMaxRadiusPixels = MouseGestureMaxRadiusPixels,
                MouseGestureShowLayerWhenVisible = MouseGestureShowLayerWhenVisible,
                MouseGestureShowLayerWhenHidden = MouseGestureShowLayerWhenHidden,
                PrefixShowLayerWhenVisible = PrefixShowLayerWhenVisible,
                PrefixShowLayerWhenHidden = PrefixShowLayerWhenHidden,
                DefaultMinimizeOptions = DefaultMinimizeOptions?.ToOptions() ?? SlotMinimizeOptions.CreateDefault(),
                SearchHotkeyEnabled = SearchHotkeyEnabled,
                SearchHotkey = SearchHotkey ?? string.Empty,
                PreferRemoteSessions = PreferRemoteSessions,
                SlotRows = SlotRows,
                SlotColumns = SlotColumns,
                SlotSize = SlotSize,
                CustomSlotSize = CustomSlotSize?.Clone() ?? CustomSlotSizeOptions.CreateDefault(),
                Layers = normalizedLayers
            };
        }
    }

    private sealed class ExportLayerSnapshot
    {
        public List<ExportSlotSnapshot> Slots { get; set; } = new();

        public static ExportLayerSnapshot FromLayer(Layer layer)
        {
            var snapshot = new ExportLayerSnapshot();
            if (layer.Slots != null)
            {
                snapshot.Slots = layer.Slots.Select(ExportSlotSnapshot.FromSlot).ToList();
            }
            return snapshot;
        }

        public Layer ToLayer()
        {
            var layer = new Layer();
            layer.Slots = Slots?.Select(s => s.ToSlot()).ToList() ?? new List<SlotModel>();
            return layer;
        }
    }

    private sealed class ExportSlotSnapshot
    {
        public string? Title { get; set; }
        public string? Command { get; set; }
        public string? ArgumentsTemplate { get; set; }
        public string? IconPath { get; set; }
        public bool ClickEnabled { get; set; } = true;
        public string? ShortcutKey { get; set; }
        public string? KeyboardMacroScript { get; set; }
        public SlotExecutionMode ExecutionMode { get; set; }
        public SlotAccentColor AccentColor { get; set; }
        public string? SearchKeywords { get; set; }
        public ExportMinimizeOptions? MinimizeOptions { get; set; }

        public static ExportSlotSnapshot FromSlot(SlotModel slot)
        {
            return new ExportSlotSnapshot
            {
                Title = slot.Title,
                Command = slot.Command,
                ArgumentsTemplate = slot.ArgumentsTemplate,
                IconPath = slot.IconPath,
                ClickEnabled = slot.ClickEnabled,
                ShortcutKey = slot.ShortcutKey,
                KeyboardMacroScript = slot.KeyboardMacroScript,
                ExecutionMode = slot.ExecutionMode,
                AccentColor = slot.AccentColor,
                SearchKeywords = slot.SearchKeywords,
                MinimizeOptions = ExportMinimizeOptions.FromOptions(slot.MinimizeOptions ?? SlotMinimizeOptions.CreateDefault())
            };
        }

        public SlotModel ToSlot()
        {
            return new SlotModel
            {
                Title = Title,
                Command = Command,
                ArgumentsTemplate = string.IsNullOrWhiteSpace(ArgumentsTemplate) ? "{args}" : ArgumentsTemplate,
                IconPath = IconPath,
                ClickEnabled = ClickEnabled,
                ShortcutKey = ShortcutKey ?? string.Empty,
                KeyboardMacroScript = KeyboardMacroScript ?? string.Empty,
                ExecutionMode = ExecutionMode,
                AccentColor = AccentColor,
                SearchKeywords = SearchKeywords ?? string.Empty,
                MinimizeOptions = MinimizeOptions?.ToOptions() ?? SlotMinimizeOptions.CreateDefault()
            };
        }
    }

    private sealed class ExportMinimizeOptions
    {
        public bool EnableOnClick { get; set; }
        public bool EnableOnShortcut { get; set; }
        public bool EnableOnDrop { get; set; }
        public bool EnableOnKeyboard { get; set; }

        public static ExportMinimizeOptions FromOptions(SlotMinimizeOptions options)
        {
            options ??= SlotMinimizeOptions.CreateDefault();
            return new ExportMinimizeOptions
            {
                EnableOnClick = options.EnableOnClick,
                EnableOnShortcut = options.EnableOnShortcut,
                EnableOnDrop = options.EnableOnDrop,
                EnableOnKeyboard = options.EnableOnKeyboard
            };
        }

        public SlotMinimizeOptions ToOptions()
        {
            return new SlotMinimizeOptions
            {
                EnableOnClick = EnableOnClick,
                EnableOnShortcut = EnableOnShortcut,
                EnableOnDrop = EnableOnDrop,
                EnableOnKeyboard = EnableOnKeyboard
            };
        }
    }
}
