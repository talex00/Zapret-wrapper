using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ZapretWrapper.Services;
using ZapretWrapper.Styles;

namespace ZapretWrapper.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadFromSettings();
        ThemeManager.ThemeChanged += (_, _) => SyncThemeCombo();
    }

    private void LoadFromSettings()
    {
        var s = SettingsService.Current;
        ZapretPathBox.Text = s.ZapretPath ?? "";
        TestDomainsBox.Text = s.TestDomains ?? "";
        ConfigPathRun.Text = SettingsService.ConfigPath;
        SyncThemeCombo();
        Validate();
    }

    private void SyncThemeCombo()
    {
        if (ThemeCombo is null) return;
        var target = ThemeManager.Current switch
        {
            AppTheme.Light => "Light",
            AppTheme.Dark => "Dark",
            _ => "System"
        };
        foreach (var item in ThemeCombo.Items)
        {
            if (item is ComboBoxItem cbi && (cbi.Tag as string) == target)
            {
                ThemeCombo.SelectedItem = cbi;
                return;
            }
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not ComboBoxItem cbi) return;
        var tag = cbi.Tag as string;
        var theme = tag switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.System
        };
        ThemeManager.Apply(theme);
        SettingsService.Current.Theme = tag ?? "System";
        SettingsService.Save();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку zapret2",
        };
        if (!string.IsNullOrEmpty(ZapretPathBox.Text) && System.IO.Directory.Exists(ZapretPathBox.Text))
        {
            dlg.InitialDirectory = ZapretPathBox.Text;
        }
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            ZapretPathBox.Text = dlg.FolderName;
            Validate();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = ZapretPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("Укажите путь к zapret2.", "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var layout = ZapretLocator.Validate(path);
        if (!layout.IsValid)
        {
            MessageBox.Show(
                "В указанной папке не найдены обязательные файлы:\n  • " +
                string.Join("\n  • ", layout.Missing) +
                "\n\nСкачайте zapret-win-bundle с https://github.com/bol-van/zapret-win-bundle и распакуйте.",
                "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SettingsService.Current.ZapretPath = path;
        SettingsService.Save();
        MessageBox.Show("Путь сохранён. Теперь можно запускать стратегии.",
            "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveDomains_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.TestDomains = TestDomainsBox.Text?.Trim() ?? "";
        SettingsService.Save();
        MessageBox.Show("Список доменов сохранён.", "ZapretWrapper",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Validate()
    {
        var path = ZapretPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            ValidationTitle.Text = "Путь не указан";
            ValidationList.ItemsSource = new[] { "Выберите папку, в которой находится zapret2." };
            ValidationPanel.Background = (Brush)Application.Current.Resources["NeutralLightBrush"];
            ValidationPanel.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
            return;
        }

        var layout = ZapretLocator.Validate(path);
        if (layout.IsValid)
        {
            ValidationTitle.Text = "✓ Структура папки корректна";
            ValidationList.ItemsSource = new[]
            {
                "binaries/windows-x86_64/winws2.exe — OK",
                "lua/ — OK",
                "files/fake/ — OK",
            };
            ValidationPanel.Background = (Brush)Application.Current.Resources["SuccessLightBrush"];
            ValidationPanel.BorderBrush = (Brush)Application.Current.Resources["SuccessBrush"];
        }
        else
        {
            if (layout.Error is not null)
            {
                ValidationTitle.Text = "✗ " + layout.Error;
                ValidationList.ItemsSource = Array.Empty<string>();
                ValidationPanel.Background = (Brush)Application.Current.Resources["DangerLightBrush"];
                ValidationPanel.BorderBrush = (Brush)Application.Current.Resources["DangerBrush"];
            }
            else
            {
                ValidationTitle.Text = "✗ Не хватает файлов:";
                ValidationList.ItemsSource = layout.Missing.ToArray();
                ValidationPanel.Background = (Brush)Application.Current.Resources["DangerLightBrush"];
                ValidationPanel.BorderBrush = (Brush)Application.Current.Resources["DangerBrush"];
            }
        }
    }
}
