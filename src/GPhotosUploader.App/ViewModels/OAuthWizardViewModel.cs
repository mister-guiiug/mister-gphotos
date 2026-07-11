using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPhotosUploader.Core.Resources;
using GPhotosUploader.Core.Services;
using Microsoft.Win32;

namespace GPhotosUploader.App.ViewModels;

/// <summary>Une étape de l'assistant de configuration Google Cloud.</summary>
public class WizardStep
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string? LinkUrl { get; init; }
    public string LinkLabel => Loc.T("Wizard_OpenLink");
    public bool IsFinal { get; init; }
    public bool HasLink => LinkUrl is not null;
}

/// <summary>
/// Assistant intégré de création du client OAuth Google Cloud.
/// Google n'expose aucune API (ni gcloud, ni Terraform) pour créer un client OAuth
/// « Application de bureau » : l'assistant guide donc l'utilisateur pas à pas dans la
/// console (liens directs vers chaque page), puis importe le fichier
/// client_secret_….json téléchargé — ou accepte les valeurs collées manuellement.
/// </summary>
public partial class OAuthWizardViewModel : ObservableObject
{
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private string _clientId;
    [ObservableProperty] private string _validationMessage = "";
    [ObservableProperty] private string _importStatus = "";

    private string _importedSecret = "";

    /// <summary>Identifiants validés, disponibles quand l'assistant se termine par « Terminer ».</summary>
    public OAuthClientCredentials? Result { get; private set; }

    /// <summary>Demande de fermeture de la fenêtre (true = terminé, false = annulé).</summary>
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<WizardStep> Steps { get; }

    public OAuthWizardViewModel(string currentClientId)
    {
        _clientId = currentClientId;
        Steps = BuildSteps();

        PreviousCommand = new RelayCommand(() => CurrentIndex--, () => CurrentIndex > 0);
        NextCommand = new RelayCommand(() => CurrentIndex++, () => CurrentIndex < Steps.Count - 1);
        OpenLinkCommand = new RelayCommand(OpenCurrentLink, () => CurrentStep.HasLink);
        ImportJsonCommand = new RelayCommand(ImportJson);
        FinishCommand = new RelayCommand<object?>(Finish);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
    }

    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand OpenLinkCommand { get; }
    public RelayCommand ImportJsonCommand { get; }
    public RelayCommand<object?> FinishCommand { get; }
    public RelayCommand CancelCommand { get; }

    public WizardStep CurrentStep => Steps[CurrentIndex];
    public string ProgressText => Loc.TF("Wizard_ProgressText", CurrentIndex + 1, Steps.Count, CurrentStep.Title);
    public int ProgressValue => CurrentIndex + 1;
    public int ProgressMaximum => Steps.Count;
    public bool IsFinalStep => CurrentStep.IsFinal;
    public bool IsNotFinalStep => !CurrentStep.IsFinal;

    partial void OnCurrentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(IsFinalStep));
        OnPropertyChanged(nameof(IsNotFinalStep));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        OpenLinkCommand.NotifyCanExecuteChanged();
    }

    private void OpenCurrentLink()
    {
        if (CurrentStep.LinkUrl is { } url)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void ImportJson()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Dialog_ImportJson_Title"),
            Filter = Loc.T("Dialog_ImportJson_Filter")
        };
        if (Directory.Exists(downloads))
            dialog.InitialDirectory = downloads;
        if (dialog.ShowDialog() != true) return;

        try
        {
            var creds = OAuthClientConfig.ParseClientSecretFile(dialog.FileName);
            ClientId = creds.ClientId;
            _importedSecret = creds.ClientSecret;
            ImportStatus = Loc.T("Wizard_ImportedStatus");
            ValidationMessage = "";
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            ImportStatus = "";
            ValidationMessage = ex.Message;
        }
    }

    private void Finish(object? parameter)
    {
        var typedSecret = (parameter as PasswordBox)?.Password ?? "";
        var secret = string.IsNullOrWhiteSpace(typedSecret) ? _importedSecret : typedSecret.Trim();

        if (!OAuthClientConfig.IsValidClientId(ClientId))
        {
            ValidationMessage = Loc.TF("Wizard_Validation_ClientId", OAuthClientConfig.ClientIdSuffix);
            return;
        }
        if (!OAuthClientConfig.IsPlausibleClientSecret(secret))
        {
            ValidationMessage = Loc.T("Wizard_Validation_ClientSecret");
            return;
        }

        Result = new OAuthClientCredentials(ClientId.Trim(), secret);
        CloseRequested?.Invoke(true);
    }

    private static IReadOnlyList<WizardStep> BuildSteps() => new List<WizardStep>
    {
        new() { Title = Loc.T("Wizard_Step1_Title"), Body = Loc.T("Wizard_Step1_Body") },
        new()
        {
            Title = Loc.T("Wizard_Step2_Title"), Body = Loc.T("Wizard_Step2_Body"),
            LinkUrl = "https://console.cloud.google.com/projectcreate"
        },
        new()
        {
            Title = Loc.T("Wizard_Step3_Title"), Body = Loc.T("Wizard_Step3_Body"),
            LinkUrl = "https://console.cloud.google.com/apis/library/photoslibrary.googleapis.com"
        },
        new()
        {
            Title = Loc.T("Wizard_Step4_Title"), Body = Loc.T("Wizard_Step4_Body"),
            LinkUrl = "https://console.cloud.google.com/apis/credentials/consent"
        },
        new()
        {
            Title = Loc.T("Wizard_Step5_Title"), Body = Loc.T("Wizard_Step5_Body"),
            LinkUrl = "https://console.cloud.google.com/apis/credentials/oauthclient"
        },
        new() { Title = Loc.T("Wizard_Step6_Title"), Body = Loc.T("Wizard_Step6_Body"), IsFinal = true },
    };
}
