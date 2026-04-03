using DatabaseViewer.Api;

var fixedUrl = "http://127.0.0.1:5027";
Console.WriteLine($"Starting API at {fixedUrl}");
await using var runtime = await DesktopApiHost.StartAsync(overrideBaseUrl: fixedUrl);
await runtime.App.WaitForShutdownAsync();
