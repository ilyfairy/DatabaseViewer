using DatabaseViewer.Core.Services;
using DatabaseViewer.Api;

var inspectionExitCode = await SqliteExtensionInspectionProcess.TryRunAsync(args, Console.Out, Console.Error);
if (inspectionExitCode.HasValue)
{
	Environment.ExitCode = inspectionExitCode.Value;
	return;
}

await using var runtime = await ApiHost.StartAsync();
Console.WriteLine($"Listening on {runtime.ListenUrl}");
Console.WriteLine($"Local access URL: {runtime.BaseUrl}");
Console.WriteLine($"Frontend URL: {runtime.FrontendUrl}");
Console.WriteLine($"Using frontend dev server: {runtime.UsesFrontendDevServer}");
await runtime.App.WaitForShutdownAsync();
