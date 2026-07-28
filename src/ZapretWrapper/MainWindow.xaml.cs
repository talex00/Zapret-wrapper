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
    private bool _advanced;
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
        RefreshStatus();
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

        // Страницы кешируются, поэтому изменённые настройки (путь к zapret, цели)
        // нужно подхватывать при каждом возврате на страницу.
        switch (target)
        {
            case HomePage hp:
                hp.RefreshFromSettings();
                break;
            case TestingPage tp:
                tp.RefreshFromSettings();
                break;
            case StrategiesPage sp:
                sp.Refresh();
                break;
        }

        PageHost.Content = target;

        // В простом режиме бокового меню нет, поэтому единственный путь назад —
        // кнопка в шапке. На главной она была бы бессмысленной.
        BackButton.Visibility = page == "home" ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Переключатель «простой / расширенный»: аналог раскрытия окна по мере надобности.</summary>
    private void AdvancedToggle_Click(object sender, RoutedEventArgs e) => SetAdvanced(!_advanced);

    private void SetAdvanced(bool on)
    {
        _advanced = on;

        Sidebar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = on ? new GridLength(220) : new GridLength(0);
        AdvancedButton.Content = on ? "Простой режим" : "Расширенный режим";

        if (on)
        {
            // Таблице тестирования и логам нужно место — окно растём только здесь.
            if (Width < 1180) Width = 1240;
            if (Height < 760) Height = 820;
            return;
        }

        NavHome.IsChecked = true;
        NavigateTo("home");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        NavHome.IsChecked = true;
        NavigateTo("home");
    }

    /// <summary>
    /// Автоподбор с главной страницы: переходим на «Тестирование» и сразу стартуем прогон,
    /// чтобы вторичный сценарий не требовал изучения меню.
    /// </summary>
    public void GoToAutoPick()
    {
        if (Width < 1080) Width = 1140;
        if (Height < 720) Height = 800;

        NavTesting.IsChecked = true;
        NavigateTo("testing");

        if (PageHost.Content is TestingPage tp)
            Dispatcher.BeginInvoke(new System.Action(tp.StartAutoPick),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// Может вызываться из событий раннера, поэтому сначала гарантируем UI-поток:
    /// обращение к элементам окна из пула потоков валило приложение.
    /// </summary>
    public void RefreshStatus()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new System.Action(RefreshStatus));
            return;
        }

        var runner = App.Runner;
        if (runner is null) return;

        if (runner.IsRunning)
        {
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            StatusText.Text = "Работает";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");

            var running = runner.CurrentStrategy
                ?? Models.StrategyCatalog.FindById(SettingsService.Current.SelectedStrategyId);
            ProfileText.Text = running is null ? "" : "· " + running.Name;
            ProfileText.Visibility = running is null ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
            StatusText.Text = "Остановлено";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");

            // Имя профиля в остановленном состоянии — лишний шум.
            ProfileText.Text = "";
            ProfileText.Visibility = Visibility.Collapsed;
        }

        if (PageHost.Content is HomePage hp) hp.RefreshFlow();
    }
}
