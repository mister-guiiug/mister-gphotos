using System.Windows;
using GPhotosUploader.App.ViewModels;

namespace GPhotosUploader.App.Views;

/// <summary>Fenêtre de l'assistant de configuration Google Cloud (modale).</summary>
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
