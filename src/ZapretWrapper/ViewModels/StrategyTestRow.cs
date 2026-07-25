using ZapretWrapper.Models;

namespace ZapretWrapper.ViewModels;

/// <summary>Строка таблицы результатов теста стратегии. Свойства уведомляют UI об изменениях,
/// чтобы DataGrid обновлялся в реальном времени.</summary>
public class StrategyTestRow : ViewModelBase
{
    private Strategy _strategy = null!;
    private TestStatus _status = TestStatus.Pending;
    private int _successCount;
    private int _totalCount;
    private double _avgLatencyMs;

    public Strategy Strategy
    {
        get => _strategy;
        set
        {
            if (SetField(ref _strategy, value))
                OnPropertyChanged(nameof(Name));
        }
    }

    public string Name => Strategy?.Name ?? "";

    public TestStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public int SuccessCount
    {
        get => _successCount;
        set
        {
            if (SetField(ref _successCount, value))
                OnPropertyChanged(nameof(SuccessRateText));
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        set
        {
            if (SetField(ref _totalCount, value))
                OnPropertyChanged(nameof(SuccessRateText));
        }
    }

    public double AvgLatencyMs
    {
        get => _avgLatencyMs;
        set
        {
            if (SetField(ref _avgLatencyMs, value))
                OnPropertyChanged(nameof(AvgLatencyText));
        }
    }

    public string StatusText => Status switch
    {
        TestStatus.Running => "Тестируется…",
        TestStatus.Success => "Успешно",
        TestStatus.Failed => "Неудача",
        _ => "Ожидание",
    };

    public string SuccessRateText => TotalCount == 0 ? "—" : $"{SuccessCount}/{TotalCount}";

    public string AvgLatencyText => TotalCount == 0 ? "—" : $"{AvgLatencyMs:0} мс";
}

public enum TestStatus
{
    Pending,
    Running,
    Success,
    Failed,
}
