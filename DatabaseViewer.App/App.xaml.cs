using System.Windows;
using DatabaseViewer.Api;
using DatabaseViewer.Core.Services;
using DatabaseViewer.App.Views;

namespace DatabaseViewer.App;

public partial class App : Application
{
    private ApiRuntime? _apiRuntime;

    protected override async void OnStartup(StartupEventArgs e)
    {
        var inspectionExitCode = await SqliteExtensionInspectionProcess.TryRunAsync(e.Args, Console.Out, Console.Error);
        if (inspectionExitCode.HasValue)
        {
            Shutdown(inspectionExitCode.Value);
            return;
        }

        Startup += async (_, _) =>
        {
            _apiRuntime = await ApiHost.StartAsync();
            var window = new MainWindow(_apiRuntime.FrontendUrl);
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