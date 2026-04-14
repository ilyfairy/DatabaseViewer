using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using DatabaseViewer.Api.Contracts;
using DatabaseViewer.Api.Services;
using DatabaseViewer.Core.Services;
using System.Net;
using System.Net.Sockets;

namespace DatabaseViewer.Api;

public sealed class ApiRuntime : IAsyncDisposable
{
    public required WebApplication App { get; init; }
    public required string BaseUrl { get; init; }
    public required string ListenUrl { get; init; }
    public required string FrontendUrl { get; init; }
    public required bool UsesFrontendDevServer { get; init; }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

public static class ApiHost
{
    private const string DefaultFrontendDevServerUrl = "http://127.0.0.1:5173";
    private static readonly AllowedNetwork DefaultLoopbackNetwork = AllowedNetwork.Parse("127.0.0.1/32");
    private static readonly AllowedNetwork DefaultIpv6LoopbackNetwork = AllowedNetwork.Parse("::1/128");

    public static async Task<ApiRuntime> StartAsync(string? overrideBaseUrl = null, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ApiHost).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            Args = Array.Empty<string>(),
        });

        var hostSettings = builder.Configuration
            .GetSection(ApiHostSettings.SectionName)
            .Get<ApiHostSettings>()
            ?? new ApiHostSettings();

        var listenUrl = overrideBaseUrl ?? ResolveListenUrl(hostSettings);
        var baseUrl = ResolveClientBaseUrl(listenUrl);
        var usesFrontendDevServer = ShouldUseFrontendDevServer();
        var frontendUrl = ResolveFrontendUrl(baseUrl, usesFrontendDevServer);
        var allowedNetworks = ResolveAllowedNetworks(hostSettings);

        builder.WebHost.UseUrls(listenUrl);
        builder.Services.AddSingleton<Base64DataCodec>();
        builder.Services.AddSingleton<ApplicationSettingsStore>();
        builder.Services.AddSingleton<ConnectionStore>();
        builder.Services.AddSingleton<DatabaseMetadataService>();
        builder.Services.AddSingleton<DatabaseQueryService>();
        builder.Services.AddSingleton<ExplorerApiService>();
        builder.Services.AddSingleton<SqlServerLoginManagementService>();
        builder.Services.AddSingleton<SqlServerLoginAdminService>();

        var app = builder.Build();
        var explorer = app.Services.GetRequiredService<ExplorerApiService>();
        var sqlServerLogins = app.Services.GetRequiredService<SqlServerLoginAdminService>();

        app.Use(async (context, next) =>
        {
            if (!IsRequestAllowed(context.Connection.RemoteIpAddress, allowedNetworks))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden");
                return;
            }

            await next();
        });

        app.MapGet("/api/explorer/settings", async () => await explorer.GetSettingsAsync());
        app.MapPut("/api/explorer/settings", async (UpdateExplorerSettingsRequest request) => await explorer.UpdateSettingsAsync(request));
        app.MapGet("/api/explorer/workspace-layout", async () => await explorer.GetWorkspaceLayoutAsync());
        app.MapPut("/api/explorer/workspace-layout", async (UpdateWorkspaceLayoutRequest request) => await explorer.UpdateWorkspaceLayoutAsync(request));
        app.MapGet("/api/explorer/bootstrap", async () => await explorer.GetBootstrapAsync());
        app.MapGet("/api/explorer/connections/{connectionId:guid}", async (Guid connectionId) => await explorer.GetConnectionConfigAsync(connectionId));
        app.MapGet("/api/explorer/collations", async (Guid connectionId, string database) =>
        {
            try
            {
                return Results.Ok(await explorer.GetCollationsAsync(connectionId, database));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/database-graph", async (Guid connectionId, string database) =>
        {
            try
            {
                return Results.Ok(await explorer.GetDatabaseGraphAsync(connectionId, database));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/table", async (string tableKey, int? offset, int? pageSize, string? sortColumn, string? sortDirection) =>
        {
            try
            {
                return Results.Ok(await explorer.GetTablePageAsync(tableKey, offset ?? 0, pageSize ?? 100, sortColumn, sortDirection));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/table-design", async (string tableKey) =>
        {
            try
            {
                return Results.Ok(await explorer.GetTableDesignAsync(tableKey));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/table-search", async (string tableKey, string query, string[]? columns, int? offset, int? pageSize, string? sortColumn, string? sortDirection) =>
        {
            try
            {
                return Results.Ok(await explorer.SearchTableAsync(tableKey, query, columns, offset ?? 0, pageSize ?? 100, sortColumn, sortDirection));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/sql-context", async (Guid connectionId, string database) =>
        {
            try
            {
                return Results.Ok(await explorer.GetSqlContextAsync(connectionId, database));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/catalog-object", async (Guid connectionId, string database, string objectType, string? schema, string name) =>
        {
            try
            {
                return Results.Ok(await explorer.GetCatalogObjectDetailAsync(connectionId, database, objectType, schema, name));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/sqlserver-logins", async (Guid connectionId) =>
        {
            try
            {
                return Results.Ok(await sqlServerLogins.GetLoginsAsync(connectionId));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/sqlserver-logins/editor-options", async (Guid connectionId) =>
        {
            try
            {
                return Results.Ok(await sqlServerLogins.GetEditorOptionsAsync(connectionId));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/sqlserver-logins/{loginName}", async (Guid connectionId, string loginName) =>
        {
            try
            {
                return Results.Ok(await sqlServerLogins.GetLoginDetailAsync(connectionId, loginName));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/sqlserver-logins/sql-preview", async (SaveSqlServerLoginRequest request) =>
        {
            try
            {
                return Results.Ok(await sqlServerLogins.PreviewSaveSqlAsync(request));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPut("/api/explorer/sqlserver-logins", async (SaveSqlServerLoginRequest request) =>
        {
            try
            {
                await sqlServerLogins.SaveAsync(request);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapDelete("/api/explorer/sqlserver-logins/{loginName}", async (Guid connectionId, string loginName) =>
        {
            try
            {
                await sqlServerLogins.DeleteAsync(connectionId, loginName);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/record", async (string tableKey, string rowKey) =>
        {
            try
            {
                return Results.Ok(await explorer.GetRecordAsync(tableKey, rowKey));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/cell", async (string tableKey, string rowKey, string columnName) =>
        {
            try
            {
                return Results.Ok(await explorer.GetCellContentAsync(tableKey, rowKey, columnName));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapGet("/api/explorer/foreign-key", async (string tableKey, string rowKey, string columnName) =>
        {
            try
            {
                var target = await explorer.ResolveForeignKeyAsync(tableKey, rowKey, columnName);
                return target is null ? Results.NotFound() : Results.Ok(target);
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/sql-execute", async (SqlExecutionRequest request) =>
        {
            try
            {
                return Results.Ok(await explorer.ExecuteSqlAsync(request));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/routine-source", async (RoutineSourceRequest request) =>
        {
            try
            {
                return Results.Ok(await explorer.GetRoutineSourceAsync(request));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/table-cell", async (TableCellUpdateRequest request) =>
        {
            try
            {
                return Results.Ok(await explorer.UpdateTableCellAsync(request));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/table-row", async (TableRowInsertRequest request) =>
        {
            try
            {
                return Results.Ok(await explorer.InsertTableRowAsync(request));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapDelete("/api/explorer/table-row", async (string tableKey, string rowKey) =>
        {
            try
            {
                await explorer.DeleteTableRowAsync(new TableRowDeleteRequest(tableKey, rowKey));
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/connections", async (CreateConnectionRequest request) =>
        {
            try
            {
                return Results.Ok(await explorer.CreateConnectionAsync(request));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/connections/test", async (TestConnectionRequest request) =>
        {
            try
            {
                await explorer.TestConnectionAsync(request);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/explorer/connections/rekey", async (SqliteRekeyRequest request) =>
        {
            try
            {
                await explorer.RekeySqliteDatabaseAsync(request);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapPut("/api/explorer/connections/{connectionId:guid}", async (Guid connectionId, CreateConnectionRequest request) =>
        {
            try
            {
                return Results.Ok(await explorer.UpdateConnectionAsync(connectionId, request));
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
        app.MapDelete("/api/explorer/connections/{connectionId:guid}", async (Guid connectionId) =>
        {
            await explorer.DeleteConnectionAsync(connectionId);
            return Results.NoContent();
        });
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

        var frontendPath = FrontendLocator.FindWwwRootDirectory();
        if (usesFrontendDevServer)
        {
            app.MapGet("/", () => Results.Redirect(frontendUrl));
            app.MapFallback(context =>
            {
                context.Response.Redirect(BuildFrontendRedirectUrl(frontendUrl, context.Request.Path, context.Request.QueryString.Value), permanent: false);
                return Task.CompletedTask;
            });
        }
        else if (!string.IsNullOrWhiteSpace(frontendPath))
        {
            var provider = new PhysicalFileProvider(frontendPath);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = provider,
                ContentTypeProvider = new FileExtensionContentTypeProvider(),
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                    context.Context.Response.Headers.Pragma = "no-cache";
                    context.Context.Response.Headers.Expires = "0";
                },
            });
            app.MapFallback(async context =>
            {
                context.Response.ContentType = "text/html";
                context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "0";
                await context.Response.SendFileAsync(Path.Combine(frontendPath, "index.html"));
            });
        }
        else
        {
            app.MapGet("/", () => Results.Content("Frontend wwwroot not found. Run 'pnpm dev' for debug mode or 'pnpm build' for static mode.", "text/plain"));
        }

        await app.StartAsync(cancellationToken);
        return new ApiRuntime
        {
            App = app,
            BaseUrl = baseUrl,
            ListenUrl = listenUrl,
            FrontendUrl = frontendUrl,
            UsesFrontendDevServer = usesFrontendDevServer,
        };
    }

    /// <summary>
    /// Resolves which frontend URL the client should open for the current host mode.
    /// </summary>
    private static string ResolveFrontendUrl(string baseUrl, bool usesFrontendDevServer)
    {
        if (!usesFrontendDevServer)
        {
            return baseUrl;
        }

        return NormalizeUrl(DefaultFrontendDevServerUrl);
    }

    /// <summary>
    /// Enables the Vite dev server workflow for debug builds only.
    /// </summary>
    private static bool ShouldUseFrontendDevServer()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// Preserves the requested path/query when redirecting API-root navigation to the Vite dev server.
    /// </summary>
    private static string BuildFrontendRedirectUrl(string frontendUrl, PathString path, string? queryString)
    {
        var normalizedFrontendUrl = frontendUrl.TrimEnd('/');
        var requestedPath = path.HasValue && path.Value != "/" ? path.Value : string.Empty;
        return $"{normalizedFrontendUrl}{requestedPath}{queryString}";
    }

    /// <summary>
    /// Resolves the configured allowed networks, or defaults to 127.0.0.1 only.
    /// </summary>
    private static IReadOnlyList<AllowedNetwork> ResolveAllowedNetworks(ApiHostSettings hostSettings)
    {
        var configuredNetworks = hostSettings.AllowedNetworks?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(AllowedNetwork.Parse)
            .ToArray();

        if (configuredNetworks is { Length: > 0 })
        {
            return configuredNetworks;
        }

        return new[] { DefaultLoopbackNetwork, DefaultIpv6LoopbackNetwork };
    }

    /// <summary>
    /// Checks whether the request remote address matches one of the configured networks.
    /// </summary>
    private static bool IsRequestAllowed(IPAddress? remoteAddress, IReadOnlyList<AllowedNetwork> allowedNetworks)
    {
        if (remoteAddress is null)
        {
            return false;
        }

        return allowedNetworks.Any(network => network.Contains(remoteAddress));
    }

    /// <summary>
    /// Resolves the configured listen URL, or falls back to an ephemeral loopback port.
    /// </summary>
    private static string ResolveListenUrl(ApiHostSettings hostSettings)
    {
        if (string.IsNullOrWhiteSpace(hostSettings.ListenUrl))
        {
            return GetAvailableBaseUrl();
        }

        return NormalizeUrl(hostSettings.ListenUrl);
    }

    /// <summary>
    /// Converts wildcard or any-address listen URLs into a browser-safe local URL for the embedded client.
    /// </summary>
    private static string ResolveClientBaseUrl(string listenUrl)
    {
        var normalizedUrl = NormalizeUrl(listenUrl);
        var wildcardUrl = TryResolveWildcardClientUrl(normalizedUrl);
        if (!string.IsNullOrWhiteSpace(wildcardUrl))
        {
            return wildcardUrl;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid listen URL '{listenUrl}'.");
        }

        var clientHost = uri.Host switch
        {
            "0.0.0.0" => "127.0.0.1",
            "::" => "localhost",
            "[::]" => "localhost",
            _ => uri.Host,
        };

        var builder = new UriBuilder(uri)
        {
            Host = clientHost,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Normalizes configured URLs so downstream parsing and logging stay consistent.
    /// </summary>
    private static string NormalizeUrl(string url)
    {
        var normalizedUrl = url.Trim();
        if (!normalizedUrl.Contains("://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Configured listen URL '{url}' must be an absolute http or https URL.");
        }

        return normalizedUrl.TrimEnd('/');
    }

    /// <summary>
    /// Resolves wildcard Kestrel URL syntax to a local loopback address for the embedded browser.
    /// </summary>
    private static string? TryResolveWildcardClientUrl(string listenUrl)
    {
        var schemeSeparatorIndex = listenUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparatorIndex < 0)
        {
            return null;
        }

        var scheme = listenUrl[..schemeSeparatorIndex];
        var authority = listenUrl[(schemeSeparatorIndex + 3)..];
        var pathSeparatorIndex = authority.IndexOf('/');
        if (pathSeparatorIndex >= 0)
        {
            authority = authority[..pathSeparatorIndex];
        }

        if (authority == "*" || authority == "+")
        {
            return $"{scheme}://127.0.0.1";
        }

        if (authority.StartsWith("*:", StringComparison.Ordinal) || authority.StartsWith("+:", StringComparison.Ordinal))
        {
            return $"{scheme}://127.0.0.1{authority[1..]}";
        }

        return null;
    }

    /// <summary>
    /// Represents one configured IP address or CIDR network rule.
    /// </summary>
    private sealed class AllowedNetwork
    {
        private AllowedNetwork(IPAddress networkAddress, int prefixLength)
        {
            NetworkAddress = NormalizeAddress(networkAddress);
            PrefixLength = prefixLength;
        }

        private IPAddress NetworkAddress { get; }

        private int PrefixLength { get; }

        /// <summary>
        /// Parses a single IP or CIDR expression such as 127.0.0.1, 192.168.0.0/16, or 10.1.1.1/8.
        /// </summary>
        public static AllowedNetwork Parse(string value)
        {
            var trimmedValue = value.Trim();
            var separatorIndex = trimmedValue.IndexOf('/');
            var addressText = separatorIndex >= 0 ? trimmedValue[..separatorIndex] : trimmedValue;
            var prefixText = separatorIndex >= 0 ? trimmedValue[(separatorIndex + 1)..] : null;

            if (!IPAddress.TryParse(addressText, out var parsedAddress))
            {
                throw new InvalidOperationException($"Invalid allowed network '{value}'.");
            }

            var normalizedAddress = NormalizeAddress(parsedAddress);
            var maxPrefixLength = normalizedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            var prefixLength = prefixText is null ? maxPrefixLength : ParsePrefixLength(prefixText, maxPrefixLength, value);

            return new AllowedNetwork(normalizedAddress, prefixLength);
        }

        /// <summary>
        /// Returns true when the candidate address is inside this network.
        /// </summary>
        public bool Contains(IPAddress address)
        {
            var normalizedAddress = NormalizeAddress(address);
            if (normalizedAddress.AddressFamily != NetworkAddress.AddressFamily)
            {
                return false;
            }

            var candidateBytes = normalizedAddress.GetAddressBytes();
            var networkBytes = NetworkAddress.GetAddressBytes();
            var wholeByteCount = PrefixLength / 8;
            var remainingBitCount = PrefixLength % 8;

            for (var index = 0; index < wholeByteCount; index++)
            {
                if (candidateBytes[index] != networkBytes[index])
                {
                    return false;
                }
            }

            if (remainingBitCount == 0)
            {
                return true;
            }

            var bitMask = (byte)(0xFF << (8 - remainingBitCount));
            return (candidateBytes[wholeByteCount] & bitMask) == (networkBytes[wholeByteCount] & bitMask);
        }

        /// <summary>
        /// Normalizes IPv4-mapped IPv6 addresses into plain IPv4 so comparisons stay consistent.
        /// </summary>
        private static IPAddress NormalizeAddress(IPAddress address)
        {
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }

        /// <summary>
        /// Parses and validates a CIDR prefix length for one network entry.
        /// </summary>
        private static int ParsePrefixLength(string value, int maxPrefixLength, string originalRule)
        {
            if (!int.TryParse(value, out var prefixLength) || prefixLength < 0 || prefixLength > maxPrefixLength)
            {
                throw new InvalidOperationException($"Invalid allowed network '{originalRule}'.");
            }

            return prefixLength;
        }
    }

    private static string GetAvailableBaseUrl()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"http://127.0.0.1:{port}";
    }
}