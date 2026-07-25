using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ZapretWrapper.Services;
using ZapretWrapper.Styles;
using ZapretWrapper.Views;

namespace ZapretWrapper;

public partial class MainWindow : Window
{
    private bool _initialized;
    private readonly Dictionary<string, FrameworkElement> _pageCache = new();

    public MainWindow()
    {
        InitializeComponent();
        _initialized = true;

        SourceInitialized += (_, _) => WindowChrome.Apply(this);
        Activated += (_, _) => WindowChrome.Apply(this);
        ThemeManager.ThemeChanged += (_, _) => WindowChrome.Apply(this);

        SizeChanged += (_, e) =>
        {
            if (PageHost.Content is HomePage hp)
                hp.HandleResize(e.NewSize.Width);
        };

        if (App.Runner is ZapretRunner runner)
            runner.StateChanged += (_, _) => RefreshStatus();

        NavigateTo("home");
        Dispatcher.BeginInvoke(new System.Action(ResizeCurrentPage),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void ResizeCurrentPage()
    {
        if (PageHost.Content is HomePage hp)
            hp.HandleResize(ActualWidth);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (sender is not RadioButton rb) return;

        var tag = rb.Name switch
        {
            nameof(NavHome) => "home",
            nameof(NavStrategies) => "strategies",
            nameof(NavTesting) => "testing",
            nameof(NavLogs) => "logs",
            nameof(NavSettings) => "settings",
            _ => "home"
        };

        NavigateTo(tag);
    }

    private void NavigateTo(string page)
    {
        if (!_pageCache.TryGetValue(page, out var target))
        {
            target = page switch
            {
                "home" => new HomePage(),
                "strategies" => new StrategiesPage(),
                "testing" => new TestingPage(),
                "logs" => new LogsPage(),
                "settings" => new SettingsPage(),
                _ => new HomePage()
            };
            _pageCache[page] = target;
        }

        if (target is HomePage hp)
            hp.RefreshFromSettings();

        PageHost.Content = target;
    }

    public void RefreshStatus()
    {
        var runner = App.Runner;
        if (runner is null) return;

        if (runner.IsRunning)
        {
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            StatusText.Text = "Работает";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            var running = Models.StrategyCatalog.FindById(SettingsService.Current.SelectedStrategyId);
            ProfileText.Text = running?.Name ?? "—";
        }
        else
        {
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
            StatusText.Text = "Остановлено";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            ProfileText.Text = "Без обхода";
        }
    }
}
