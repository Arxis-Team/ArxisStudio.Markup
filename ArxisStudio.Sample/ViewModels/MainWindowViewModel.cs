namespace ArxisStudio.Markup.Sample.ViewModels;

/// <summary>
/// Модель представления главного окна демонстрационного приложения.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Приветственное сообщение для интерфейса.
    /// </summary>
    public string Greeting { get; } = "Welcome to Avalonia!";
}
