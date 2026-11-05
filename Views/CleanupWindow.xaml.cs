using System.Windows;
using TidyYard.ViewModels;

namespace TidyYard.Views;

/// <summary>Window plumbing only. Behaviour is in CleanupViewModel.</summary>
public partial class CleanupWindow : Window
{
    public CleanupWindow(CleanupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        GlassWindow.Attach(this);
    }
}
