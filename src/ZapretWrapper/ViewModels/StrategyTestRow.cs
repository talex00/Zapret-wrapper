namespace ZapretWrapper.ViewModels;

public enum TestStatus
{
    Pending,
    Running,
    Success,
    Failure,
}

/// <summary>
/// Строка таблицы результатов теста стратегии на главной странице. Раньше наследовалась от
/// ViewModelBase, но использовала простые auto-свойства — PropertyChanged никогда не вызывался,
/// и DataGrid на главной странице не обновлялся во время теста.
/// </summary>
public class StrategyTestRow : ViewModelBase
{
    private string _name = "";
    private TestStatus _status = TestStatus.Pending;
    private int? _ping;
    private double? _loss;
    private double? _speed;

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

    /// <summary>Средняя задержка успешных ответов, мс.</summary>
    public int? Ping
    {
        get => _ping;
        set
        {
            if (SetField(ref _ping, value))
                OnPropertyChanged(nameof(PingText));
        }
    }

    /// <summary>Доля успешных попыток, %. Название историческое, в UI отображается как «Успех».</summary>
    public double? Loss
    {
        get => _loss;
        set
        {
            if (SetField(ref _loss, value))
                OnPropertyChanged(nameof(LossText));
        }
    }

    /// <summary>Средняя задержка по всем успешным ответам, мс.</summary>
    public double? Speed
    {
        get => _speed;
        set
        {
            if (SetField(ref _speed, value))
                OnPropertyChanged(nameof(SpeedText));
        }
    }

    public string StatusText => Status switch
    {
        TestStatus.Running => "Тестирование…",
        TestStatus.Success => "Успешно",
        TestStatus.Failure => "Ошибка",
        _ => "Ожидание",
    };

    public string PingText => Ping is null ? "—" : $"{Ping} мс";
    public string LossText => Loss is null ? "—" : $"{Loss:0}%";
    public string SpeedText => Speed is null ? "—" : $"{Speed:0} мс";
}
