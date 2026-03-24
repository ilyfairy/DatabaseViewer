using System.Windows;
using DatabaseViewer.Api;
using DatabaseViewer.App.Views;

namespace DatabaseViewer.App;

public partial class App : Application
{
    private DesktopApiRuntime? _apiRuntime;

    protected override void OnStartup(StartupEventArgs e)
    {
        Startup += async (_, _) =>
        {
            _apiRuntime = await DesktopApiHost.StartAsync();
            var window = new MainWindow(_apiRuntime.BaseUrl);
            MainWindow = window;
            window.Show();
        };

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_apiRuntime is not null)
        {
            await _apiRuntime.DisposeAsync();
        }

        base.OnExit(e);
    }
}