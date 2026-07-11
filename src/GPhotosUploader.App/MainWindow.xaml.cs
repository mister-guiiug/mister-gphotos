using System.ComponentModel;
using System.Windows;
using GPhotosUploader.App.ViewModels;

namespace GPhotosUploader.App;

public partial class MainWindow : Window
{
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Fermeture propre : on arrête d'abord le scan et l'upload (les fichiers en cours
    /// repassent en 'paused' dans SQLite) avant de laisser la fenêtre se fermer.
    /// Un clic impatient supplémentaire sur la croix pendant l'arrêt est ignoré :
    /// seul le Closing re-déclenché par Application.Shutdown() est laissé passer.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_shutdownStarted)
        {
            if (!_shutdownCompleted) e.Cancel = true;
            return;
        }
        if (DataContext is not MainViewModel vm) return;

        e.Cancel = true;
        _shutdownStarted = true;
        try
        {
            await vm.ShutdownAsync();
        }
        finally
        {
            _shutdownCompleted = true;
            Application.Current.Shutdown();
        }
    }
}
