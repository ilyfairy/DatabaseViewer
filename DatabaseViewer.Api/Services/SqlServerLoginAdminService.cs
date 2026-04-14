using DatabaseViewer.Api.Contracts;
using DatabaseViewer.Core.Models;
using DatabaseViewer.Core.Services;

namespace DatabaseViewer.Api.Services;

/// <summary>
/// Maps SQL Server login management requests between API DTOs and Core services.
/// </summary>
public sealed class SqlServerLoginAdminService
{
    private readonly ConnectionStore _connectionStore;
    private readonly SqlServerLoginManagementService _loginManagementService;

    public SqlServerLoginAdminService(ConnectionStore connectionStore, SqlServerLoginManagementService loginManagementService)
    {
        _connectionStore = connectionStore;
        _loginManagementService = loginManagementService;
    }

    public async Task<SqlServerLoginListResponse> GetLoginsAsync(Guid connectionId)
    {
        var connection = await ResolveConnectionAsync(connectionId);
        var logins = await _loginManagementService.GetLoginsAsync(connection);
        return new SqlServerLoginListResponse(logins.Select(ToSummaryDto).ToArray());
    }

    public async Task<SqlServerLoginEditorOptionsResponse> GetEditorOptionsAsync(Guid connectionId)
    {
        var connection = await ResolveConnectionAsync(connectionId);
        var options = await _loginManagementService.GetEditorOptionsAsync(connection);
        return ToEditorOptionsDto(options);
    }

    public async Task<SqlServerLoginDetailResponse> GetLoginDetailAsync(Guid connectionId, string loginName)
    {
        var connection = await ResolveConnectionAsync(connectionId);
        var options = await _loginManagementService.GetEditorOptionsAsync(connection);
        var detail = await _loginManagementService.GetLoginDetailAsync(connection, loginName)
            ?? throw new InvalidOperationException($"未找到登录 {loginName}。");

        return new SqlServerLoginDetailResponse(
            detail.OriginalName,
            detail.Name,
            detail.PrincipalId,
            detail.PrincipalTypeCode,
            detail.PrincipalTypeLabel,
            detail.AuthenticationType,
            detail.IsSystem,
            detail.CanDelete,
            detail.SupportsGeneralEditing,
            detail.SupportsPasswordEditing,
            detail.SupportsRoleEditing,
            detail.SupportsPermissionEditing,
            detail.SupportsRename,
            detail.SupportsEnableDisable,
            detail.SupportsPasswordPolicy,
            detail.DefaultDatabase,
            detail.DefaultLanguage,
            detail.IsEnabled,
            detail.CheckPasswordPolicy,
            detail.CheckPasswordExpiration,
            detail.ServerRoles.ToArray(),
            detail.ServerPermissions.Select(ToPermissionDto).ToArray(),
            detail.EndpointPermissions.Select(ToEndpointPermissionDto).ToArray(),
            detail.LoginPermissions.Select(ToLoginPermissionDto).ToArray(),
            ToEditorOptionsDto(options));
    }

    public async Task<SqlServerLoginSqlPreviewResponse> PreviewSaveSqlAsync(SaveSqlServerLoginRequest request)
    {
        var connection = await ResolveConnectionAsync(request.ConnectionId);
        var sql = await _loginManagementService.PreviewSaveSqlAsync(connection, ToSaveDefinition(request));
        return new SqlServerLoginSqlPreviewResponse(sql);
    }

    public async Task SaveAsync(SaveSqlServerLoginRequest request)
    {
        var connection = await ResolveConnectionAsync(request.ConnectionId);
        await _loginManagementService.SaveLoginAsync(connection, ToSaveDefinition(request));
    }

    public async Task DeleteAsync(Guid connectionId, string loginName)
    {
        var connection = await ResolveConnectionAsync(connectionId);
        await _loginManagementService.DeleteLoginAsync(connection, loginName);
    }

    private async Task<ConnectionDefinition> ResolveConnectionAsync(Guid connectionId)
    {
        var connection = (await _connectionStore.LoadAsync()).FirstOrDefault(item => item.Id == connectionId)
            ?? throw new InvalidOperationException($"未找到连接 {connectionId}。");

        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            throw new InvalidOperationException("当前连接不是 SQL Server，暂不支持用户管理。");
        }

        return connection;
    }

    private static SqlServerLoginSaveDefinition ToSaveDefinition(SaveSqlServerLoginRequest request)
    {
        return new SqlServerLoginSaveDefinition
        {
            OriginalName = request.OriginalName,
            Name = request.Name,
            AuthenticationType = request.AuthenticationType,
            Password = request.Password,
            OldPassword = request.OldPassword,
            UseOldPassword = request.UseOldPassword,
            IsEnabled = request.IsEnabled,
            CheckPasswordPolicy = request.CheckPasswordPolicy,
            CheckPasswordExpiration = request.CheckPasswordExpiration,
            MustChangePasswordOnNextLogin = request.MustChangePasswordOnNextLogin,
            DefaultDatabase = request.DefaultDatabase,
            DefaultLanguage = request.DefaultLanguage,
            ServerRoles = request.ServerRoles.ToArray(),
            ServerPermissions = request.ServerPermissions.Select(ToPermissionInfo).ToArray(),
            EndpointPermissions = request.EndpointPermissions.Select(item => new SqlServerEndpointPermissionInfo
            {
                EndpointName = item.EndpointName,
                Permissions = item.Permissions.Select(ToPermissionInfo).ToArray(),
            }).ToArray(),
            LoginPermissions = request.LoginPermissions.Select(item => new SqlServerLoginPermissionTargetInfo
            {
                LoginName = item.LoginName,
                PrincipalTypeCode = item.PrincipalTypeCode,
                PrincipalTypeLabel = item.PrincipalTypeLabel,
                IsSystem = item.IsSystem,
                Permissions = item.Permissions.Select(ToPermissionInfo).ToArray(),
            }).ToArray(),
        };
    }

    private static SqlServerLoginSummaryDto ToSummaryDto(SqlServerLoginSummaryInfo info)
    {
        return new SqlServerLoginSummaryDto(
            info.Name,
            info.PrincipalId,
            info.DefaultDatabase,
            info.IsEnabled,
            info.PrincipalTypeCode,
            info.PrincipalTypeLabel,
            info.AuthenticationType,
            info.IsSystem,
            info.SupportsGeneralEditing,
            info.SupportsPasswordEditing,
            info.SupportsRoleEditing,
            info.SupportsPermissionEditing,
            info.CanDelete);
    }

    private static SqlServerLoginEditorOptionsResponse ToEditorOptionsDto(SqlServerLoginEditorOptionsInfo info)
    {
        return new SqlServerLoginEditorOptionsResponse(
            info.Databases.ToArray(),
            info.Languages.Select(item => new SqlServerSelectOptionDto(item.Value, item.Label)).ToArray(),
            info.ServerRoles.ToArray(),
            info.ServerPermissions.ToArray(),
            info.EndpointPermissions.ToArray(),
            info.LoginPermissions.ToArray(),
            info.Endpoints.ToArray(),
            info.LoginTargets.Select(ToSummaryDto).ToArray());
    }

    private static SqlServerPermissionAssignmentDto ToPermissionDto(SqlServerPermissionAssignmentInfo info)
    {
        return new SqlServerPermissionAssignmentDto(info.Name, info.State switch
        {
            SqlServerPermissionState.Grant => "grant",
            SqlServerPermissionState.GrantWithGrantOption => "grant-with-grant-option",
            SqlServerPermissionState.Deny => "deny",
            _ => "none",
        });
    }

    private static SqlServerEndpointPermissionDto ToEndpointPermissionDto(SqlServerEndpointPermissionInfo info)
    {
        return new SqlServerEndpointPermissionDto(info.EndpointName, info.Permissions.Select(ToPermissionDto).ToArray());
    }

    private static SqlServerLoginPermissionTargetDto ToLoginPermissionDto(SqlServerLoginPermissionTargetInfo info)
    {
        return new SqlServerLoginPermissionTargetDto(
            info.LoginName,
            info.PrincipalTypeCode,
            info.PrincipalTypeLabel,
            info.IsSystem,
            info.Permissions.Select(ToPermissionDto).ToArray());
    }

    private static SqlServerPermissionAssignmentInfo ToPermissionInfo(SqlServerPermissionAssignmentDto dto)
    {
        return new SqlServerPermissionAssignmentInfo
        {
            Name = dto.Name,
            State = dto.State switch
            {
                "grant" => SqlServerPermissionState.Grant,
                "grant-with-grant-option" => SqlServerPermissionState.GrantWithGrantOption,
                "deny" => SqlServerPermissionState.Deny,
                _ => SqlServerPermissionState.None,
            },
        };
    }
}