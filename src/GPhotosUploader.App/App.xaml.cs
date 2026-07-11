using System.IO;
using System.Net.Http;
using System.Windows;
using GPhotosUploader.App.ViewModels;
using GPhotosUploader.Core.Data;
using GPhotosUploader.Core.Services;

namespace GPhotosUploader.App;

/// <summary>Racine de composition : instancie la base, les dépôts et les services.</summary>
public partial class App : Application
{
    private Logger? _logger;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Instance unique : deux processus se disputeraient la même base SQLite et
        // RecoverAfterRestart requalifierait les fichiers en cours d'upload de l'autre.
        _singleInstanceMutex = new Mutex(true, @"Global\GooglePhotosLocalUploader", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Google Photos Local Uploader est déjà en cours d'exécution.",
                "Google Photos Local Uploader", MessageBoxButton.OK, MessageBoxImage.Information);
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
            _logger?.Error("App", $"Erreur non gérée : {args.Exception.Message}");
            MessageBox.Show(
                $"Une erreur inattendue est survenue :\n{args.Exception.Message}\n\nConsultez le journal pour plus de détails.",
                "Google Photos Local Uploader", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        window.Show();
        _logger.Info("App", "Application démarrée.");
    }
}
