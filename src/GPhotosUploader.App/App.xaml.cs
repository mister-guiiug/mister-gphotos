using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using GPhotosUploader.App.ViewModels;
using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Resources;
using GPhotosUploader.Core.Services;

namespace GPhotosUploader.App;

/// <summary>Racine de composition : instancie la base, les dépôts et les services.</summary>
public partial class App : Application
{
    /// <summary>Nom de marque du produit (identique dans toutes les langues).</summary>
    public const string ProductName = "Google Photos Local Uploader";

    private Logger? _logger;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Langue de l'interface = langue d'affichage de l'OS. Fixée pour tous les
        // threads (les workers de scan/upload doivent produire les mêmes traductions).
        var uiCulture = CultureInfo.CurrentUICulture;
        Loc.Culture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;

        // Instance unique : deux processus se disputeraient la même base SQLite et
        // RecoverAfterRestart requalifierait les fichiers en cours d'upload de l'autre.
        _singleInstanceMutex = new Mutex(true, @"Global\GooglePhotosLocalUploader", out bool createdNew);
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

        // Reprise après fermeture ou crash : les fichiers restés 'uploading' sont remis en file.
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
