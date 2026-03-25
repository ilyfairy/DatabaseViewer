using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace DatabaseViewer.App.Views;

public partial class MainWindow : Window
{
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<string> _pendingSqlFiles = [];
    private bool _webAppNavigationCompleted;
    private bool _closeConfirmed;
    private bool _closeCheckInProgress;
    private TaskCompletionSource<string>? _closeRequestCompletionSource;

    public MainWindow(string baseUrl)
    {
        _baseUrl = baseUrl;
        InitializeComponent();
        AllowDrop = true;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewDragEnter += OnPreviewDragOver;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await CreateWebViewEnvironmentAsync();
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.AllowDrop = false;
            Browser.PreviewDragEnter += OnPreviewDragOver;
            Browser.PreviewDragOver += OnPreviewDragOver;
            Browser.PreviewDrop += OnPreviewDrop;
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
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

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (TryGetDroppedFiles(e.Data).Count > 0)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnPreviewDrop(object sender, DragEventArgs e)
    {
        var droppedFiles = TryGetDroppedFiles(e.Data);
        if (droppedFiles.Count == 0)
        {
            return;
        }

        var files = droppedFiles.Where(path => string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Length > 0)
        {
            await OpenSqlFilesInAppAsync(files);
        }

        e.Handled = true;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeConfirmed || _closeCheckInProgress)
        {
            return;
        }

        e.Cancel = true;
        _closeCheckInProgress = true;
        try
        {
            if (Browser.CoreWebView2 is null || !_webAppNavigationCompleted)
            {
                _closeConfirmed = true;
                Close();
                return;
            }

            var hasDirtySqlTabs = await ExecuteWebBooleanAsync("window.__dbvHasDirtySqlTabs ? window.__dbvHasDirtySqlTabs() : false");
            if (!hasDirtySqlTabs)
            {
                _closeConfirmed = true;
                Close();
                return;
            }

            var result = await RequestCloseConfirmationFromWebAsync();
            if (string.Equals(result, "cancel", StringComparison.Ordinal))
            {
                return;
            }

            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closeCheckInProgress = false;
        }
    }

    private async void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var filePath = TryGetFilePathFromUri(e.Uri);
        if (filePath is null)
        {
            return;
        }

        e.Cancel = true;
        if (string.Equals(Path.GetExtension(filePath), ".sql", StringComparison.OrdinalIgnoreCase))
        {
            await OpenSqlFilesInAppAsync([filePath]);
        }
    }

    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var filePath = TryGetFilePathFromUri(e.Uri);
        if (filePath is null)
        {
            return;
        }

        e.Handled = true;
        if (string.Equals(Path.GetExtension(filePath), ".sql", StringComparison.OrdinalIgnoreCase))
        {
            await OpenSqlFilesInAppAsync([filePath]);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || Browser.CoreWebView2 is null)
        {
            return;
        }

        _webAppNavigationCompleted = true;
        if (_pendingSqlFiles.Count == 0)
        {
            return;
        }

        var pending = _pendingSqlFiles.ToArray();
        _pendingSqlFiles.Clear();
        _ = OpenSqlFilesInAppAsync(pending);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        HostRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<HostRequest>(e.WebMessageAsJson, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (request is null || !string.Equals(request.Channel, "dbv-request", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(request.Id))
        {
            return;
        }

        if (string.Equals(request.Command, "close-app-response", StringComparison.Ordinal))
        {
            var payload = request.Payload.Deserialize<CloseAppResponsePayload>(JsonOptions);
            _closeRequestCompletionSource?.TrySetResult(payload?.Result ?? "cancel");
            return;
        }

        if (!string.Equals(request.Command, "save-sql-file", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var payload = request.Payload.Deserialize<SaveSqlFilePayload>(JsonOptions)
                ?? throw new InvalidOperationException("无效的保存请求。") ;

            var targetPath = payload.FilePath;
            if (payload.SaveAs || string.IsNullOrWhiteSpace(targetPath))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "SQL 文件 (*.sql)|*.sql|所有文件 (*.*)|*.*",
                    FileName = payload.SuggestedFileName,
                    AddExtension = true,
                    DefaultExt = ".sql",
                    OverwritePrompt = true,
                };

                var accepted = dialog.ShowDialog(this) == true;
                if (!accepted)
                {
                    PostHostResponse(new HostResponse(request.Id, true, new { canceled = true, filePath = targetPath }, null));
                    return;
                }

                targetPath = dialog.FileName;
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                PostHostResponse(new HostResponse(request.Id, true, new { canceled = true, filePath = (string?)null }, null));
                return;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(targetPath, payload.Content ?? string.Empty);
            PostHostResponse(new HostResponse(request.Id, true, new { canceled = false, filePath = targetPath }, null));
        }
        catch (Exception ex)
        {
            PostHostResponse(new HostResponse(request.Id, false, null, ex.Message));
        }
    }

    private void PostHostResponse(HostResponse response)
    {
        Browser.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(response, JsonOptions));
    }

    private async Task OpenSqlFilesInAppAsync(IReadOnlyList<string> files)
    {
        var sqlFiles = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sqlFiles.Length == 0)
        {
            return;
        }

        if (Browser.CoreWebView2 is null || !_webAppNavigationCompleted)
        {
            foreach (var file in sqlFiles)
            {
                if (!_pendingSqlFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    _pendingSqlFiles.Add(file);
                }
            }
            return;
        }

        var payload = new HostEventPayload(
            "dbv-event",
            "open-sql-files",
            new
            {
                files = await Task.WhenAll(sqlFiles.Select(async path => new
                {
                    path,
                    content = await File.ReadAllTextAsync(path),
                })),
            });

        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private async Task<bool> ExecuteWebBooleanAsync(string script)
    {
        if (Browser.CoreWebView2 is null)
        {
            return false;
        }

        var json = await Browser.CoreWebView2.ExecuteScriptAsync(script);
        try
        {
            return JsonSerializer.Deserialize<bool>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string> RequestCloseConfirmationFromWebAsync()
    {
        if (Browser.CoreWebView2 is null)
        {
            return "cancel";
        }

        _closeRequestCompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var payload = new HostEventPayload(
            "dbv-event",
            "request-close-with-dirty-sql-tabs",
            new { });

        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
        var completed = await Task.WhenAny(_closeRequestCompletionSource.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        if (completed != _closeRequestCompletionSource.Task)
        {
            _closeRequestCompletionSource = null;
            return "cancel";
        }

        var result = await _closeRequestCompletionSource.Task;
        _closeRequestCompletionSource = null;
        return result;
    }

    private static string? TryGetFilePathFromUri(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText) || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.IsFile)
        {
            return null;
        }

        return uri.LocalPath;
    }

    private static string? TryGetSqlFilePathFromUri(string? uriText)
    {
        var localPath = TryGetFilePathFromUri(uriText);
        return string.Equals(Path.GetExtension(localPath), ".sql", StringComparison.OrdinalIgnoreCase)
            ? localPath
            : null;
    }

    private static IReadOnlyList<string> TryGetDroppedFiles(IDataObject dataObject)
    {
        if (!dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            return Array.Empty<string>();
        }

        var files = dataObject.GetData(DataFormats.FileDrop) as string[];
        if (files is null)
        {
            return Array.Empty<string>();
        }

        return files;
    }

    private static IReadOnlyList<string> TryGetDroppedSqlFiles(IDataObject dataObject)
    {
        var files = TryGetDroppedFiles(dataObject);
        return files.Where(path => string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private sealed record HostRequest(string Channel, string Id, string Command, JsonElement Payload);

    private sealed record HostResponse(string Id, bool Success, object? Payload, string? Error)
    {
        public string Channel { get; init; } = "dbv-response";
    }

    private sealed record HostEventPayload(string Channel, string Event, object Payload);

    private sealed record SaveSqlFilePayload(string? FilePath, string SuggestedFileName, string? Content, bool SaveAs);

    private sealed record CloseAppResponsePayload(string Result);
}