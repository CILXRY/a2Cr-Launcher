using CommunityToolkit.Mvvm.ComponentModel;

namespace a2Cr.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}