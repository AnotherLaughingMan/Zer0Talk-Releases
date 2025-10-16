using Avalonia.Controls;
using ZTalk.ViewModels;

namespace ZTalk.Views;

public partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();
        DataContext = new LoadingWindowViewModel();
    }

    public LoadingWindowViewModel ViewModel => (LoadingWindowViewModel)DataContext!;
}