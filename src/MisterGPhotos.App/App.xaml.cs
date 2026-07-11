using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using MisterGPhotos.App.ViewModels;
using MisterGPhotos.Core.Data;
using MisterGPhotos.Core.Resources;
using MisterGPhotos.Core.Services;

namespace MisterGPhotos.App;

/// <summary>Composition root: instantiates the database, repositories and services.</summary>
public partial class App : Application
{
    /// <summary>Product brand name (identical across all languages).</summary>
    public const string ProductName = "Google Photos Local Uploader";

    private Logger? _logger;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // UI language = OS display language. Fixed for all
        // threads (the scan/upload workers must produce the same translations).
        var uiCulture = CultureInfo.CurrentUICulture;
        Loc.Culture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;

        // Single instance: two processes would fight over the same SQLite database and
        // RecoverAfterRestart would requalify the other's files currently being uploaded.
        _singleInstanceMutex = new Mutex(true, @"Global\MisterGPhotos", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                Loc.T("App_AlreadyRunning"),
                ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Directory.CreateDirectory(AppPaths.DataDirectory);
        var database = new Database(AppPaths.DatabasePath);
        var logRepo = new LogRepository(database);
        _logger = new Logger(logRepo);
        var mediaFiles = new MediaFileRepository(database);
        var accounts = new AccountRepository(database);
        var batches = new BatchRepository(database);
        var settings = new SettingsRepository(database);

        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var auth = new GoogleAuthService(http, accounts, _logger);
        var api = new GooglePhotosApi(http);
        var scanner = new FileScanner(mediaFiles, _logger);
        var upload = new UploadService(mediaFiles, batches, auth, api, _logger);

        // Resume after shutdown or crash: files left in 'uploading' state are re-queued.
        upload.RecoverAfterRestart();
        logRepo.Purge(keepDays: 90);

        var viewModel = new MainViewModel(settings, mediaFiles, batches, auth, scanner, upload, _logger);
        var window = new MainWindow { DataContext = viewModel };

        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Error("App", Loc.TF("Log_UnhandledError", args.Exception.Message));
            MessageBox.Show(
                Loc.TF("App_UnexpectedError", args.Exception.Message),
                ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        window.Show();
        _logger.Info("App", Loc.T("App_Started"));
    }
}
