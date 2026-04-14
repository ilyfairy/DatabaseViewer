using System.Data.Common;
using System.Text;
using Dapper;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

/// <summary>
/// Loads and updates SQL Server server-level login metadata without coupling it to the explorer object catalog flow.
/// </summary>
public sealed class SqlServerLoginManagementService
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<IReadOnlyList<SqlServerLoginSummaryInfo>> GetLoginsAsync(ConnectionDefinition connection)
    {
        EnsureSqlServer(connection);

        await using var db = DbConnectionFactory.Create(connection);
        await db.OpenAsync();

        var rows = (await db.QueryAsync<LoginSummaryRow>(@"
            SELECT
                sp.name AS Name,
                sp.principal_id AS PrincipalId,
                sp.default_database_name AS DefaultDatabase,
                sp.is_disabled AS IsDisabled,
                sp.type AS PrincipalTypeCode,
                sp.type_desc AS PrincipalTypeDescription
            FROM sys.server_principals sp
            WHERE sp.type IN ('S', 'U', 'G', 'C', 'K')
            ORDER BY sp.name;")).ToArray();

        return rows.Select(MapSummary).ToArray();
    }

    public async Task<SqlServerLoginEditorOptionsInfo> GetEditorOptionsAsync(ConnectionDefinition connection)
    {
        EnsureSqlServer(connection);

        await using var db = DbConnectionFactory.Create(connection);
        await db.OpenAsync();

        var databases = (await db.QueryAsync<string>(@"
            SELECT name
            FROM sys.databases
            ORDER BY name;"))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        var languageRows = await LoadLanguageOptionsAsync(db);

        var roles = (await db.QueryAsync<string>(@"
            SELECT name
            FROM sys.server_principals
            WHERE type = 'R'
              AND name <> 'public'
            ORDER BY name;"))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        var serverPermissions = await LoadPermissionNamesAsync(db, "SERVER");
        var endpointPermissions = await LoadPermissionNamesAsync(db, "ENDPOINT");
        var loginPermissions = await LoadPermissionNamesAsync(db, "LOGIN");

        var endpoints = (await db.QueryAsync<string>(@"
            SELECT name
            FROM sys.endpoints
            ORDER BY endpoint_id;"))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        var loginTargets = (await GetLoginsAsync(connection)).ToArray();

        return new SqlServerLoginEditorOptionsInfo
        {
            Databases = databases,
            Languages = languageRows,
            ServerRoles = roles,
            ServerPermissions = serverPermissions,
            EndpointPermissions = endpointPermissions,
            LoginPermissions = loginPermissions,
            Endpoints = endpoints,
            LoginTargets = loginTargets,
        };
    }

    public async Task<SqlServerLoginDetailInfo?> GetLoginDetailAsync(ConnectionDefinition connection, string loginName)
    {
        EnsureSqlServer(connection);

        await using var db = DbConnectionFactory.Create(connection);
        await db.OpenAsync();

        var login = await db.QuerySingleOrDefaultAsync<LoginDetailRow>(@"
            SELECT
                sp.name AS Name,
                sp.principal_id AS PrincipalId,
                sp.type AS PrincipalTypeCode,
                sp.type_desc AS PrincipalTypeDescription,
                sp.default_database_name AS DefaultDatabase,
                sp.default_language_name AS DefaultLanguage,
                sp.is_disabled AS IsDisabled,
                CAST(ISNULL(sl.is_policy_checked, 0) AS bit) AS IsPolicyChecked,
                CAST(ISNULL(sl.is_expiration_checked, 0) AS bit) AS IsExpirationChecked
            FROM sys.server_principals sp
            LEFT JOIN sys.sql_logins sl ON sl.principal_id = sp.principal_id
            WHERE sp.name = @loginName
              AND sp.type IN ('S', 'U', 'G', 'C', 'K');",
            new { loginName });

        if (login is null)
        {
            return null;
        }

        var editorOptions = await GetEditorOptionsAsync(connection);
        var languageOptions = await LoadLanguageOptionsAsync(db);
        var currentRoles = await db.QueryAsync<string>(@"
            SELECT role_principal.name
            FROM sys.server_role_members members
            INNER JOIN sys.server_principals role_principal ON role_principal.principal_id = members.role_principal_id
            WHERE members.member_principal_id = @principalId
            ORDER BY role_principal.name;",
            new { principalId = login.PrincipalId });

        var currentServerPermissions = (await db.QueryAsync<PermissionStateRow>(@"
            SELECT permission_name AS Name, state_desc AS StateDescription
            FROM sys.server_permissions
            WHERE class = 100
              AND major_id = 0
              AND grantee_principal_id = @principalId;",
            new { principalId = login.PrincipalId }))
            .ToArray();

        var currentEndpointPermissions = (await db.QueryAsync<EndpointPermissionStateRow>(@"
            SELECT endpoint_ref.name AS EndpointName, permissions.permission_name AS PermissionName, permissions.state_desc AS StateDescription
            FROM sys.server_permissions permissions
            INNER JOIN sys.endpoints endpoint_ref ON endpoint_ref.endpoint_id = permissions.major_id
            WHERE permissions.class_desc = 'ENDPOINT'
              AND permissions.grantee_principal_id = @principalId;",
            new { principalId = login.PrincipalId }))
            .ToArray();

        var currentLoginPermissions = (await db.QueryAsync<LoginPermissionStateRow>(@"
            SELECT target.name AS LoginName, target.type AS PrincipalTypeCode, target.type_desc AS PrincipalTypeDescription, target.principal_id AS PrincipalId, permissions.permission_name AS PermissionName, permissions.state_desc AS StateDescription
            FROM sys.server_permissions permissions
            INNER JOIN sys.server_principals target ON target.principal_id = permissions.major_id
            WHERE permissions.class_desc = 'LOGIN'
              AND permissions.grantee_principal_id = @principalId
              AND target.type IN ('S', 'U', 'G', 'C', 'K');",
            new { principalId = login.PrincipalId }))
            .ToArray();

        var currentServerPermissionMap = currentServerPermissions.ToDictionary(item => item.Name, item => ParsePermissionState(item.StateDescription), NameComparer);
        var currentEndpointPermissionMap = currentEndpointPermissions
            .GroupBy(item => item.EndpointName, NameComparer)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(item => item.PermissionName, item => ParsePermissionState(item.StateDescription), NameComparer),
                NameComparer);

        var currentLoginPermissionMap = currentLoginPermissions
            .GroupBy(item => item.LoginName, NameComparer)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(item => item.PermissionName, item => ParsePermissionState(item.StateDescription), NameComparer),
                NameComparer);

        var summary = MapSummary(new LoginSummaryRow
        {
            Name = login.Name,
            PrincipalId = login.PrincipalId,
            DefaultDatabase = login.DefaultDatabase,
            IsDisabled = login.IsDisabled,
            PrincipalTypeCode = login.PrincipalTypeCode,
            PrincipalTypeDescription = login.PrincipalTypeDescription,
        });

        return new SqlServerLoginDetailInfo
        {
            OriginalName = login.Name,
            Name = login.Name,
            PrincipalId = login.PrincipalId,
            PrincipalTypeCode = summary.PrincipalTypeCode,
            PrincipalTypeLabel = summary.PrincipalTypeLabel,
            AuthenticationType = summary.AuthenticationType,
            IsSystem = summary.IsSystem,
            CanDelete = summary.CanDelete,
            SupportsGeneralEditing = summary.SupportsGeneralEditing,
            SupportsPasswordEditing = summary.SupportsPasswordEditing,
            SupportsRoleEditing = summary.SupportsRoleEditing,
            SupportsPermissionEditing = summary.SupportsPermissionEditing,
            SupportsRename = summary.SupportsGeneralEditing && summary.SupportsPasswordEditing,
            SupportsEnableDisable = summary.SupportsGeneralEditing,
            SupportsPasswordPolicy = summary.SupportsPasswordEditing,
            DefaultDatabase = login.DefaultDatabase,
            DefaultLanguage = NormalizeDefaultLanguage(login.DefaultLanguage, languageOptions),
            IsEnabled = !login.IsDisabled,
            CheckPasswordPolicy = login.IsPolicyChecked,
            CheckPasswordExpiration = login.IsExpirationChecked,
            ServerRoles = currentRoles.Where(item => !string.Equals(item, "public", StringComparison.OrdinalIgnoreCase)).ToArray(),
            ServerPermissions = editorOptions.ServerPermissions.Select(permissionName => new SqlServerPermissionAssignmentInfo
            {
                Name = permissionName,
                State = currentServerPermissionMap.TryGetValue(permissionName, out var state) ? state : SqlServerPermissionState.None,
            }).ToArray(),
            EndpointPermissions = editorOptions.Endpoints.Select(endpointName => new SqlServerEndpointPermissionInfo
            {
                EndpointName = endpointName,
                Permissions = editorOptions.EndpointPermissions.Select(permissionName => new SqlServerPermissionAssignmentInfo
                {
                    Name = permissionName,
                    State = currentEndpointPermissionMap.TryGetValue(endpointName, out var endpointStates) && endpointStates.TryGetValue(permissionName, out var state)
                        ? state
                        : SqlServerPermissionState.None,
                }).ToArray(),
            }).ToArray(),
            LoginPermissions = editorOptions.LoginTargets.Select(target => new SqlServerLoginPermissionTargetInfo
            {
                LoginName = target.Name,
                PrincipalTypeCode = target.PrincipalTypeCode,
                PrincipalTypeLabel = target.PrincipalTypeLabel,
                IsSystem = target.IsSystem,
                Permissions = editorOptions.LoginPermissions.Select(permissionName => new SqlServerPermissionAssignmentInfo
                {
                    Name = permissionName,
                    State = currentLoginPermissionMap.TryGetValue(target.Name, out var loginStates) && loginStates.TryGetValue(permissionName, out var state)
                        ? state
                        : SqlServerPermissionState.None,
                }).ToArray(),
            }).ToArray(),
        };
    }

    public async Task<string> PreviewSaveSqlAsync(ConnectionDefinition connection, SqlServerLoginSaveDefinition definition)
    {
        EnsureSqlServer(connection);
        return await BuildSaveSqlAsync(connection, definition);
    }

    public async Task SaveLoginAsync(ConnectionDefinition connection, SqlServerLoginSaveDefinition definition)
    {
        EnsureSqlServer(connection);

        var sql = await BuildSaveSqlAsync(connection, definition);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        await using var db = DbConnectionFactory.Create(connection);
        await db.OpenAsync();
        await db.ExecuteAsync(sql);
    }

    public async Task DeleteLoginAsync(ConnectionDefinition connection, string loginName)
    {
        EnsureSqlServer(connection);

        var detail = await GetLoginDetailAsync(connection, loginName)
            ?? throw new InvalidOperationException($"未找到登录 {loginName}。");

        if (!detail.CanDelete)
        {
            throw new InvalidOperationException("当前登录不允许删除。");
        }

        await using var db = DbConnectionFactory.Create(connection);
        await db.OpenAsync();
        await db.ExecuteAsync($"DROP LOGIN {QuoteIdentifier(loginName)};");
    }

    private async Task<string> BuildSaveSqlAsync(ConnectionDefinition connection, SqlServerLoginSaveDefinition definition)
    {
        var loginName = definition.Name.Trim();
        if (string.IsNullOrWhiteSpace(loginName))
        {
            throw new InvalidOperationException("登录名不能为空。");
        }

        var normalizedAuthenticationType = NormalizeAuthenticationType(definition.AuthenticationType);
        if (definition.CheckPasswordExpiration && !definition.CheckPasswordPolicy)
        {
            throw new InvalidOperationException("启用密码过期之前必须先启用密码策略。");
        }

        if (definition.MustChangePasswordOnNextLogin && !definition.CheckPasswordPolicy)
        {
            throw new InvalidOperationException("要求下次登录修改密码之前必须先启用密码策略。");
        }

        var current = string.IsNullOrWhiteSpace(definition.OriginalName)
            ? null
            : await GetLoginDetailAsync(connection, definition.OriginalName);

        if (current is null && normalizedAuthenticationType is not ("sql" or "windows"))
        {
            throw new InvalidOperationException("新建登录仅支持 SQL Server 身份验证和 Windows 身份验证。");
        }

        if (!string.IsNullOrWhiteSpace(definition.OriginalName) && current is null)
        {
            throw new InvalidOperationException($"未找到登录 {definition.OriginalName}。");
        }

        var currentName = current?.Name ?? loginName;
        var sqlBuilder = new StringBuilder();

        if (current is null)
        {
            AppendCreateLoginSql(sqlBuilder, loginName, normalizedAuthenticationType, definition);

            if (!definition.IsEnabled)
            {
                sqlBuilder.AppendLine($"ALTER LOGIN {QuoteIdentifier(loginName)} DISABLE;");
            }
        }
        else
        {
            if (!current.SupportsGeneralEditing && !current.SupportsRoleEditing && !current.SupportsPermissionEditing)
            {
                throw new InvalidOperationException("当前登录不支持编辑。");
            }

            if (!string.Equals(current.AuthenticationType, normalizedAuthenticationType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("暂不支持切换现有登录的验证类型。");
            }

            if (current.SupportsRename && !string.Equals(current.Name, loginName, StringComparison.OrdinalIgnoreCase))
            {
                sqlBuilder.AppendLine($"ALTER LOGIN {QuoteIdentifier(current.Name)} WITH NAME = {QuoteIdentifier(loginName)};");
                currentName = loginName;
            }

            AppendAlterGeneralSql(sqlBuilder, current, currentName, definition);
        }

        var desiredRoles = new HashSet<string>(definition.ServerRoles.Where(item => !string.IsNullOrWhiteSpace(item)), NameComparer);
        desiredRoles.Remove("public");
        var currentRoles = new HashSet<string>(current?.ServerRoles ?? Array.Empty<string>(), NameComparer);

        foreach (var roleToAdd in desiredRoles.Except(currentRoles, NameComparer).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            sqlBuilder.AppendLine($"ALTER SERVER ROLE {QuoteIdentifier(roleToAdd)} ADD MEMBER {QuoteIdentifier(currentName)};");
        }

        foreach (var roleToRemove in currentRoles.Except(desiredRoles, NameComparer).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            sqlBuilder.AppendLine($"ALTER SERVER ROLE {QuoteIdentifier(roleToRemove)} DROP MEMBER {QuoteIdentifier(currentName)};");
        }

        AppendServerPermissionSql(sqlBuilder, currentName, current?.ServerPermissions ?? Array.Empty<SqlServerPermissionAssignmentInfo>(), definition.ServerPermissions);
        AppendEndpointPermissionSql(sqlBuilder, currentName, current?.EndpointPermissions ?? Array.Empty<SqlServerEndpointPermissionInfo>(), definition.EndpointPermissions);
        AppendLoginPermissionSql(sqlBuilder, currentName, current?.LoginPermissions ?? Array.Empty<SqlServerLoginPermissionTargetInfo>(), definition.LoginPermissions);

        return sqlBuilder.ToString().Trim();
    }

    private static void AppendCreateLoginSql(StringBuilder sqlBuilder, string loginName, string authenticationType, SqlServerLoginSaveDefinition definition)
    {
        if (authenticationType == "sql")
        {
            if (string.IsNullOrWhiteSpace(definition.Password))
            {
                throw new InvalidOperationException("SQL Server 登录必须提供密码。");
            }

            var options = new List<string>
            {
                $"PASSWORD = N'{EscapeLiteral(definition.Password)}'",
                $"CHECK_POLICY = {(definition.CheckPasswordPolicy ? "ON" : "OFF")}",
                $"CHECK_EXPIRATION = {(definition.CheckPasswordExpiration ? "ON" : "OFF")}",
            };

            if (!string.IsNullOrWhiteSpace(definition.DefaultDatabase))
            {
                options.Add($"DEFAULT_DATABASE = {QuoteIdentifier(definition.DefaultDatabase)}");
            }

            if (!string.IsNullOrWhiteSpace(definition.DefaultLanguage))
            {
                options.Add($"DEFAULT_LANGUAGE = {QuoteIdentifier(definition.DefaultLanguage)}");
            }

            if (definition.MustChangePasswordOnNextLogin)
            {
                options.Add("MUST_CHANGE");
            }

            sqlBuilder.AppendLine($"CREATE LOGIN {QuoteIdentifier(loginName)} WITH {string.Join(", ", options)};");
            return;
        }

        var windowsOptions = new List<string>();
        if (!string.IsNullOrWhiteSpace(definition.DefaultDatabase))
        {
            windowsOptions.Add($"DEFAULT_DATABASE = {QuoteIdentifier(definition.DefaultDatabase)}");
        }

        if (!string.IsNullOrWhiteSpace(definition.DefaultLanguage))
        {
            windowsOptions.Add($"DEFAULT_LANGUAGE = {QuoteIdentifier(definition.DefaultLanguage)}");
        }

        var withClause = windowsOptions.Count > 0 ? $" WITH {string.Join(", ", windowsOptions)}" : string.Empty;
        sqlBuilder.AppendLine($"CREATE LOGIN {QuoteIdentifier(loginName)} FROM WINDOWS{withClause};");
    }

    private static void AppendAlterGeneralSql(StringBuilder sqlBuilder, SqlServerLoginDetailInfo current, string currentName, SqlServerLoginSaveDefinition definition)
    {
        if (current.SupportsPasswordEditing && !string.IsNullOrWhiteSpace(definition.Password))
        {
            var passwordParts = new List<string>
            {
                $"PASSWORD = N'{EscapeLiteral(definition.Password)}'",
            };

            if (definition.UseOldPassword)
            {
                if (string.IsNullOrWhiteSpace(definition.OldPassword))
                {
                    throw new InvalidOperationException("启用旧密码校验时必须填写旧密码。");
                }

                passwordParts.Add($"OLD_PASSWORD = N'{EscapeLiteral(definition.OldPassword)}'");
            }

            if (definition.MustChangePasswordOnNextLogin)
            {
                passwordParts.Add("MUST_CHANGE");
            }

            sqlBuilder.AppendLine($"ALTER LOGIN {QuoteIdentifier(currentName)} WITH {string.Join(", ", passwordParts)};");
        }

        var generalParts = new List<string>();
        if (!string.Equals(current.DefaultDatabase ?? string.Empty, definition.DefaultDatabase ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            generalParts.Add($"DEFAULT_DATABASE = {QuoteIdentifier(string.IsNullOrWhiteSpace(definition.DefaultDatabase) ? "master" : definition.DefaultDatabase)}");
        }

        if (!string.Equals(current.DefaultLanguage ?? string.Empty, definition.DefaultLanguage ?? string.Empty, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(definition.DefaultLanguage))
        {
            generalParts.Add($"DEFAULT_LANGUAGE = {QuoteIdentifier(definition.DefaultLanguage)}");
        }

        if (current.SupportsPasswordPolicy)
        {
            if (current.CheckPasswordPolicy != definition.CheckPasswordPolicy)
            {
                generalParts.Add($"CHECK_POLICY = {(definition.CheckPasswordPolicy ? "ON" : "OFF")}");
            }

            if (current.CheckPasswordExpiration != definition.CheckPasswordExpiration)
            {
                generalParts.Add($"CHECK_EXPIRATION = {(definition.CheckPasswordExpiration ? "ON" : "OFF")}");
            }
        }

        if (generalParts.Count > 0)
        {
            sqlBuilder.AppendLine($"ALTER LOGIN {QuoteIdentifier(currentName)} WITH {string.Join(", ", generalParts)};");
        }

        if (current.SupportsEnableDisable && current.IsEnabled != definition.IsEnabled)
        {
            sqlBuilder.AppendLine($"ALTER LOGIN {QuoteIdentifier(currentName)} {(definition.IsEnabled ? "ENABLE" : "DISABLE")};");
        }
    }

    private static void AppendServerPermissionSql(StringBuilder sqlBuilder, string loginName, IReadOnlyList<SqlServerPermissionAssignmentInfo> currentPermissions, IReadOnlyList<SqlServerPermissionAssignmentInfo> desiredPermissions)
    {
        var currentMap = currentPermissions.ToDictionary(item => item.Name, item => item.State, NameComparer);

        foreach (var permission in desiredPermissions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var currentState = currentMap.TryGetValue(permission.Name, out var state) ? state : SqlServerPermissionState.None;
            if (currentState == permission.State)
            {
                continue;
            }

            if (currentState != SqlServerPermissionState.None)
            {
                sqlBuilder.AppendLine($"REVOKE {permission.Name} TO {QuoteIdentifier(loginName)};");
            }

            AppendServerPermissionState(sqlBuilder, permission.Name, loginName, permission.State);
        }
    }

    private static void AppendEndpointPermissionSql(StringBuilder sqlBuilder, string loginName, IReadOnlyList<SqlServerEndpointPermissionInfo> currentPermissions, IReadOnlyList<SqlServerEndpointPermissionInfo> desiredPermissions)
    {
        var currentMap = currentPermissions.ToDictionary(item => item.EndpointName, item => item.Permissions.ToDictionary(permission => permission.Name, permission => permission.State, NameComparer), NameComparer);

        foreach (var endpoint in desiredPermissions.OrderBy(item => item.EndpointName, StringComparer.OrdinalIgnoreCase))
        {
            var endpointStates = currentMap.TryGetValue(endpoint.EndpointName, out var states) ? states : new Dictionary<string, SqlServerPermissionState>(NameComparer);
            foreach (var permission in endpoint.Permissions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var currentState = endpointStates.TryGetValue(permission.Name, out var state) ? state : SqlServerPermissionState.None;
                var currentlyGranted = currentState == SqlServerPermissionState.Grant || currentState == SqlServerPermissionState.GrantWithGrantOption;
                var desiredGranted = permission.State == SqlServerPermissionState.Grant || permission.State == SqlServerPermissionState.GrantWithGrantOption;

                if (currentState == SqlServerPermissionState.Deny && desiredGranted)
                {
                    sqlBuilder.AppendLine($"REVOKE {permission.Name} ON ENDPOINT::{QuoteIdentifier(endpoint.EndpointName)} TO {QuoteIdentifier(loginName)};");
                    sqlBuilder.AppendLine($"GRANT {permission.Name} ON ENDPOINT::{QuoteIdentifier(endpoint.EndpointName)} TO {QuoteIdentifier(loginName)};");
                    continue;
                }

                if (currentlyGranted == desiredGranted)
                {
                    continue;
                }

                if (currentlyGranted && !desiredGranted)
                {
                    sqlBuilder.AppendLine($"REVOKE {permission.Name} ON ENDPOINT::{QuoteIdentifier(endpoint.EndpointName)} TO {QuoteIdentifier(loginName)};");
                    continue;
                }

                if (!currentlyGranted && desiredGranted)
                {
                    sqlBuilder.AppendLine($"GRANT {permission.Name} ON ENDPOINT::{QuoteIdentifier(endpoint.EndpointName)} TO {QuoteIdentifier(loginName)};");
                }
            }
        }
    }

    private static void AppendLoginPermissionSql(StringBuilder sqlBuilder, string loginName, IReadOnlyList<SqlServerLoginPermissionTargetInfo> currentPermissions, IReadOnlyList<SqlServerLoginPermissionTargetInfo> desiredPermissions)
    {
        var currentMap = currentPermissions.ToDictionary(item => item.LoginName, item => item.Permissions.ToDictionary(permission => permission.Name, permission => permission.State, NameComparer), NameComparer);

        foreach (var target in desiredPermissions.OrderBy(item => item.LoginName, StringComparer.OrdinalIgnoreCase))
        {
            var targetStates = currentMap.TryGetValue(target.LoginName, out var states) ? states : new Dictionary<string, SqlServerPermissionState>(NameComparer);
            foreach (var permission in target.Permissions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var currentState = targetStates.TryGetValue(permission.Name, out var state) ? state : SqlServerPermissionState.None;
                var currentlyGranted = currentState == SqlServerPermissionState.Grant || currentState == SqlServerPermissionState.GrantWithGrantOption;
                var desiredGranted = permission.State == SqlServerPermissionState.Grant || permission.State == SqlServerPermissionState.GrantWithGrantOption;

                if (currentState == SqlServerPermissionState.Deny && desiredGranted)
                {
                    sqlBuilder.AppendLine($"REVOKE {permission.Name} ON LOGIN::{QuoteIdentifier(target.LoginName)} TO {QuoteIdentifier(loginName)};");
                    sqlBuilder.AppendLine($"GRANT {permission.Name} ON LOGIN::{QuoteIdentifier(target.LoginName)} TO {QuoteIdentifier(loginName)};");
                    continue;
                }

                if (currentlyGranted == desiredGranted)
                {
                    continue;
                }

                if (currentlyGranted && !desiredGranted)
                {
                    sqlBuilder.AppendLine($"REVOKE {permission.Name} ON LOGIN::{QuoteIdentifier(target.LoginName)} TO {QuoteIdentifier(loginName)};");
                    continue;
                }

                if (!currentlyGranted && desiredGranted)
                {
                    sqlBuilder.AppendLine($"GRANT {permission.Name} ON LOGIN::{QuoteIdentifier(target.LoginName)} TO {QuoteIdentifier(loginName)};");
                }
            }
        }
    }

    private static void AppendServerPermissionState(StringBuilder sqlBuilder, string permissionName, string loginName, SqlServerPermissionState state)
    {
        switch (state)
        {
            case SqlServerPermissionState.Grant:
                sqlBuilder.AppendLine($"GRANT {permissionName} TO {QuoteIdentifier(loginName)};");
                break;
            case SqlServerPermissionState.GrantWithGrantOption:
                sqlBuilder.AppendLine($"GRANT {permissionName} TO {QuoteIdentifier(loginName)} WITH GRANT OPTION;");
                break;
            case SqlServerPermissionState.Deny:
                sqlBuilder.AppendLine($"DENY {permissionName} TO {QuoteIdentifier(loginName)};");
                break;
        }
    }

    private static SqlServerLoginSummaryInfo MapSummary(LoginSummaryRow row)
    {
        var principalTypeCode = row.PrincipalTypeCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var isSystem = IsSystemLogin(row.Name, row.PrincipalId);
        var authenticationType = principalTypeCode switch
        {
            "S" => "sql",
            "U" => "windows",
            "G" => "windows",
            "C" => "certificate",
            "K" => "asymmetric-key",
            _ => "sql",
        };

        var supportsGeneralEditing = principalTypeCode is "S" or "U" or "G";
        var supportsPasswordEditing = principalTypeCode == "S";

        return new SqlServerLoginSummaryInfo
        {
            Name = row.Name,
            PrincipalId = row.PrincipalId,
            DefaultDatabase = row.DefaultDatabase,
            IsEnabled = !row.IsDisabled,
            PrincipalTypeCode = principalTypeCode,
            PrincipalTypeLabel = MapPrincipalTypeLabel(principalTypeCode, row.PrincipalTypeDescription),
            AuthenticationType = authenticationType,
            IsSystem = isSystem,
            SupportsGeneralEditing = supportsGeneralEditing,
            SupportsPasswordEditing = supportsPasswordEditing,
            SupportsRoleEditing = true,
            SupportsPermissionEditing = true,
            CanDelete = !isSystem,
        };
    }

    private static string MapPrincipalTypeLabel(string principalTypeCode, string? principalTypeDescription)
    {
        return principalTypeCode switch
        {
            "S" => "SQL 登录",
            "U" => "Windows 用户",
            "G" => "Windows 组",
            "C" => "证书映射登录",
            "K" => "非对称密钥映射登录",
            _ => string.IsNullOrWhiteSpace(principalTypeDescription) ? principalTypeCode : principalTypeDescription,
        };
    }

    private static bool IsSystemLogin(string loginName, int principalId)
    {
        if (principalId <= 255)
        {
            return true;
        }

        return loginName.StartsWith("##MS_", StringComparison.OrdinalIgnoreCase)
            && loginName.EndsWith("##", StringComparison.OrdinalIgnoreCase);
    }

    private static SqlServerPermissionState ParsePermissionState(string? stateDescription)
    {
        return stateDescription?.ToUpperInvariant() switch
        {
            "GRANT" => SqlServerPermissionState.Grant,
            "GRANT_WITH_GRANT_OPTION" => SqlServerPermissionState.GrantWithGrantOption,
            "DENY" => SqlServerPermissionState.Deny,
            _ => SqlServerPermissionState.None,
        };
    }

    private static string NormalizeAuthenticationType(string authenticationType)
    {
        return authenticationType.Trim().ToLowerInvariant() switch
        {
            "sql" => "sql",
            "windows" => "windows",
            "certificate" => "certificate",
            "asymmetric-key" => "asymmetric-key",
            _ => throw new InvalidOperationException("不支持当前登录的验证类型。"),
        };
    }

    private static async Task<IReadOnlyList<string>> LoadPermissionNamesAsync(DbConnection db, string securableClass)
    {
        return (await db.QueryAsync<string>(@"
            SELECT DISTINCT permission_name
            FROM sys.fn_builtin_permissions(@securableClass)
            ORDER BY permission_name;",
            new { securableClass }))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string EscapeLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string? NormalizeDefaultLanguage(string? defaultLanguage, IReadOnlyList<SqlServerSelectOptionInfo> languages)
    {
        if (string.IsNullOrWhiteSpace(defaultLanguage))
        {
            return null;
        }

        var matched = languages.FirstOrDefault(item => string.Equals(item.Value, defaultLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Label, defaultLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.MatchName, defaultLanguage, StringComparison.OrdinalIgnoreCase));

        return matched?.Value ?? defaultLanguage;
    }

    private static async Task<IReadOnlyList<SqlServerSelectOptionInfo>> LoadLanguageOptionsAsync(DbConnection db)
    {
        var languageRows = (await db.QueryAsync<LanguageOptionRow>(@"
            SELECT
                alias AS Value,
                alias AS Label,
                name AS MatchName
            FROM sys.syslanguages
            ORDER BY alias;"))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToArray();

        return languageRows.Select(item => new SqlServerSelectOptionInfo
        {
            Value = item.Value,
            Label = item.Label,
            MatchName = item.MatchName,
        }).ToArray();
    }

    private static void EnsureSqlServer(ConnectionDefinition connection)
    {
        if (connection.ProviderType != DatabaseProviderType.SqlServer)
        {
            throw new InvalidOperationException("当前连接不是 SQL Server，暂不支持用户管理。");
        }
    }

    private sealed class LoginSummaryRow
    {
        public string Name { get; set; } = string.Empty;

        public int PrincipalId { get; set; }

        public string? DefaultDatabase { get; set; }

        public bool IsDisabled { get; set; }

        public string? PrincipalTypeCode { get; set; }

        public string? PrincipalTypeDescription { get; set; }
    }

    private sealed class LoginDetailRow
    {
        public string Name { get; set; } = string.Empty;

        public int PrincipalId { get; set; }

        public string? DefaultDatabase { get; set; }

        public bool IsDisabled { get; set; }

        public string? PrincipalTypeCode { get; set; }

        public string? PrincipalTypeDescription { get; set; }

        public string? DefaultLanguage { get; set; }

        public bool IsPolicyChecked { get; set; }

        public bool IsExpirationChecked { get; set; }
    }

    private sealed class PermissionStateRow
    {
        public string Name { get; set; } = string.Empty;

        public string? StateDescription { get; set; }
    }

    private sealed class EndpointPermissionStateRow
    {
        public string EndpointName { get; set; } = string.Empty;

        public string PermissionName { get; set; } = string.Empty;

        public string? StateDescription { get; set; }
    }

    private sealed class LoginPermissionStateRow
    {
        public string LoginName { get; set; } = string.Empty;

        public string PrincipalTypeCode { get; set; } = string.Empty;

        public string PrincipalTypeDescription { get; set; } = string.Empty;

        public int PrincipalId { get; set; }

        public string PermissionName { get; set; } = string.Empty;

        public string? StateDescription { get; set; }
    }

    private sealed class LanguageOptionRow
    {
        public string Value { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string? MatchName { get; set; }
    }
}