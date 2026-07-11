using System.Windows;
using GPhotosUploader.App.ViewModels;

namespace GPhotosUploader.App.Views;

/// <summary>Google Cloud setup wizard window (modal).</summary>
public partial class OAuthWizardWindow : Window
{
    public OAuthWizardWindow(OAuthWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += result =>
        {
            DialogResult = result;
            Close();
        };
    }
}
