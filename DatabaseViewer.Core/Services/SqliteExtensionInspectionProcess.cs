using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SQLitePCL;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public static class SqliteExtensionInspectionProcess
{
    private const string InspectCommand = "--sqlite-inspect-preopen-extensions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int?> TryRunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 2 || !string.Equals(args[0], InspectCommand, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var payloadBytes = Convert.FromBase64String(args[1]);
            var payload = JsonSerializer.Deserialize<SqliteExtensionInspectionPayload>(payloadBytes, JsonOptions)
                ?? throw new InvalidOperationException("无效的 SQLite 扩展探测请求。", null);
            var inspection = InspectInCurrentProcess(payload.Extensions);
            await stdout.WriteAsync(JsonSerializer.Serialize(inspection, JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            await stderr.WriteAsync(ex.Message);
            return 1;
        }
    }

    public static SqliteExtensionInspectionResult InspectPreOpenExtensions(IEnumerable<SqliteLoadableExtensionOptions> extensions)
    {
        var normalizedExtensions = extensions
            .Where(extension => extension.Phase == SqliteLoadableExtensionPhase.PreOpen)
            .Select(extension => new SqliteLoadableExtensionOptions
            {
                Path = extension.Path,
                EntryPoint = extension.EntryPoint,
                Phase = SqliteLoadableExtensionPhase.PreOpen,
            })
            .ToArray();

        if (normalizedExtensions.Length == 0)
        {
            return new SqliteExtensionInspectionResult([], [], string.Empty);
        }

        return SqliteExtensionInspectionCache.GetOrAdd(normalizedExtensions);
    }

    internal static SqliteExtensionInspectionResult InspectInCurrentProcess(IEnumerable<SqliteLoadableExtensionOptions> extensions)
    {
        Batteries_V2.Init();
        var baselineRegisteredVfsNames = SqliteLoadableExtensionRegistry.CaptureRegisteredVfsNames();
        foreach (var extension in extensions)
        {
            var extensionPath = SqliteLoadableExtensionRegistry.NormalizeExtensionPath(extension.Path);
            var extensionEntryPoint = SqliteLoadableExtensionRegistry.NormalizeEntryPoint(extension.EntryPoint);
            SqliteLoadableExtensionRegistry.LoadExtensionWithTemporaryDatabase(extensionPath, extensionEntryPoint);
        }

        var registeredVfsNames = SqliteLoadableExtensionRegistry.CaptureRegisteredVfsNames();
        var defaultVfsName = SqliteLoadableExtensionRegistry.CaptureDefaultVfsName();
        var providedVfsNames = registeredVfsNames
            .Except(baselineRegisteredVfsNames, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new SqliteExtensionInspectionResult(
            registeredVfsNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            providedVfsNames,
            defaultVfsName);
    }

    private sealed class SqliteExtensionInspectionCache
    {
        private static readonly Dictionary<string, SqliteExtensionInspectionResult> Cache = new(StringComparer.Ordinal);
        private static readonly object SyncRoot = new();

        public static SqliteExtensionInspectionResult GetOrAdd(IReadOnlyList<SqliteLoadableExtensionOptions> extensions)
        {
            var cacheKey = string.Join("|", extensions.Select(extension => $"{Path.GetFullPath(extension.Path.Trim())}>{extension.EntryPoint?.Trim() ?? string.Empty}"));
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(cacheKey, out var existing))
                {
                    return existing;
                }
            }

            var inspection = InspectViaChildProcess(extensions);
            lock (SyncRoot)
            {
                Cache[cacheKey] = inspection;
            }

            return inspection;
        }

        private static SqliteExtensionInspectionResult InspectViaChildProcess(IReadOnlyList<SqliteLoadableExtensionOptions> extensions)
        {
            var processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前进程路径，无法探测 SQLite 扩展。", null);
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            var isDotnetHost = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
            if (isDotnetHost && string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("无法确定当前入口程序集路径，无法探测 SQLite 扩展。", null);
            }

            var payload = new SqliteExtensionInspectionPayload(extensions.Select(extension => new SqliteLoadableExtensionOptions
            {
                Path = extension.Path,
                EntryPoint = extension.EntryPoint,
                Phase = SqliteLoadableExtensionPhase.PreOpen,
            }).ToArray());
            var payloadBase64 = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            if (isDotnetHost)
            {
                startInfo.ArgumentList.Add(entryAssemblyPath!);
            }

            startInfo.ArgumentList.Add(InspectCommand);
            startInfo.ArgumentList.Add(payloadBase64);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 SQLite 扩展探测子进程。", null);

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            var output = standardOutput.GetAwaiter().GetResult();
            var error = standardError.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "SQLite 扩展探测子进程执行失败。"
                    : $"SQLite 扩展探测失败: {error}", null);
            }

            return JsonSerializer.Deserialize<SqliteExtensionInspectionResult>(output, JsonOptions)
                ?? throw new InvalidOperationException("SQLite 扩展探测返回了无效结果。", null);
        }
    }
}

public sealed record SqliteExtensionInspectionResult(IReadOnlyList<string> RegisteredVfsNames, IReadOnlyList<string> ProvidedVfsNames, string DefaultVfsName);

internal sealed record SqliteExtensionInspectionPayload(IReadOnlyList<SqliteLoadableExtensionOptions> Extensions);