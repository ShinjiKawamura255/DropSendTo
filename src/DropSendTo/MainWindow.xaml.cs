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
    private AppConfig _config;
    private int _currentLayer = 0; // 0..3

    public MainWindow()
    {
        InitializeComponent();
        _configService = new ConfigService();
        _launcher = new LauncherService();
        _config = _configService.LoadOrCreate();
        _currentLayer = Math.Clamp(_config.CurrentLayer, 0, 3);
        Title = "DropSendTo (Layer " + (_currentLayer + 1) + ")";
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
        }
    }

    private void RemoveSlot(FrameworkElement fe)
    {
        int idx = GetSlotIndex(fe);
        _config.Layers[_currentLayer].Slots[idx] = new SlotModel();
        _configService.Save(_config);
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
}

