using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace DatabaseViewer.App.Views;

public partial class MainWindow : Window
{
    private readonly string _baseUrl;

    public MainWindow(string baseUrl)
    {
        _baseUrl = baseUrl;
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await CreateWebViewEnvironmentAsync();
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.Source = new Uri(_baseUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "WebView2 runtime not found. Please install Microsoft Edge WebView2 Runtime, or publish the app with a bundled fixed runtime under the WebView2Runtime folder.",
                "Database Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Database Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<CoreWebView2Environment?> CreateWebViewEnvironmentAsync()
    {
        var browserExecutableFolder = FindBundledRuntimeFolder();
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DatabaseViewer",
            "WebView2Data");

        Directory.CreateDirectory(userDataFolder);

        if (browserExecutableFolder is null)
        {
            return null;
        }

        return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: browserExecutableFolder, userDataFolder: userDataFolder);
    }

    private static string? FindBundledRuntimeFolder()
    {
        var runtimeRoot = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
        if (!Directory.Exists(runtimeRoot))
        {
            return null;
        }

        if (File.Exists(Path.Combine(runtimeRoot, "msedgewebview2.exe")))
        {
            return runtimeRoot;
        }

        var nestedRuntime = Directory
            .EnumerateDirectories(runtimeRoot)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "msedgewebview2.exe")));

        return nestedRuntime;
    }

    private static bool IsRefreshGesture(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == Key.F5 || (key == Key.R && Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    private static bool IsResetZoomGesture(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (key == Key.D0 || key == Key.NumPad0);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F12)
        {
            if (Browser.CoreWebView2 is not null)
            {
                Browser.CoreWebView2.OpenDevToolsWindow();
                e.Handled = true;
            }

            return;
        }

        if (!IsRefreshGesture(e))
        {
            if (IsResetZoomGesture(e))
            {
                Browser.ZoomFactor = 1d;
                e.Handled = true;
            }

            return;
        }

        _ = Browser.CoreWebView2?.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('dbv-host-refresh'))");
        e.Handled = true;
    }
}