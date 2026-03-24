using DatabaseViewer.Api;

await using var runtime = await DesktopApiHost.StartAsync();
await runtime.App.WaitForShutdownAsync();
