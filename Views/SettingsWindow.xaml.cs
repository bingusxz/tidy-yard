using System.Windows;
using TidyYard.ViewModels;

namespace TidyYard.Views;

/// <summary>Window plumbing only. Behaviour is in SettingsViewModel.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
