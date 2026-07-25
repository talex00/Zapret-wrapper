namespace ZapretWrapper.ViewModels;

public enum TestStatus
{
    Pending,
    Running,
    Success,
    Failure,
}

public class StrategyTestRow : ViewModelBase
{
    public string Name { get; set; } = "";
    public TestStatus Status { get; set; } = TestStatus.Pending;
    public int? Ping { get; set; }
    public double? Loss { get; set; }
    public double? Speed { get; set; }

    public string StatusText => Status switch
    {
        TestStatus.Pending => "Ожидание",
        TestStatus.Running => "Тестирование…",
        TestStatus.Success => "Успешно",
        TestStatus.Failure => "Ошибка",
        _ => ""
    };

    public string PingText => Ping.HasValue ? Ping.Value.ToString() : "—";
    public string LossText => Loss.HasValue ? Loss.Value.ToString("0.0") : "—";
    public string SpeedText => Speed.HasValue ? Speed.Value.ToString("0.0") : "—";
}
