using System.Windows.Controls;
using ZapretWrapper.Styles;

namespace ZapretWrapper.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        SyncComboToCurrentTheme();
        ThemeManager.ThemeChanged += (_, _) => SyncComboToCurrentTheme();
    }

    private void SyncComboToCurrentTheme()
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
    }
}
