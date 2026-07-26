using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ZapretWrapper.Models;
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
            Title = "Выберите папку zapret (zapret2 или zapret-discord-youtube)",
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
            MessageBox.Show("Укажите путь к папке zapret.", "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var layout = ZapretBackend.Validate(path);
        if (!layout.IsValid)
        {
            MessageBox.Show(
                (layout.Error ?? "В указанной папке не найдены обязательные файлы:\n  • "
                                 + string.Join("\n  • ", layout.Missing))
                + "\n\nПоддерживаются две сборки:"
                + "\n  • zapret-discord-youtube (архив со страницы релизов Flowseal)"
                + "\n  • zapret2 / zapret-win-bundle (bol-van)"
                + "\n\nВ git-клоне бинарников нет — нужен именно архив релиза.",
                "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SettingsService.Current.ZapretPath = path;
        SettingsService.Save();

        // Папка сменилась — сборка могла стать другой, поэтому список стратегий
        // нужно читать заново, а аргументы от предыдущей сборки выбросить.
        StrategyCatalog.Reload(force: true);

        var selected = SettingsService.Current.SelectedStrategyId;
        if (!string.IsNullOrEmpty(selected) && StrategyCatalog.FindById(selected) is null)
        {
            SettingsService.Current.SelectedStrategyId = null;
            SettingsService.Save();
            LogService.Info("Выбранная стратегия сброшена: в новой папке её нет.");
        }

        Validate();

        MessageBox.Show(
            $"Путь сохранён. Определена сборка: {layout.Label}.\n{StrategyCatalog.SourceDescription}",
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
            ValidationList.ItemsSource = new[]
            {
                "Выберите папку с zapret: подходит как zapret-discord-youtube, так и zapret2."
            };
            ValidationPanel.Background = (Brush)Application.Current.Resources["NeutralLightBrush"];
            ValidationPanel.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
            return;
        }

        var layout = ZapretBackend.Validate(path);
        if (layout.IsValid)
        {
            ValidationTitle.Text = "✓ Сборка опознана: " + layout.Label;
            ValidationList.ItemsSource = layout.Flavor == ZapretFlavor.Flowseal
                ? new[]
                {
                    "bin/winws.exe — OK",
                    "bin/cygwin1.dll — OK",
                    "bin/WinDivert.dll — OK",
                    "lists/ — OK",
                }
                : new[]
                {
                    "binaries/windows-x86_64/winws2.exe — OK",
                    "lua/ — OK",
                    "files/fake/ — OK",
                };
            ValidationPanel.Background = (Brush)Application.Current.Resources["SuccessLightBrush"];
            ValidationPanel.BorderBrush = (Brush)Application.Current.Resources["SuccessBrush"];
            return;
        }

        if (layout.Error is not null)
        {
            ValidationTitle.Text = "✗ " + layout.Error;
            ValidationList.ItemsSource = Array.Empty<string>();
        }
        else
        {
            ValidationTitle.Text = "✗ Не хватает файлов:";
            ValidationList.ItemsSource = layout.Missing.ToArray();
        }

        ValidationPanel.Background = (Brush)Application.Current.Resources["DangerLightBrush"];
        ValidationPanel.BorderBrush = (Brush)Application.Current.Resources["DangerBrush"];
    }
}
