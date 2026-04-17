namespace DatabaseViewer.Core.Models;

/// <summary>
/// 数据库属性信息，包含常规信息、文件信息和权限信息。
/// </summary>
public sealed class DbDatabasePropertiesInfo
{
    /// <summary>常规属性（名称、状态、大小等）。</summary>
    public IReadOnlyList<DbCatalogObjectProperty> GeneralProperties { get; set; } = Array.Empty<DbCatalogObjectProperty>();

    /// <summary>数据库文件列表。</summary>
    public IReadOnlyList<DbDatabaseFileInfo> Files { get; set; } = Array.Empty<DbDatabaseFileInfo>();

    /// <summary>权限/用户列表。</summary>
    public IReadOnlyList<DbDatabasePermissionInfo> Permissions { get; set; } = Array.Empty<DbDatabasePermissionInfo>();
}

/// <summary>
/// 数据库文件信息。
/// </summary>
public sealed class DbDatabaseFileInfo
{
    public string LogicalName { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;

    public string? FileGroup { get; set; }

    public string SizeMB { get; set; } = string.Empty;

    public string? AutoGrowth { get; set; }

    public string? Path { get; set; }
}

/// <summary>
/// 数据库权限/用户信息。
/// </summary>
public sealed class DbDatabasePermissionInfo
{
    public string UserName { get; set; } = string.Empty;

    public string UserType { get; set; } = string.Empty;

    public string? DefaultSchema { get; set; }

    public string? LoginName { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}
