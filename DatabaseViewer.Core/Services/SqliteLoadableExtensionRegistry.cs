using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SQLitePCL;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

internal static class SqliteLoadableExtensionRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> LoadedExtensions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BaselineRegisteredVfsNames;
    private static readonly string BaselineDefaultVfsName;

    static SqliteLoadableExtensionRegistry()
    {
        Batteries_V2.Init();
        BaselineRegisteredVfsNames = CaptureRegisteredVfsNames();
        BaselineDefaultVfsName = CaptureDefaultVfsName();
    }

    public static string ResolvePinnedVfsName(SqliteConnectionOptions sqliteOptions)
    {
        ValidatePreOpenExtensionUsage(sqliteOptions);

        return sqliteOptions.Vfs.Kind switch
        {
            SqliteVfsKind.Default => BaselineDefaultVfsName,
            SqliteVfsKind.BuiltInOffset => string.Empty,
            SqliteVfsKind.Named => ResolveNamedVfsName(sqliteOptions),
            _ => BaselineDefaultVfsName,
        };
    }

    public static bool IsBaselineRegisteredVfs(string vfsName)
        => !string.IsNullOrWhiteSpace(vfsName) && BaselineRegisteredVfsNames.Contains(vfsName.Trim());

    public static void ValidateNamedVfsAvailability(SqliteConnectionOptions sqliteOptions)
    {
        if (sqliteOptions.Vfs.Kind != SqliteVfsKind.Named)
        {
            return;
        }

        var vfsName = sqliteOptions.Vfs.Named.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vfsName))
        {
            throw new InvalidOperationException("命名 SQLite VFS 需要填写已注册的 VFS 名称。", null);
        }

        var preOpenExtensions = sqliteOptions.Extensions.Where(extension => extension.Phase == SqliteLoadableExtensionPhase.PreOpen).ToArray();
        if (preOpenExtensions.Length == 0)
        {
            if (!IsBaselineRegisteredVfs(vfsName))
                {
                    throw new InvalidOperationException($"SQLite VFS '{vfsName}' 不属于应用启动时已存在的 VFS 集合；请改为在全局设置中配置对应的预打开扩展。", null);
                }

            return;
        }

        var inspection = SqliteExtensionInspectionProcess.InspectPreOpenExtensions(preOpenExtensions);
        if (!inspection.ProvidedVfsNames.Contains(vfsName, StringComparer.OrdinalIgnoreCase))
        {
                throw new InvalidOperationException($"全局设置中配置的预打开扩展不会注册 SQLite VFS '{vfsName}'。", null);
        }
    }

    public static void EnsurePreOpenExtensionsLoaded(IEnumerable<SqliteLoadableExtensionOptions> extensions)
    {
        foreach (var extension in extensions.Where(extension => extension.Phase == SqliteLoadableExtensionPhase.PreOpen))
        {
            var extensionPath = NormalizeExtensionPath(extension.Path);
            var extensionEntryPoint = NormalizeEntryPoint(extension.EntryPoint);
            var cacheKey = $"{extensionPath}|{extensionEntryPoint}";

            lock (SyncRoot)
            {
                if (LoadedExtensions.Contains(cacheKey))
                {
                    continue;
                }

                LoadExtensionWithTemporaryDatabase(extensionPath, extensionEntryPoint);
                LoadedExtensions.Add(cacheKey);
            }
        }
    }

    public static void LoadPostOpenExtensions(sqlite3 databaseHandle, IEnumerable<SqliteLoadableExtensionOptions> extensions)
    {
        foreach (var extension in extensions.Where(extension => extension.Phase == SqliteLoadableExtensionPhase.PostOpen))
        {
            LoadExtension(databaseHandle, NormalizeExtensionPath(extension.Path), NormalizeEntryPoint(extension.EntryPoint));
        }
    }

    public static void LoadPostOpenExtensions(Microsoft.Data.Sqlite.SqliteConnection connection, IEnumerable<SqliteLoadableExtensionOptions> extensions)
    {
        var postOpenExtensions = extensions.Where(extension => extension.Phase == SqliteLoadableExtensionPhase.PostOpen).ToArray();
        if (postOpenExtensions.Length == 0)
        {
            return;
        }

        connection.EnableExtensions(true);
        try
        {
            foreach (var extension in postOpenExtensions)
            {
                var extensionPath = NormalizeExtensionPath(extension.Path);
                var extensionEntryPoint = NormalizeEntryPoint(extension.EntryPoint);
                connection.LoadExtension(extensionPath, extensionEntryPoint);
            }
        }
        finally
        {
            connection.EnableExtensions(false);
        }
    }

    internal static string NormalizeExtensionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("SQLite 扩展路径不能为空。", null);
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"SQLite 扩展文件不存在: {fullPath}", null);
        }

        return fullPath;
    }

    internal static string NormalizeEntryPoint(string entryPoint)
        => string.IsNullOrWhiteSpace(entryPoint) ? string.Empty : entryPoint.Trim();

    public static void ValidatePreOpenExtensionUsage(SqliteConnectionOptions sqliteOptions)
    {
        _ = sqliteOptions;
    }

    private static string ResolveNamedVfsName(SqliteConnectionOptions sqliteOptions)
    {
        var vfsName = sqliteOptions.Vfs.Named.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vfsName))
        {
            throw new InvalidOperationException("命名 SQLite VFS 需要填写已注册的 VFS 名称。", null);
        }

        if (!SqliteVfsConnectionFactory.IsRegisteredVfs(vfsName))
        {
            throw new InvalidOperationException($"SQLite VFS '{vfsName}' 尚未在当前进程中注册。", null);
        }

        return vfsName;
    }

    internal static unsafe HashSet<string> CaptureRegisteredVfsNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = NativeSqliteVfsMethods.sqlite3_vfs_find(null);
        while (current != IntPtr.Zero)
        {
            var header = Unsafe.AsRef<NativeSqliteVfsHeader>((void*)current);
            var name = Marshal.PtrToStringUTF8(header.zName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }

            current = header.pNext;
        }

        return names;
    }

    internal static unsafe string CaptureDefaultVfsName()
    {
        var current = NativeSqliteVfsMethods.sqlite3_vfs_find(null);
        if (current == IntPtr.Zero)
        {
            return string.Empty;
        }

        var header = Unsafe.AsRef<NativeSqliteVfsHeader>((void*)current);
        return Marshal.PtrToStringUTF8(header.zName) ?? string.Empty;
    }

    internal static void LoadExtensionWithTemporaryDatabase(string extensionPath, string extensionEntryPoint)
    {
        var openResult = raw.sqlite3_open_v2(":memory:", out var databaseHandle, raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE, null);
        if (openResult != raw.SQLITE_OK)
        {
            throw new InvalidOperationException($"无法创建 SQLite 扩展加载上下文，SQLite 错误码: {openResult}", null);
        }

        try
        {
            LoadExtension(databaseHandle, extensionPath, extensionEntryPoint);
        }
        finally
        {
            raw.sqlite3_close_v2(databaseHandle);
        }
    }

    private static void LoadExtension(sqlite3 databaseHandle, string extensionPath, string extensionEntryPoint)
    {
        var enableResult = NativeSqliteExtensionMethods.sqlite3_enable_load_extension(databaseHandle, 1);
        if (enableResult != raw.SQLITE_OK)
        {
            throw new InvalidOperationException($"无法启用 SQLite 扩展加载，SQLite 错误码: {enableResult}", null);
        }

        var loadResult = NativeSqliteExtensionMethods.sqlite3_load_extension(
            databaseHandle,
            extensionPath,
            string.IsNullOrWhiteSpace(extensionEntryPoint) ? null : extensionEntryPoint,
            out var errorMessagePointer);

        if (loadResult != raw.SQLITE_OK)
        {
            var errorMessage = errorMessagePointer != IntPtr.Zero
                ? Marshal.PtrToStringUTF8(errorMessagePointer)
                : raw.sqlite3_errmsg(databaseHandle).utf8_to_string();
            if (errorMessagePointer != IntPtr.Zero)
            {
                NativeSqliteExtensionMethods.sqlite3_free(errorMessagePointer);
            }

            throw new InvalidOperationException($"加载 SQLite 扩展失败: {errorMessage}", null);
        }

        NativeSqliteExtensionMethods.sqlite3_enable_load_extension(databaseHandle, 0);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeSqliteVfsHeader
{
    public readonly int iVersion;
    public readonly int szOsFile;
    public readonly int mxPathname;
    public readonly IntPtr pNext;
    public readonly IntPtr zName;
    public readonly IntPtr pAppData;
}

internal static unsafe class NativeSqliteVfsMethods
{
    [DllImport("e_sqlcipher", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_vfs_find")]
    public static extern IntPtr sqlite3_vfs_find(byte* vfsName);
}

internal static class NativeSqliteExtensionMethods
{
    [DllImport("e_sqlcipher", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_enable_load_extension")]
    public static extern int sqlite3_enable_load_extension(sqlite3 databaseHandle, int onoff);

    [DllImport("e_sqlcipher", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_load_extension")]
    public static extern int sqlite3_load_extension(
        sqlite3 databaseHandle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? entryPoint,
        out IntPtr errorMessage);

    [DllImport("e_sqlcipher", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_free")]
    public static extern void sqlite3_free(IntPtr pointer);
}