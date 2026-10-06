using System;
using System.Runtime.InteropServices;

namespace DropSendTo.Services;

internal sealed class NativeMacroDisplayAdapter : IMacroDisplayAdapter
{
    public static NativeMacroDisplayAdapter Instance { get; } = new();
    private const uint OnlyActivePaths = 2;
    private const int InsufficientBuffer = 122;

    public bool TryGetBounds(int displayNumber, out MacroDisplayBounds bounds, out string? error)
    {
        bounds = default;
        error = null;
        // Query fresh topology for every command. A topology change can invalidate the buffer sizes.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(OnlyActivePaths, out var pathCount, out var modeCount);
            if (result != 0)
            {
                error = $"ディスプレイ情報の取得に失敗しました (Windows error {result})。";
                return false;
            }
            var paths = new DisplayPath[pathCount];
            var modes = new DisplayMode[modeCount];
            result = QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == InsufficientBuffer)
            {
                continue;
            }
            if (result != 0)
            {
                error = $"ディスプレイ情報の取得に失敗しました (Windows error {result})。";
                return false;
            }
            return TrySelectBounds(displayNumber, paths, pathCount, modes, modeCount, out bounds, out error);
        }
        error = "ディスプレイ構成が変更されたため、領域を取得できませんでした。";
        return false;
    }

    internal static bool TrySelectBounds(int number, DisplayPath[] paths, uint pathCount,
        DisplayMode[] modes, uint modeCount, out MacroDisplayBounds bounds, out string? error)
    {
        bounds = default;
        error = null;
        // The active CCD path index is the numbering used by Microsoft's PowerToys PowerDisplay.
        // Do not use Screen.AllScreens order, primary-first sorting, or GDI DISPLAYn suffixes.
        if (number <= 0 || number > pathCount || pathCount > paths.Length || modeCount > modes.Length)
        {
            error = $"ディスプレイ {number} が見つかりません。";
            return false;
        }
        var source = paths[number - 1].Source;
        if (source.ModeIndex >= modeCount)
        {
            error = "ディスプレイの座標を取得できませんでした。";
            return false;
        }
        var mode = modes[source.ModeIndex];
        if (mode.Type != 1 || mode.Id != source.Id ||
            mode.Adapter.Low != source.Adapter.Low || mode.Adapter.High != source.Adapter.High ||
            mode.Width == 0 || mode.Height == 0 || mode.Width > int.MaxValue || mode.Height > int.MaxValue)
        {
            error = "ディスプレイの領域が不正です。";
            return false;
        }
        bounds = new MacroDisplayBounds(mode.X, mode.Y, (int)mode.Width, (int)mode.Height);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterId
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplaySource
    {
        public AdapterId Adapter;
        public uint Id;
        public uint ModeIndex;
        public uint Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayTarget
    {
        public AdapterId Adapter;
        public uint Id;
        public uint ModeIndex;
        public int OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanlineOrdering;
        public int Available;
        public uint Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayPath
    {
        public DisplaySource Source;
        public DisplayTarget Target;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO has a 48-byte union; source mode occupies its first 20 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct DisplayMode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public AdapterId Adapter;
        [FieldOffset(16)] public uint Width;
        [FieldOffset(20)] public uint Height;
        [FieldOffset(24)] public uint PixelFormat;
        [FieldOffset(28)] public int X;
        [FieldOffset(32)] public int Y;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayPath[] paths,
        ref uint modeCount, [Out] DisplayMode[] modes, IntPtr topologyId);
}
