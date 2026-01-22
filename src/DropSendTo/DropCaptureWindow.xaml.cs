using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using DropSendTo.Models;

namespace DropSendTo;

public partial class DropCaptureWindow : Window
{
    public event EventHandler<DropCaptureEventArgs>? DropCompleted;
    public event EventHandler? CancelRequested;

    public DropCaptureWindow()
    {
        InitializeComponent();
    }

    public void SetLanguage(AppLanguage language)
    {
        if (TitleText != null)
        {
            TitleText.Text = language == AppLanguage.English ? "Drop here" : "ここにドロップ";
        }
        if (SubtitleText != null)
        {
            SubtitleText.Text = language == AppLanguage.English ? "Drop files or folders" : "ファイル/フォルダをドロップ";
        }
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        UpdateDragEffects(e);
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        UpdateDragEffects(e);
    }

    private static void UpdateDragEffects(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            DropCompleted?.Invoke(this, new DropCaptureEventArgs(paths));
        }

        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}

public sealed class DropCaptureEventArgs : EventArgs
{
    public DropCaptureEventArgs(IReadOnlyList<string> paths)
    {
        Paths = paths ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Paths { get; }
}
