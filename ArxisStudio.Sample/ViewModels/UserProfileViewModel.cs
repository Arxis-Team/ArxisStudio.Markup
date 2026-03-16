using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArxisStudio.Markup.Sample.ViewModels;

public partial class UserProfileViewModel : ViewModelBase
{
    [ObservableProperty] private bool _canSave =  true;

    [RelayCommand]
    public void Save()
    {
        Console.WriteLine("Command Executed");
    }
}