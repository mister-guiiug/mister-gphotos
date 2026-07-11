using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPhotosUploader.Core.Services;
using Microsoft.Win32;

namespace GPhotosUploader.App.ViewModels;

/// <summary>Une étape de l'assistant de configuration Google Cloud.</summary>
public class WizardStep
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string? LinkUrl { get; init; }
    public string LinkLabel { get; init; } = "Ouvrir cette étape dans le navigateur";
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
    public string ProgressText => $"Étape {CurrentIndex + 1} sur {Steps.Count} — {CurrentStep.Title}";
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
            Title = "Choisir le fichier client_secret_….json téléchargé",
            Filter = "Fichier client secret (client_secret*.json)|client_secret*.json|Fichiers JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*"
        };
        if (Directory.Exists(downloads))
            dialog.InitialDirectory = downloads;
        if (dialog.ShowDialog() != true) return;

        try
        {
            var creds = OAuthClientConfig.ParseClientSecretFile(dialog.FileName);
            ClientId = creds.ClientId;
            _importedSecret = creds.ClientSecret;
            ImportStatus = "Fichier importé : Client ID et Client Secret chargés. Vous pouvez cliquer sur « Terminer ».";
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
            ValidationMessage =
                $"Le Client ID est vide ou invalide : il doit se terminer par « {OAuthClientConfig.ClientIdSuffix} ». " +
                "Importez le fichier JSON ou recopiez la valeur depuis la console.";
            return;
        }
        if (!OAuthClientConfig.IsPlausibleClientSecret(secret))
        {
            ValidationMessage =
                "Le Client Secret est vide ou invalide. Importez le fichier JSON téléchargé, " +
                "ou collez la valeur affichée à la création du client (elle commence en général par « GOCSPX- »).";
            return;
        }

        Result = new OAuthClientCredentials(ClientId.Trim(), secret);
        CloseRequested?.Invoke(true);
    }

    private static IReadOnlyList<WizardStep> BuildSteps() => new List<WizardStep>
    {
        new()
        {
            Title = "Pourquoi cette étape ?",
            Body =
                "Google impose que chaque application accédant à Google Photos utilise son propre « client OAuth », " +
                "créé dans votre console Google Cloud (gratuit, une seule fois, environ 15 minutes).\n\n" +
                "• Aucun mot de passe Google ne sera jamais saisi dans cette application : la connexion se fera dans votre navigateur.\n" +
                "• Google ne permet pas d'automatiser entièrement cette création (aucune API publique, ni gcloud, ni Terraform, " +
                "ne peut créer un client « Application de bureau ») : cet assistant ouvre les bonnes pages et vous indique quoi cliquer.\n" +
                "• À la fin, vous importerez le fichier JSON téléchargé — ou collerez le Client ID et le Client Secret.\n\n" +
                "Astuce : gardez cette fenêtre ouverte à côté de votre navigateur."
        },
        new()
        {
            Title = "Créer un projet Google Cloud",
            Body =
                "1. Cliquez sur le bouton ci-dessous pour ouvrir la page de création de projet.\n" +
                "2. Connectez-vous avec votre compte Google si la console vous le demande.\n" +
                "3. Nom du projet : par exemple « Photos Uploader » (le nom est libre).\n" +
                "4. Cliquez sur « Créer », puis attendez la notification de fin de création.\n" +
                "5. Vérifiez, dans le bandeau en haut de la console, que ce nouveau projet est bien sélectionné.",
            LinkUrl = "https://console.cloud.google.com/projectcreate"
        },
        new()
        {
            Title = "Activer l'API Photos Library",
            Body =
                "1. Ouvrez la page de l'API « Photos Library API » avec le bouton ci-dessous.\n" +
                "2. Vérifiez que votre projet est sélectionné en haut de la page.\n" +
                "3. Cliquez sur « Activer ».\n\n" +
                "Si le bouton affiche « Gérer » au lieu d'« Activer », l'API est déjà active : passez à l'étape suivante.",
            LinkUrl = "https://console.cloud.google.com/apis/library/photoslibrary.googleapis.com"
        },
        new()
        {
            Title = "Configurer l'écran de consentement",
            Body =
                "1. Ouvrez l'écran de consentement OAuth avec le bouton ci-dessous.\n" +
                "2. Type d'utilisateurs : choisissez « Externes », puis « Créer ».\n" +
                "3. Renseignez le nom de l'application (ex. « Photos Uploader ») et votre adresse e-mail " +
                "(assistance utilisateur et contact développeur) ; laissez le reste vide et enregistrez jusqu'à la fin.\n" +
                "4. Dans la section « Utilisateurs test » (ou « Audience »), cliquez sur « Ajouter des utilisateurs » " +
                "et ajoutez votre propre adresse Google.\n\n" +
                "Important : tant que l'application Google Cloud reste en mode « Test », la connexion expire tous les 7 jours " +
                "(il suffit alors de se reconnecter). Vous pourrez plus tard cliquer sur « Publier l'application » pour lever " +
                "cette limite — voir docs/google-cloud-setup.md.",
            LinkUrl = "https://console.cloud.google.com/apis/credentials/consent"
        },
        new()
        {
            Title = "Créer le client OAuth « Application de bureau »",
            Body =
                "1. Ouvrez la page de création d'identifiants avec le bouton ci-dessous.\n" +
                "2. Type d'application : choisissez « Application de bureau ».\n" +
                "3. Nom : par exemple « Photos Uploader Desktop », puis cliquez sur « Créer ».\n" +
                "4. Une fenêtre affiche votre Client ID et votre Client Secret : cliquez sur « Télécharger le JSON » " +
                "(recommandé), ou copiez soigneusement les deux valeurs.\n\n" +
                "Le fichier téléchargé s'appelle « client_secret_….json » et se trouve en général dans votre dossier Téléchargements.",
            LinkUrl = "https://console.cloud.google.com/apis/credentials/oauthclient"
        },
        new()
        {
            Title = "Renseigner les identifiants",
            Body =
                "Importez le fichier JSON téléchargé à l'étape précédente (recommandé), ou collez les valeurs manuellement.\n\n" +
                "Le Client Secret sera stocké dans le Gestionnaire d'identifiants Windows, jamais en clair sur le disque.",
            IsFinal = true
        }
    };
}
