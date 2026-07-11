using System.ComponentModel;
using System.Windows;
using MisterGPhotos.App.ViewModels;

namespace MisterGPhotos.App;

public partial class MainWindow : Window
{
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Clean shutdown: we first stop the scan and upload (files in progress
    /// revert to 'paused' in SQLite) before letting the window close.
    /// An extra impatient click on the close button during shutdown is ignored:
    /// only the Closing re-triggered by Application.Shutdown() is allowed through.
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
