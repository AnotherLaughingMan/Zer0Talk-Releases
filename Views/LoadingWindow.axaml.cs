using Avalonia.Controls;
using Zer0Talk.ViewModels;

namespace Zer0Talk.Views;

public partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();
        DataContext = new LoadingWindowViewModel();
    }

    public LoadingWindowViewModel ViewModel => (LoadingWindowViewModel)DataContext!;
}