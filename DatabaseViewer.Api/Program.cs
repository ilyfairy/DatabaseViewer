using DatabaseViewer.Api;

await using var runtime = await DesktopApiHost.StartAsync();
Console.WriteLine($"Listening on {runtime.ListenUrl}");
Console.WriteLine($"Local access URL: {runtime.BaseUrl}");
await runtime.App.WaitForShutdownAsync();
