namespace ZapretWrapper.ViewModels;

public enum TestStatus
{
    Pending,
    Running,
    Success,
    Partial,
    Failure,
}

/// <summary>
/// Строка таблицы результатов. Все свойства уведомляют об изменении — иначе DataGrid
/// не перерисовывался бы по ходу теста (именно из-за этого таблица раньше оставалась пустой).
/// </summary>
public class StrategyTestRow : ViewModelBase
{
    private string _name = "";
    private TestStatus _status = TestStatus.Pending;
    private int? _ping;
    private double? _successRate;
    private string _details = "";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public TestStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Средняя задержка успешных проб, мс.</summary>
    public int? Ping
    {
        get => _ping;
        set
        {
            if (SetField(ref _ping, value))
                OnPropertyChanged(nameof(PingText));
        }
    }

    /// <summary>Доля пройденных критичных проверок, %.</summary>
    public double? SuccessRate
    {
        get => _successRate;
        set
        {
            if (SetField(ref _successRate, value))
                OnPropertyChanged(nameof(SuccessText));
        }
    }

    /// <summary>Что именно не прошло, либо текущая выполняемая проба.</summary>
    public string Details
    {
        get => _details;
        set => SetField(ref _details, value);
    }

    public string StatusText => Status switch
    {
        TestStatus.Running => "Тестирование…",
        TestStatus.Success => "Работает",
        TestStatus.Partial => "Частично",
        TestStatus.Failure => "Не работает",
        _ => "Ожидание",
    };

    public string PingText => Ping is null ? "—" : $"{Ping} мс";
    public string SuccessText => SuccessRate is null ? "—" : $"{SuccessRate:0}%";

    public void Reset()
    {
        Status = TestStatus.Pending;
        Ping = null;
        SuccessRate = null;
        Details = "";
    }
}
