using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArxisStudio.Markup.Sample.ViewModels;

/// <summary>
/// Модель представления профиля пользователя.
/// </summary>
public partial class UserProfileViewModel : ViewModelBase
{
    [ObservableProperty] private bool _canSave =  true;

    /// <summary>
    /// Выполняет сохранение данных профиля.
    /// </summary>
    [RelayCommand]
    public void Save()
    {
        Console.WriteLine("Command Executed");
    }
}
