using System.Windows;

namespace ZapretWrapper.Views;

/// <summary>
/// Точка входа для автоподбора с главной страницы. Вынесена в отдельный файл,
/// чтобы не смешивать публичный сценарий «нажали кнопку на главной» с логикой
/// самого прогона.
/// </summary>
public partial class TestingPage
{
    /// <summary>Запускает подбор, если прогон ещё не идёт. Повторный вызов безопасен.</summary>
    public void StartAutoPick()
    {
        if (_isRunning) return;
        TestToggle_Click(this, new RoutedEventArgs());
    }
}
