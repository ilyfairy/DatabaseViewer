namespace DatabaseViewer.Api.Contracts;

public sealed record SqlServerLoginSummaryDto(
    string Name,
    int PrincipalId,
    string? DefaultDatabase,
    bool IsEnabled,
    string PrincipalTypeCode,
    string PrincipalTypeLabel,
    string AuthenticationType,
    bool IsSystem,
    bool SupportsGeneralEditing,
    bool SupportsPasswordEditing,
    bool SupportsRoleEditing,
    bool SupportsPermissionEditing,
    bool CanDelete);

public sealed record SqlServerPermissionAssignmentDto(string Name, string State);

public sealed record SqlServerEndpointPermissionDto(string EndpointName, IReadOnlyList<SqlServerPermissionAssignmentDto> Permissions);

public sealed record SqlServerLoginPermissionTargetDto(
    string LoginName,
    string PrincipalTypeCode,
    string PrincipalTypeLabel,
    bool IsSystem,
    IReadOnlyList<SqlServerPermissionAssignmentDto> Permissions);

public sealed record SqlServerSelectOptionDto(string Value, string Label);

public sealed record SqlServerLoginEditorOptionsResponse(
    IReadOnlyList<string> Databases,
    IReadOnlyList<SqlServerSelectOptionDto> Languages,
    IReadOnlyList<string> ServerRoles,
    IReadOnlyList<string> ServerPermissions,
    IReadOnlyList<string> EndpointPermissions,
    IReadOnlyList<string> LoginPermissions,
    IReadOnlyList<string> Endpoints,
    IReadOnlyList<SqlServerLoginSummaryDto> LoginTargets);

public sealed record SqlServerLoginListResponse(IReadOnlyList<SqlServerLoginSummaryDto> Logins);

public sealed record SqlServerLoginDetailResponse(
    string OriginalName,
    string Name,
    int PrincipalId,
    string PrincipalTypeCode,
    string PrincipalTypeLabel,
    string AuthenticationType,
    bool IsSystem,
    bool CanDelete,
    bool SupportsGeneralEditing,
    bool SupportsPasswordEditing,
    bool SupportsRoleEditing,
    bool SupportsPermissionEditing,
    bool SupportsRename,
    bool SupportsEnableDisable,
    bool SupportsPasswordPolicy,
    string? DefaultDatabase,
    string? DefaultLanguage,
    bool IsEnabled,
    bool CheckPasswordPolicy,
    bool CheckPasswordExpiration,
    IReadOnlyList<string> ServerRoles,
    IReadOnlyList<SqlServerPermissionAssignmentDto> ServerPermissions,
    IReadOnlyList<SqlServerEndpointPermissionDto> EndpointPermissions,
    IReadOnlyList<SqlServerLoginPermissionTargetDto> LoginPermissions,
    SqlServerLoginEditorOptionsResponse Options);

public sealed record SaveSqlServerLoginRequest(
    Guid ConnectionId,
    string? OriginalName,
    string Name,
    string AuthenticationType,
    string? Password,
    string? OldPassword,
    bool UseOldPassword,
    bool IsEnabled,
    bool CheckPasswordPolicy,
    bool CheckPasswordExpiration,
    bool MustChangePasswordOnNextLogin,
    string? DefaultDatabase,
    string? DefaultLanguage,
    IReadOnlyList<string> ServerRoles,
    IReadOnlyList<SqlServerPermissionAssignmentDto> ServerPermissions,
    IReadOnlyList<SqlServerEndpointPermissionDto> EndpointPermissions,
    IReadOnlyList<SqlServerLoginPermissionTargetDto> LoginPermissions);

public sealed record SqlServerLoginSqlPreviewResponse(string Sql);