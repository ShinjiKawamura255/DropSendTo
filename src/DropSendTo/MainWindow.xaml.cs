using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DropSendTo.Models;
using DropSendTo.Services;

namespace DropSendTo;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly LauncherService _launcher;
    private readonly WindowPlacementService _placement = new();
    private AppConfig _config;
    private int _currentLayer = 0; // 0..3

    public MainWindow()
    {
        InitializeComponent();
        _configService = new ConfigService();
        _launcher = new LauncherService();
        _config = _configService.LoadOrCreate();
        _currentLayer = Math.Clamp(_config.CurrentLayer, 0, 3);

        // Restore window position with clamping
        var bounds = new ScreenBounds(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
        {
            var (l, t) = _placement.Clamp(_config.WindowLeft.Value, _config.WindowTop.Value, bounds, this.Width, this.Height);
            Left = l; Top = t;
        }
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();
        this.Closing += (_, _) =>
        {
            var (l, t) = _placement.Clamp(this.Left, this.Top, bounds, this.Width, this.Height);
            _config.WindowLeft = l;
            _config.WindowTop = t;
            _config.CurrentLayer = _currentLayer;
            _configService.Save(_config);
        };
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        _config.CurrentLayer = _currentLayer;
        _configService.Save(_config);
        Close();
    }

    private void OnLayer1(object sender, RoutedEventArgs e) => SetLayer(0);
    private void OnLayer2(object sender, RoutedEventArgs e) => SetLayer(1);
    private void OnLayer3(object sender, RoutedEventArgs e) => SetLayer(2);
    private void OnLayer4(object sender, RoutedEventArgs e) => SetLayer(3);

    private void SetLayer(int index)
    {
        _currentLayer = index;
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
        RefreshUi();
    }

    private void OnSlotContextMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var cm = new ContextMenu();
        var miRegister = new MenuItem { Header = "Register..." };
        miRegister.Click += (_, _) => RegisterSlot(fe);
        var miEdit = new MenuItem { Header = "Edit..." };
        miEdit.Click += (_, _) => EditSlot(fe);
        var miRemove = new MenuItem { Header = "Remove" };
        miRemove.Click += (_, _) => RemoveSlot(fe);
        cm.Items.Add(miRegister);
        cm.Items.Add(miEdit);
        cm.Items.Add(new Separator());
        cm.Items.Add(miRemove);
        cm.Items.Add(new Separator());
        var idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        var toggle = new MenuItem { Header = slot.ClickEnabled ? "Disable Click Launch" : "Enable Click Launch" };
        toggle.Click += (_, _) => { slot.ClickEnabled = !slot.ClickEnabled; _configService.Save(_config); };
        cm.Items.Add(toggle);
        fe.ContextMenu = cm;
        cm.IsOpen = true;
    }

    private int GetSlotIndex(FrameworkElement fe)
    {
        int row = Grid.GetRow(fe);
        int col = Grid.GetColumn(fe);
        return row * 2 + col; // 0..3
    }

    private void RegisterSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        var dlg = new RegisterDialog();
        if (dlg.ShowDialog() == true)
        {
            _config.Layers[_currentLayer].Slots[idx] = new SlotModel
            {
                Title = dlg.AppTitle,
                Command = dlg.CommandPath,
                ArgumentsTemplate = dlg.ArgumentsTemplate
            };
            _configService.Save(_config);
            RefreshUi();
        }
    }

    private void EditSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        var dlg = new RegisterDialog(slot);
        if (dlg.ShowDialog() == true)
        {
            slot.Title = dlg.AppTitle;
            slot.Command = dlg.CommandPath;
            slot.ArgumentsTemplate = dlg.ArgumentsTemplate;
            _configService.Save(_config);
            RefreshUi();
        }
    }

    private void RemoveSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        _config.Layers[_currentLayer].Slots[idx] = new SlotModel();
        _configService.Save(_config);
        RefreshUi();
    }

    private void OnSlotDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (sender is not FrameworkElement fe) return;
            int idx = GetSlotIndex(fe);
            var slot = _config.Layers[_currentLayer].Slots[idx];
            if (string.IsNullOrWhiteSpace(slot.Command))
            {
                MessageBox.Show("No app registered for this slot.", "DropSendTo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = _launcher.Launch(slot, paths);
            if (!result.Success)
            {
                MessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Drop Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (this.ContextMenu != null)
        {
            this.ContextMenu.PlacementTarget = (UIElement)sender;
            this.ContextMenu.IsOpen = true;
        }
    }

    private void OnMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta > 0) SetLayer((_currentLayer + 3) % 4); else SetLayer((_currentLayer + 1) % 4);
    }

    private void OnDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* ignore */ }
        }
    }

    private void OnSlotMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border b) { b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32)); b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray); }
    }
    private void OnSlotMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border b) { b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 17, 17)); b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)); }
    }
    private void OnSlotDragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border b) { b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48)); b.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White); }
    }
    private void OnSlotDragLeave(object sender, DragEventArgs e)
    {
        OnSlotMouseLeave(sender, null!);
    }

    private void OnSlotClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        int idx = GetSlotIndex(fe);
        var slot = _config.Layers[_currentLayer].Slots[idx];
        if (!slot.ClickEnabled) return;
        if (string.IsNullOrWhiteSpace(slot.Command)) return;
        var result = _launcher.Launch(slot, Array.Empty<string>());
        if (!result.Success)
            MessageBox.Show(result.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void RefreshUi()
    {
        var layer = _config.Layers[_currentLayer];
        Slot1Text.Text = string.IsNullOrWhiteSpace(layer.Slots[0].Title) ? "Slot 1" : layer.Slots[0].Title;
        Slot2Text.Text = string.IsNullOrWhiteSpace(layer.Slots[1].Title) ? "Slot 2" : layer.Slots[1].Title;
        Slot3Text.Text = string.IsNullOrWhiteSpace(layer.Slots[2].Title) ? "Slot 3" : layer.Slots[2].Title;
        Slot4Text.Text = string.IsNullOrWhiteSpace(layer.Slots[3].Title) ? "Slot 4" : layer.Slots[3].Title;
        // Layer button highlight
        LayerBtn1.Opacity = _currentLayer == 0 ? 1.0 : 0.6;
        LayerBtn2.Opacity = _currentLayer == 1 ? 1.0 : 0.6;
        LayerBtn3.Opacity = _currentLayer == 2 ? 1.0 : 0.6;
        LayerBtn4.Opacity = _currentLayer == 3 ? 1.0 : 0.6;
    }
}
