using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MisterGPhotos.Core.Data;
using MisterGPhotos.Core.Models;
using MisterGPhotos.Core.Resources;
using MisterGPhotos.Core.Services;
using Microsoft.Win32;

namespace MisterGPhotos.App.ViewModels;

/// <summary>Row displayed in the "File details" tab.</summary>
public class FileRow
{
    public string FileName { get; init; } = "";
    public string LocalPath { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string ErrorText { get; init; } = "";
    public string UploadedText { get; init; } = "";
}

/// <summary>Row displayed in the "History" tab.</summary>
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

/// <summary>Filter option for the Details view: translated label + targeted status (null = all).</summary>
public class FilterOption
{
    public string Label { get; init; } = "";
    public UploadStatus? Status { get; init; }
    public override string ToString() => Label;
}

public partial class MainViewModel : ObservableObject
{
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

        FilterOptions = BuildFilterOptions();
        _selectedFilter = FilterOptions[0];

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
            StatusMessage = Loc.T("Status_AuthLost");
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

    // ---- Observable properties ----

    [ObservableProperty] private string _rootFolder = "";
    [ObservableProperty] private string _accountStatusText = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private UploadServiceState _serviceState = UploadServiceState.Idle;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private int _totalFiles;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _uploadedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private double _globalProgress;
    [ObservableProperty] private string _globalProgressText = "";

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

    [ObservableProperty] private FilterOption _selectedFilter;

    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<FileRow> FileRows { get; } = new();
    public ObservableCollection<BatchRow> BatchRows { get; } = new();
    public IReadOnlyList<FilterOption> FilterOptions { get; }

    private bool IsBusy => IsScanning || IsConnecting || ServiceState != UploadServiceState.Idle;

    // ---- Commands ----

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

    private static IReadOnlyList<FilterOption> BuildFilterOptions() => new List<FilterOption>
    {
        new() { Label = Loc.T("Filter_All"), Status = null },
        new() { Label = Loc.T("Filter_Pending"), Status = UploadStatus.Queued },
        new() { Label = Loc.T("Filter_Uploaded"), Status = UploadStatus.Uploaded },
        new() { Label = Loc.T("Filter_Errors"), Status = UploadStatus.Failed },
        new() { Label = Loc.T("Filter_SkippedLocal"), Status = UploadStatus.SkippedDuplicateLocal },
        new() { Label = Loc.T("Filter_SkippedRemote"), Status = UploadStatus.SkippedDuplicateRemoteAppCreated },
        new() { Label = Loc.T("Filter_SkippedIncompatible"), Status = UploadStatus.SkippedIncompatible },
    };

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Dialog_ChooseFolder_Title") };
        if (!string.IsNullOrEmpty(RootFolder) && Directory.Exists(RootFolder))
            dialog.InitialDirectory = RootFolder;
        if (dialog.ShowDialog() == true)
        {
            RootFolder = dialog.FolderName;
            SaveSettings();
            _log.Info("Config", Loc.TF("Log_RootSelected", RootFolder));
        }
        RaiseCommandStates();
    }

    private async Task ConnectAsync(object? parameter)
    {
        SaveSettings();
        var clientId = OAuthClientId.Trim();
        var secret = ((parameter as PasswordBox)?.Password ?? "").Trim();
        // Empty field: reuse the secret already stored in the Windows
        // Credential Manager (completed wizard or previous connection) —
        // only if it actually belongs to the current Client ID.
        if (string.IsNullOrWhiteSpace(secret) && OAuthClientConfig.IsValidClientId(clientId))
            secret = CredentialStore.ReadClientSecret(clientId) ?? "";

        if (!OAuthClientConfig.IsValidClientId(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            var open = MessageBox.Show(
                Loc.TF("Msg_MissingCreds_Text", OAuthClientConfig.ClientIdSuffix),
                Loc.T("Msg_ConnectGoogle_Caption"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (open == MessageBoxResult.Yes)
                OpenOAuthWizard();
            return;
        }
        try
        {
            _connectCts = new CancellationTokenSource();
            IsConnecting = true;
            RaiseCommandStates();
            StatusMessage = Loc.T("Status_Authorizing");
            var account = await _auth.SignInAsync(clientId, secret, _connectCts.Token);
            StatusMessage = Loc.TF("Status_ConnectedAccount", account.Email);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("Status_ConnectionCancelled");
        }
        catch (AuthRequiredException ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, Loc.T("Msg_ConnectGoogle_Caption"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _log.Error("Auth", Loc.TF("Log_ConnectFailed", ex.Message));
            MessageBox.Show(Loc.TF("Msg_ConnectFailed_Text", ex.Message), Loc.T("Msg_ConnectGoogle_Caption"),
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
            _log.Info("Config", Loc.T("Log_CredsSavedWizard"));
            StatusMessage = Loc.T("Status_CredsSaved");
        }
    }

    private async Task DisconnectAsync()
    {
        var confirm = MessageBox.Show(
            Loc.T("Msg_Disconnect_Text"),
            Loc.T("Msg_Disconnect_Caption"), MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            MessageBox.Show(Loc.T("Msg_RootMissing_Text"),
                Loc.T("Msg_Scan_Caption"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SaveSettings();
        IsScanning = true;
        RaiseCommandStates();
        StatusMessage = Loc.T("Status_ScanRunning");
        UpdateStateText();
        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            StatusMessage = Loc.TF("Status_ScanProgress", p.FilesSeen);
        });
        try
        {
            // Settings snapshot: the scan must not see hot edits.
            var result = await _scanner.ScanAsync(RootFolder, _settings.Clone(), progress, _scanCts.Token);
            StatusMessage = Loc.TF("Status_ScanDone",
                result.TotalSeen, result.NewFiles, result.Duplicates, result.Incompatible);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("Status_ScanCancelled");
        }
        catch (Exception ex)
        {
            _log.Error("Scan", Loc.TF("Log_ScanFailed", ex.Message));
            StatusMessage = Loc.TF("Status_ScanFailed", ex.Message);
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
        // Settings snapshot: the upload run is immutable; edits made
        // afterwards in the Settings tab will apply to the next run.
        if (_upload.Start(_settings.Clone()))
            StatusMessage = Loc.T("Status_UploadStarted");
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

        // Reflect the possibly clamped values.
        BatchSize = _settings.BatchSize;
        MaxRetries = _settings.MaxRetries;
        Concurrency = _settings.Concurrency;
        MaxFileSizeMb = _settings.MaxFileSizeMb;
        IncludedExtensions = _settings.IncludedExtensions;
        StatusMessage = Loc.T("Status_SettingsSaved");
    }

    private void ExportLog()
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Dialog_ExportLog_Title"),
            FileName = $"mister-gphotos-log-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = Loc.T("Dialog_ExportLog_Filter")
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _log.ExportTo(dialog.FileName);
            StatusMessage = Loc.TF("Status_LogExported", dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.TF("Msg_ExportFailed_Text", ex.Message), Loc.T("Msg_ExportLog_Caption"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetFailed()
    {
        var count = _files.ResetFailed();
        StatusMessage = Loc.TF("Status_ResetFailed", count);
        RefreshCounters();
        RefreshDetails();
    }

    private async Task DeleteLocalDataAsync()
    {
        var confirm = MessageBox.Show(
            Loc.T("Msg_DeleteData_Text"),
            Loc.T("Msg_DeleteData_Caption"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _connectCts?.Cancel();
        await _upload.StopAsync();
        await _auth.SignOutAsync();
        // SignOutAsync intentionally keeps the Client Secret; here the user
        // requests the TOTAL deletion of local data: we erase it as well.
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
                Loc.TF("Msg_DeletePartial_Text", ex.Message, AppPaths.DataDirectory),
                Loc.T("Msg_DeletePartial_Caption"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Application.Current.Shutdown();
    }

    /// <summary>Called when the window closes: clean shutdown for a reliable resume.</summary>
    public async Task ShutdownAsync()
    {
        _refreshTimer.Stop();
        _scanCts?.Cancel();
        _connectCts?.Cancel();
        if (ServiceState != UploadServiceState.Idle)
            await _upload.StopAsync();
    }

    // ---- Refreshes ----

    private void RefreshAccountStatus()
    {
        IsConnected = _auth.IsConnected;
        var account = _auth.CurrentAccount;
        AccountStatusText = IsConnected
            ? Loc.TF("Account_Connected", account?.Email ?? Loc.T("Account_Generic"))
            : Loc.T("Account_None");
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
        GlobalProgressText = Loc.TF("Progress_GlobalText", GlobalProgress, UploadedCount, TotalFiles);
    }

    private void RefreshDetails()
    {
        var filter = SelectedFilter?.Status;
        FileRows.Clear();
        foreach (var f in _files.List(filter, limit: 2000, offset: 0))
        {
            FileRows.Add(new FileRow
            {
                FileName = f.FileName,
                LocalPath = f.LocalPath,
                SizeText = FormatBytes(f.FileSize),
                StatusText = StatusDisplay(f.UploadStatus),
                ErrorText = f.LastError ?? "",
                UploadedText = f.UploadedAt?.ToLocalTime().ToString("g") ?? ""
            });
        }
    }

    partial void OnSelectedFilterChanged(FilterOption value) => RefreshDetails();

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
                Status = b.Status switch
                {
                    "completed" => Loc.T("BatchStatus_Completed"),
                    "stopped" => Loc.T("BatchStatus_Stopped"),
                    _ => Loc.T("BatchStatus_Running")
                }
            });
        }
    }

    private void OnFileProgress(UploadFileProgress p)
    {
        // Throttle the frequency of UI updates (events arrive roughly every ~128 KB).
        var now = DateTime.UtcNow;
        if ((now - _lastFileProgressUi).TotalMilliseconds < 100 && p.BytesSent < p.TotalBytes) return;
        _lastFileProgressUi = now;
        RunOnUi(() =>
        {
            CurrentFileName = p.FileName;
            CurrentFileProgress = p.TotalBytes > 0 ? p.BytesSent * 100.0 / p.TotalBytes : 0;
            CurrentFileText = Loc.TF("Progress_CurrentFile", p.FileName, FormatBytes(p.BytesSent), FormatBytes(p.TotalBytes));
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
        ThroughputText = rate > 0 ? Loc.TF("Progress_Throughput", FormatBytes((long)rate)) : "";
        var eta = _upload.EstimateRemaining(_settings.MaxRetries);
        EtaText = eta is { } t ? Loc.TF("Progress_Eta", FormatDuration(t)) : "";
    }

    private void UpdateStateText()
    {
        StateText = IsScanning ? Loc.T("State_Scanning") : ServiceState switch
        {
            UploadServiceState.Running => Loc.T("State_Uploading"),
            UploadServiceState.Paused => Loc.T("State_Paused"),
            UploadServiceState.Stopping => Loc.T("State_Stopping"),
            _ => Loc.T("State_Ready")
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

    public static string StatusDisplay(UploadStatus status) => status switch
    {
        UploadStatus.Discovered => Loc.T("Status_Discovered"),
        UploadStatus.Queued => Loc.T("Status_Queued"),
        UploadStatus.Uploading => Loc.T("Status_Uploading"),
        UploadStatus.Uploaded => Loc.T("Status_Uploaded"),
        UploadStatus.SkippedDuplicateLocal => Loc.T("Status_SkippedDuplicateLocal"),
        UploadStatus.SkippedDuplicateRemoteAppCreated => Loc.T("Status_SkippedDuplicateRemote"),
        UploadStatus.SkippedIncompatible => Loc.T("Status_SkippedIncompatible"),
        UploadStatus.Failed => Loc.T("Status_Failed"),
        UploadStatus.Paused => Loc.T("Status_Paused"),
        _ => status.ToString()
    };

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{(bytes / (1024.0 * 1024 * 1024)).ToString("F2", Loc.Culture)} {Loc.T("Unit_GB")}",
            >= 1024L * 1024 => $"{(bytes / (1024.0 * 1024)).ToString("F1", Loc.Culture)} {Loc.T("Unit_MB")}",
            >= 1024L => $"{(bytes / 1024.0).ToString("F0", Loc.Culture)} {Loc.T("Unit_KB")}",
            _ => $"{bytes} {Loc.T("Unit_B")}"
        };
    }

    public static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return Loc.TF("Duration_HoursMinutes", (int)t.TotalHours, t.Minutes);
        if (t.TotalMinutes >= 1) return Loc.TF("Duration_MinutesSeconds", t.Minutes, t.Seconds);
        return Loc.TF("Duration_Seconds", t.Seconds);
    }
}
