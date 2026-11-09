using System.Windows;
using DualSound.App.ViewModels;

namespace DualSound.App;

/// <summary>
/// Main application shell window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
