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
        GlassWindow.Attach(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Shows one page of the sidebar (0 Appearance, 1 Calendar, 2 Plots, 3 Notepad, ...).</summary>
    public void ShowPage(int index) => Nav.SelectedIndex = index;
}
