using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Models;
using GPhotosUploader.Core.Services;
using Microsoft.Win32;

namespace GPhotosUploader.App.ViewModels;

/// <summary>Ligne affichée dans l'onglet « Détails des fichiers ».</summary>
public class FileRow
{
    public string FileName { get; init; } = "";
    public string LocalPath { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string ErrorText { get; init; } = "";
    public string UploadedText { get; init; } = "";
}

/// <summary>Ligne affichée dans l'onglet « Historique ».</summary>
public class BatchRow
{
    public long Id { get; init; }
    public string CreatedText { get; init; } = "";
    public string CompletedText { get; init; } = "";
    public int FileCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public string Status { get; init; } = "";
}

public partial class MainViewModel : ObservableObject
{
    public const string DuplicateDisclaimer =
        "Google Photos ne permet pas à cette application de vérifier toute votre bibliothèque. " +
        "La détection des doublons est garantie uniquement pour les fichiers déjà indexés localement " +
        "ou uploadés par cette application.";

    private readonly SettingsRepository _settingsRepo;
    private readonly MediaFileRepository _files;
    private readonly BatchRepository _batches;
    private readonly GoogleAuthService _auth;
    private readonly FileScanner _scanner;
    private readonly UploadService _upload;
    private readonly Logger _log;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;

    private AppSettings _settings;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _connectCts;
    private DateTime _lastFileProgressUi = DateTime.MinValue;

    public MainViewModel(SettingsRepository settingsRepo, MediaFileRepository files,
        BatchRepository batches, GoogleAuthService auth, FileScanner scanner,
        UploadService upload, Logger log)
    {
        _settingsRepo = settingsRepo;
        _files = files;
        _batches = batches;
        _auth = auth;
        _scanner = scanner;
        _upload = upload;
        _log = log;
        _dispatcher = Application.Current.Dispatcher;

        _settings = _settingsRepo.Load();
        RootFolder = _settings.RootFolder;
        BatchSize = _settings.BatchSize;
        MaxRetries = _settings.MaxRetries;
        Concurrency = _settings.Concurrency;
        MaxFileSizeMb = _settings.MaxFileSizeMb;
        IncludedExtensions = _settings.IncludedExtensions;
        OAuthClientId = _settings.OAuthClientId;

        BrowseFolderCommand = new RelayCommand(BrowseFolder, () => !IsBusy);
        ConnectCommand = new AsyncRelayCommand<object?>(ConnectAsync, _ => !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && RootFolder.Length > 0);
        StartCommand = new RelayCommand(StartUpload, () => !IsBusy && IsConnected);
        PauseCommand = new RelayCommand(_upload.Pause, () => ServiceState == UploadServiceState.Running);
        ResumeCommand = new RelayCommand(_upload.Resume, () => ServiceState == UploadServiceState.Paused);
        StopCommand = new AsyncRelayCommand(StopAsync,
            () => IsScanning || ServiceState is UploadServiceState.Running or UploadServiceState.Paused);
        SaveSettingsCommand = new RelayCommand(SaveSettings, () => !IsBusy);
        ExportLogCommand = new RelayCommand(ExportLog);
        RefreshDetailsCommand = new RelayCommand(RefreshDetails);
        RefreshHistoryCommand = new RelayCommand(RefreshHistory);
        ResetFailedCommand = new RelayCommand(ResetFailed, () => !IsBusy);
        DeleteDataCommand = new AsyncRelayCommand(DeleteLocalDataAsync, () => !IsBusy);
        OpenOAuthWizardCommand = new RelayCommand(OpenOAuthWizard, () => !IsBusy);

        _upload.StateChanged += state => RunOnUi(() =>
        {
            ServiceState = state;
            UpdateStateText();
            RaiseCommandStates();
        });
        _upload.CountersChanged += () => RunOnUi(RefreshCounters);
        _upload.FileProgressChanged += OnFileProgress;
        _upload.RunCompleted += message => RunOnUi(() =>
        {
            CurrentFileName = "";
            CurrentFileProgress = 0;
            CurrentFileText = "";
            StatusMessage = message;
            RefreshCounters();
            RefreshHistory();
        });
        _upload.AuthenticationLost += () => RunOnUi(() =>
        {
            RefreshAccountStatus();
            StatusMessage = "Session Google expirée : reconnectez votre compte puis relancez l'upload.";
        });
        _log.MessageLogged += (level, line) => RunOnUi(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > 500) LogLines.RemoveAt(0);
            if (level == AppLogLevel.Error) LastError = line;
        });

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => UpdateThroughput();
        _refreshTimer.Start();

        RefreshAccountStatus();
        RefreshCounters();
        RefreshDetails();
        RefreshHistory();
        UpdateStateText();
    }

    // ---- Propriétés observables ----

    [ObservableProperty] private string _rootFolder = "";
    [ObservableProperty] private string _accountStatusText = "Aucun compte connecté";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private UploadServiceState _serviceState = UploadServiceState.Idle;
    [ObservableProperty] private string _stateText = "Prêt";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private int _totalFiles;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _uploadedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private double _globalProgress;
    [ObservableProperty] private string _globalProgressText = "0 %";

    [ObservableProperty] private string _currentFileName = "";
    [ObservableProperty] private double _currentFileProgress;
    [ObservableProperty] private string _currentFileText = "";
    [ObservableProperty] private string _throughputText = "";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private string _lastError = "";

    [ObservableProperty] private int _batchSize;
    [ObservableProperty] private int _maxRetries;
    [ObservableProperty] private int _concurrency;
    [ObservableProperty] private int _maxFileSizeMb;
    [ObservableProperty] private string _includedExtensions = "";
    [ObservableProperty] private string _oAuthClientId = "";

    [ObservableProperty] private string _selectedStatusFilter = "Tous";

    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<FileRow> FileRows { get; } = new();
    public ObservableCollection<BatchRow> BatchRows { get; } = new();
    public string[] StatusFilters { get; } =
    {
        "Tous", "En attente", "Uploadés", "Erreurs", "Ignorés (doublon local)",
        "Ignorés (déjà uploadé)", "Ignorés (incompatible)"
    };

    public string DuplicateDisclaimerText => DuplicateDisclaimer;

    private bool IsBusy => IsScanning || IsConnecting || ServiceState != UploadServiceState.Idle;

    // ---- Commandes ----

    public RelayCommand BrowseFolderCommand { get; }
    public AsyncRelayCommand<object?> ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ExportLogCommand { get; }
    public RelayCommand RefreshDetailsCommand { get; }
    public RelayCommand RefreshHistoryCommand { get; }
    public RelayCommand ResetFailedCommand { get; }
    public AsyncRelayCommand DeleteDataCommand { get; }
    public RelayCommand OpenOAuthWizardCommand { get; }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choisir le dossier racine contenant vos photos" };
        if (!string.IsNullOrEmpty(RootFolder) && Directory.Exists(RootFolder))
            dialog.InitialDirectory = RootFolder;
        if (dialog.ShowDialog() == true)
        {
            RootFolder = dialog.FolderName;
            SaveSettings();
            _log.Info("Config", $"Dossier racine sélectionné : {RootFolder}");
        }
        RaiseCommandStates();
    }

    private async Task ConnectAsync(object? parameter)
    {
        SaveSettings();
        var clientId = OAuthClientId.Trim();
        var secret = ((parameter as PasswordBox)?.Password ?? "").Trim();
        // Champ vide : réutiliser le secret déjà enregistré dans le Gestionnaire
        // d'identifiants Windows (assistant terminé ou connexion précédente) —
        // uniquement s'il appartient bien au Client ID courant.
        if (string.IsNullOrWhiteSpace(secret) && OAuthClientConfig.IsValidClientId(clientId))
            secret = CredentialStore.ReadClientSecret(clientId) ?? "";

        if (!OAuthClientConfig.IsValidClientId(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            var open = MessageBox.Show(
                "Le Client ID est manquant ou invalide (il doit se terminer par " +
                $"« {OAuthClientConfig.ClientIdSuffix} »), ou le Client Secret est manquant.\n\n" +
                "Voulez-vous ouvrir l'assistant de configuration pas à pas ?",
                "Connexion Google", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (open == MessageBoxResult.Yes)
                OpenOAuthWizard();
            return;
        }
        try
        {
            _connectCts = new CancellationTokenSource();
            IsConnecting = true;
            RaiseCommandStates();
            StatusMessage = "Autorisation en cours dans votre navigateur...";
            var account = await _auth.SignInAsync(clientId, secret, _connectCts.Token);
            StatusMessage = $"Compte connecté : {account.Email}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Connexion annulée.";
        }
        catch (AuthRequiredException ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "Connexion Google", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _log.Error("Auth", $"Échec de la connexion Google : {ex.Message}");
            MessageBox.Show($"Échec de la connexion : {ex.Message}", "Connexion Google",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _connectCts = null;
            IsConnecting = false;
            RefreshAccountStatus();
            RaiseCommandStates();
        }
    }

    private void OpenOAuthWizard()
    {
        var wizardVm = new OAuthWizardViewModel(OAuthClientId);
        var window = new Views.OAuthWizardWindow(wizardVm) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true && wizardVm.Result is { } credentials)
        {
            OAuthClientId = credentials.ClientId;
            SaveSettings();
            CredentialStore.SaveClientSecret(credentials.ClientId, credentials.ClientSecret);
            _log.Info("Config", "Identifiants OAuth enregistrés via l'assistant de configuration.");
            StatusMessage = "Identifiants OAuth enregistrés. Cliquez sur « Connecter mon compte Google ».";
        }
    }

    private async Task DisconnectAsync()
    {
        var confirm = MessageBox.Show(
            "Déconnecter le compte Google ?\nLe refresh token sera révoqué et supprimé du Gestionnaire d'identifiants Windows.",
            "Déconnexion", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        _connectCts?.Cancel();
        await _auth.SignOutAsync();
        RefreshAccountStatus();
        RaiseCommandStates();
    }

    private async Task ScanAsync()
    {
        if (!Directory.Exists(RootFolder))
        {
            MessageBox.Show("Le dossier racine n'existe pas ou n'est pas accessible.",
                "Scan", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SaveSettings();
        IsScanning = true;
        RaiseCommandStates();
        StatusMessage = "Scan en cours...";
        UpdateStateText();
        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            StatusMessage = $"Scan en cours : {p.FilesSeen} fichiers examinés...";
        });
        try
        {
            // Instantané des paramètres : le scan ne doit pas voir les éditions à chaud.
            var result = await _scanner.ScanAsync(RootFolder, _settings.Clone(), progress, _scanCts.Token);
            StatusMessage =
                $"Scan terminé : {result.TotalSeen} images vues, {result.NewFiles} nouvelles, " +
                $"{result.Duplicates} doublons, {result.Incompatible} incompatibles.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan interrompu. Relancez-le pour compléter l'inventaire.";
        }
        catch (Exception ex)
        {
            _log.Error("Scan", $"Échec du scan : {ex.Message}");
            StatusMessage = $"Échec du scan : {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts = null;
            RefreshCounters();
            RefreshDetails();
            UpdateStateText();
            RaiseCommandStates();
        }
    }

    private void StartUpload()
    {
        SaveSettings();
        // Instantané des paramètres : le run d'upload est immuable, les éditions
        // faites ensuite dans l'onglet Paramètres s'appliqueront au prochain run.
        if (_upload.Start(_settings.Clone()))
            StatusMessage = "Upload démarré.";
        RaiseCommandStates();
    }

    private async Task StopAsync()
    {
        _scanCts?.Cancel();
        await _upload.StopAsync();
        RaiseCommandStates();
    }

    private void SaveSettings()
    {
        _settings.RootFolder = RootFolder;
        _settings.BatchSize = BatchSize;
        _settings.MaxRetries = MaxRetries;
        _settings.Concurrency = Concurrency;
        _settings.MaxFileSizeMb = MaxFileSizeMb;
        _settings.IncludedExtensions = IncludedExtensions;
        _settings.OAuthClientId = OAuthClientId.Trim();
        _settings.Clamp();
        _settingsRepo.Save(_settings);

        // Refléter les valeurs éventuellement bornées.
        BatchSize = _settings.BatchSize;
        MaxRetries = _settings.MaxRetries;
        Concurrency = _settings.Concurrency;
        MaxFileSizeMb = _settings.MaxFileSizeMb;
        IncludedExtensions = _settings.IncludedExtensions;
        StatusMessage = "Paramètres enregistrés.";
    }

    private void ExportLog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exporter le journal",
            FileName = $"google-photos-uploader-journal-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = "Fichier texte (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _log.ExportTo(dialog.FileName);
            StatusMessage = $"Journal exporté vers {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export impossible : {ex.Message}", "Export du journal",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetFailed()
    {
        var count = _files.ResetFailed();
        StatusMessage = $"{count} fichier(s) en erreur remis en file d'attente.";
        RefreshCounters();
        RefreshDetails();
    }

    private async Task DeleteLocalDataAsync()
    {
        var confirm = MessageBox.Show(
            "Supprimer toutes les données locales de l'application ?\n\n" +
            "Cela efface : l'inventaire SQLite, les journaux, les paramètres et les secrets " +
            "du Gestionnaire d'identifiants Windows.\n\n" +
            "Vos photos locales et vos médias Google Photos ne sont PAS touchés.\n" +
            "L'application se fermera ensuite.",
            "Supprimer les données locales", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _connectCts?.Cancel();
        await _upload.StopAsync();
        await _auth.SignOutAsync();
        // SignOutAsync conserve volontairement le Client Secret ; ici l'utilisateur
        // demande la suppression TOTALE des données locales : on l'efface aussi.
        CredentialStore.Delete(CredentialStore.ClientSecretTarget);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(AppPaths.DataDirectory))
                Directory.Delete(AppPaths.DataDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Certaines données n'ont pas pu être supprimées : {ex.Message}\n" +
                $"Vous pouvez supprimer manuellement le dossier :\n{AppPaths.DataDirectory}",
                "Suppression partielle", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Application.Current.Shutdown();
    }

    /// <summary>Appelé à la fermeture de la fenêtre : arrêt propre pour une reprise fiable.</summary>
    public async Task ShutdownAsync()
    {
        _refreshTimer.Stop();
        _scanCts?.Cancel();
        _connectCts?.Cancel();
        if (ServiceState != UploadServiceState.Idle)
            await _upload.StopAsync();
    }

    // ---- Rafraîchissements ----

    private void RefreshAccountStatus()
    {
        IsConnected = _auth.IsConnected;
        var account = _auth.CurrentAccount;
        AccountStatusText = IsConnected
            ? $"Connecté : {account?.Email ?? "(compte Google)"}"
            : "Aucun compte connecté";
    }

    private void RefreshCounters()
    {
        var counts = _files.CountByStatus();
        int Get(UploadStatus s) => counts.GetValueOrDefault(s);

        TotalFiles = counts.Values.Sum();
        UploadedCount = Get(UploadStatus.Uploaded);
        SkippedCount = Get(UploadStatus.SkippedDuplicateLocal)
                     + Get(UploadStatus.SkippedDuplicateRemoteAppCreated)
                     + Get(UploadStatus.SkippedIncompatible);
        ErrorCount = Get(UploadStatus.Failed);
        PendingCount = Get(UploadStatus.Queued) + Get(UploadStatus.Paused)
                     + Get(UploadStatus.Uploading) + Get(UploadStatus.Discovered);

        var done = UploadedCount + SkippedCount;
        GlobalProgress = TotalFiles > 0 ? done * 100.0 / TotalFiles : 0;
        GlobalProgressText = $"{GlobalProgress:F1} % ({UploadedCount} uploadés / {TotalFiles} fichiers)";
    }

    private void RefreshDetails()
    {
        UploadStatus? filter = SelectedStatusFilter switch
        {
            "En attente" => UploadStatus.Queued,
            "Uploadés" => UploadStatus.Uploaded,
            "Erreurs" => UploadStatus.Failed,
            "Ignorés (doublon local)" => UploadStatus.SkippedDuplicateLocal,
            "Ignorés (déjà uploadé)" => UploadStatus.SkippedDuplicateRemoteAppCreated,
            "Ignorés (incompatible)" => UploadStatus.SkippedIncompatible,
            _ => null
        };
        FileRows.Clear();
        foreach (var f in _files.List(filter, limit: 2000, offset: 0))
        {
            FileRows.Add(new FileRow
            {
                FileName = f.FileName,
                LocalPath = f.LocalPath,
                SizeText = FormatBytes(f.FileSize),
                StatusText = StatusFr(f.UploadStatus),
                ErrorText = f.LastError ?? "",
                UploadedText = f.UploadedAt?.ToLocalTime().ToString("g") ?? ""
            });
        }
    }

    partial void OnSelectedStatusFilterChanged(string value) => RefreshDetails();

    private void RefreshHistory()
    {
        BatchRows.Clear();
        foreach (var b in _batches.ListRecent(100))
        {
            BatchRows.Add(new BatchRow
            {
                Id = b.Id,
                CreatedText = b.CreatedAt.ToLocalTime().ToString("g"),
                CompletedText = b.CompletedAt?.ToLocalTime().ToString("g") ?? "—",
                FileCount = b.FileCount,
                SuccessCount = b.SuccessCount,
                FailureCount = b.FailureCount,
                Status = b.Status == "completed" ? "Terminé" : b.Status == "stopped" ? "Arrêté" : "En cours"
            });
        }
    }

    private void OnFileProgress(UploadFileProgress p)
    {
        // Limiter la fréquence des mises à jour UI (les événements arrivent tous les ~128 Ko).
        var now = DateTime.UtcNow;
        if ((now - _lastFileProgressUi).TotalMilliseconds < 100 && p.BytesSent < p.TotalBytes) return;
        _lastFileProgressUi = now;
        RunOnUi(() =>
        {
            CurrentFileName = p.FileName;
            CurrentFileProgress = p.TotalBytes > 0 ? p.BytesSent * 100.0 / p.TotalBytes : 0;
            CurrentFileText = $"{p.FileName} — {FormatBytes(p.BytesSent)} / {FormatBytes(p.TotalBytes)}";
        });
    }

    private void UpdateThroughput()
    {
        if (ServiceState != UploadServiceState.Running)
        {
            ThroughputText = "";
            EtaText = "";
            return;
        }
        var rate = _upload.BytesPerSecond;
        ThroughputText = rate > 0 ? $"Débit : {FormatBytes((long)rate)}/s" : "";
        var eta = _upload.EstimateRemaining(_settings.MaxRetries);
        EtaText = eta is { } t ? $"Temps restant estimé : {FormatDuration(t)}" : "";
    }

    private void UpdateStateText()
    {
        StateText = IsScanning ? "Scan en cours" : ServiceState switch
        {
            UploadServiceState.Running => "Upload en cours",
            UploadServiceState.Paused => "Upload en pause",
            UploadServiceState.Stopping => "Arrêt en cours...",
            _ => "Prêt"
        };
    }

    private void RaiseCommandStates()
    {
        BrowseFolderCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        ScanCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        ResetFailedCommand.NotifyCanExecuteChanged();
        DeleteDataCommand.NotifyCanExecuteChanged();
        OpenOAuthWizardCommand.NotifyCanExecuteChanged();
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    public static string StatusFr(UploadStatus status) => status switch
    {
        UploadStatus.Discovered => "Détecté",
        UploadStatus.Queued => "En attente",
        UploadStatus.Uploading => "Upload en cours",
        UploadStatus.Uploaded => "Uploadé",
        UploadStatus.SkippedDuplicateLocal => "Ignoré (doublon local)",
        UploadStatus.SkippedDuplicateRemoteAppCreated => "Ignoré (déjà uploadé)",
        UploadStatus.SkippedIncompatible => "Ignoré (incompatible)",
        UploadStatus.Failed => "Erreur",
        UploadStatus.Paused => "En pause",
        _ => status.ToString()
    };

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} Go",
            >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):F1} Mo",
            >= 1024L => $"{bytes / 1024.0:F0} Ko",
            _ => $"{bytes} o"
        };
    }

    public static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes:D2} min";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} min {t.Seconds:D2} s";
        return $"{t.Seconds} s";
    }
}
