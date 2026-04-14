namespace DatabaseViewer.Core.Models;

public enum SqlServerPermissionState
{
    None,
    Grant,
    GrantWithGrantOption,
    Deny,
}

public sealed class SqlServerLoginSummaryInfo
{
    public string Name { get; set; } = string.Empty;

    public int PrincipalId { get; set; }

    public string? DefaultDatabase { get; set; }

    public bool IsEnabled { get; set; }

    public string PrincipalTypeCode { get; set; } = string.Empty;

    public string PrincipalTypeLabel { get; set; } = string.Empty;

    public string AuthenticationType { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public bool SupportsGeneralEditing { get; set; }

    public bool SupportsPasswordEditing { get; set; }

    public bool SupportsRoleEditing { get; set; }

    public bool SupportsPermissionEditing { get; set; }

    public bool CanDelete { get; set; }
}

public sealed class SqlServerPermissionAssignmentInfo
{
    public string Name { get; set; } = string.Empty;

    public SqlServerPermissionState State { get; set; }
}

public sealed class SqlServerEndpointPermissionInfo
{
    public string EndpointName { get; set; } = string.Empty;

    public IReadOnlyList<SqlServerPermissionAssignmentInfo> Permissions { get; set; } = Array.Empty<SqlServerPermissionAssignmentInfo>();
}

public sealed class SqlServerLoginPermissionTargetInfo
{
    public string LoginName { get; set; } = string.Empty;

    public string PrincipalTypeCode { get; set; } = string.Empty;

    public string PrincipalTypeLabel { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public IReadOnlyList<SqlServerPermissionAssignmentInfo> Permissions { get; set; } = Array.Empty<SqlServerPermissionAssignmentInfo>();
}

public sealed class SqlServerSelectOptionInfo
{
    public string Value { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? MatchName { get; set; }
}

public sealed class SqlServerLoginEditorOptionsInfo
{
    public IReadOnlyList<string> Databases { get; set; } = Array.Empty<string>();

    public IReadOnlyList<SqlServerSelectOptionInfo> Languages { get; set; } = Array.Empty<SqlServerSelectOptionInfo>();

    public IReadOnlyList<string> ServerRoles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ServerPermissions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> EndpointPermissions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> LoginPermissions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Endpoints { get; set; } = Array.Empty<string>();

    public IReadOnlyList<SqlServerLoginSummaryInfo> LoginTargets { get; set; } = Array.Empty<SqlServerLoginSummaryInfo>();
}

public sealed class SqlServerLoginDetailInfo
{
    public string OriginalName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int PrincipalId { get; set; }

    public string PrincipalTypeCode { get; set; } = string.Empty;

    public string PrincipalTypeLabel { get; set; } = string.Empty;

    public string AuthenticationType { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public bool CanDelete { get; set; }

    public bool SupportsGeneralEditing { get; set; }

    public bool SupportsPasswordEditing { get; set; }

    public bool SupportsRoleEditing { get; set; }

    public bool SupportsPermissionEditing { get; set; }

    public bool SupportsRename { get; set; }

    public bool SupportsEnableDisable { get; set; }

    public bool SupportsPasswordPolicy { get; set; }

    public string? DefaultDatabase { get; set; }

    public string? DefaultLanguage { get; set; }

    public bool IsEnabled { get; set; }

    public bool CheckPasswordPolicy { get; set; }

    public bool CheckPasswordExpiration { get; set; }

    public IReadOnlyList<string> ServerRoles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<SqlServerPermissionAssignmentInfo> ServerPermissions { get; set; } = Array.Empty<SqlServerPermissionAssignmentInfo>();

    public IReadOnlyList<SqlServerEndpointPermissionInfo> EndpointPermissions { get; set; } = Array.Empty<SqlServerEndpointPermissionInfo>();

    public IReadOnlyList<SqlServerLoginPermissionTargetInfo> LoginPermissions { get; set; } = Array.Empty<SqlServerLoginPermissionTargetInfo>();
}

public sealed class SqlServerLoginSaveDefinition
{
    public string? OriginalName { get; set; }

    public string Name { get; set; } = string.Empty;

    public string AuthenticationType { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string? OldPassword { get; set; }

    public bool UseOldPassword { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool CheckPasswordPolicy { get; set; } = true;

    public bool CheckPasswordExpiration { get; set; }

    public bool MustChangePasswordOnNextLogin { get; set; }

    public string? DefaultDatabase { get; set; }

    public string? DefaultLanguage { get; set; }

    public IReadOnlyList<string> ServerRoles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<SqlServerPermissionAssignmentInfo> ServerPermissions { get; set; } = Array.Empty<SqlServerPermissionAssignmentInfo>();

    public IReadOnlyList<SqlServerEndpointPermissionInfo> EndpointPermissions { get; set; } = Array.Empty<SqlServerEndpointPermissionInfo>();

    public IReadOnlyList<SqlServerLoginPermissionTargetInfo> LoginPermissions { get; set; } = Array.Empty<SqlServerLoginPermissionTargetInfo>();
}