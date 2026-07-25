using System.Collections.Generic;
using System.Windows.Controls;
using ZapretWrapper.ViewModels;

namespace ZapretWrapper.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = new List<StrategyTestRow>
        {
            new() { Name = "discord_auto",  Status = TestStatus.Success,  Ping = 45, Loss = 0.0, Speed = 85.4 },
            new() { Name = "youtube_qq",    Status = TestStatus.Success,  Ping = 52, Loss = 0.1, Speed = 92.1 },
            new() { Name = "general_tcp",   Status = TestStatus.Failure,  Ping = null, Loss = 100, Speed = 0.0 },
            new() { Name = "udp_fragment",  Status = TestStatus.Running,  Ping = 38, Loss = 0.0, Speed = null },
            new() { Name = "tls_fake_sni",  Status = TestStatus.Pending,  Ping = null, Loss = null, Speed = null },
        };
    }

    public void HandleResize(double windowWidth) { }
}
