using System.Windows;
using System.Windows.Controls;
using ZapretWrapper.Styles;
using ZapretWrapper.Views;

namespace ZapretWrapper;

public partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        _initialized = true;

        // Системный title bar подстраиваем под активную тему.
        SourceInitialized += (_, _) => WindowChrome.Apply(this);
        Activated += (_, _) => WindowChrome.Apply(this);
        ThemeManager.ThemeChanged += (_, _) => WindowChrome.Apply(this);

        // При ресайзе окна форвардим размер в активную страницу (адаптивный layout).
        SizeChanged += (_, e) =>
        {
            if (PageHost.Content is HomePage hp)
                hp.HandleResize(e.NewSize.Width);
        };

        NavigateTo("home");
        // Принудительно дёрнем ресайз сразу после навигации.
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
        FrameworkElement target = page switch
        {
            "home" => new HomePage(),
            "strategies" => new StrategiesPage(),
            "testing" => new TestingPage(),
            "logs" => new LogsPage(),
            "settings" => new SettingsPage(),
            _ => new HomePage()
        };

        PageHost.Content = target;
    }
}
