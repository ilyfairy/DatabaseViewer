using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using DatabaseViewer.Api.Contracts;
using DatabaseViewer.Api.Services;
using DatabaseViewer.Core.Services;
using System.Net;
using System.Net.Sockets;

namespace DatabaseViewer.Api;

public sealed class DesktopApiRuntime : IAsyncDisposable
{
    public required WebApplication App { get; init; }
    public required string BaseUrl { get; init; }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

public static class DesktopApiHost
{
    public static async Task<DesktopApiRuntime> StartAsync(string? overrideBaseUrl = null, CancellationToken cancellationToken = default)
    {
        var baseUrl = overrideBaseUrl ?? GetAvailableBaseUrl();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(DesktopApiHost).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            Args = Array.Empty<string>(),
        });

        builder.WebHost.UseUrls(baseUrl);
        builder.Services.AddSingleton<WindowsDataProtector>();
        builder.Services.AddSingleton<ConnectionStore>();
        builder.Services.AddSingleton<DatabaseMetadataService>();
        builder.Services.AddSingleton<DatabaseQueryService>();
        builder.Services.AddSingleton<ExplorerApiService>();

        var app = builder.Build();
        var explorer = app.Services.GetRequiredService<ExplorerApiService>();

        app.MapGet("/api/explorer/bootstrap", async () => await explorer.GetBootstrapAsync());
        app.MapGet("/api/explorer/connections/{connectionId:guid}", async (Guid connectionId) => await explorer.GetConnectionConfigAsync(connectionId));
        app.MapGet("/api/explorer/database-graph", async (Guid connectionId, string database) => await explorer.GetDatabaseGraphAsync(connectionId, database));
        app.MapGet("/api/explorer/table", async (string tableKey, int? offset, int? pageSize, string? sortColumn, string? sortDirection) => await explorer.GetTablePageAsync(tableKey, offset ?? 0, pageSize ?? 100, sortColumn, sortDirection));
        app.MapGet("/api/explorer/table-design", async (string tableKey) => await explorer.GetTableDesignAsync(tableKey));
        app.MapGet("/api/explorer/table-search", async (string tableKey, string query, string[]? columns, int? offset, int? pageSize, string? sortColumn, string? sortDirection) => await explorer.SearchTableAsync(tableKey, query, columns, offset ?? 0, pageSize ?? 100, sortColumn, sortDirection));
        app.MapGet("/api/explorer/sql-context", async (Guid connectionId, string database) => await explorer.GetSqlContextAsync(connectionId, database));
        app.MapGet("/api/explorer/record", async (string tableKey, string rowKey) => await explorer.GetRecordAsync(tableKey, rowKey));
        app.MapGet("/api/explorer/cell", async (string tableKey, string rowKey, string columnName) => await explorer.GetCellContentAsync(tableKey, rowKey, columnName));
        app.MapGet("/api/explorer/foreign-key", async (string tableKey, string rowKey, string columnName) =>
        {
            var target = await explorer.ResolveForeignKeyAsync(tableKey, rowKey, columnName);
            return target is null ? Results.NotFound() : Results.Ok(target);
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

        var distPath = FrontendLocator.FindDistDirectory();
        if (!string.IsNullOrWhiteSpace(distPath))
        {
            var provider = new PhysicalFileProvider(distPath);
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
                await context.Response.SendFileAsync(Path.Combine(distPath, "index.html"));
            });
        }
        else
        {
            app.MapGet("/", () => Results.Content("Frontend dist not found. Run 'pnpm build' in database-viewer-web.", "text/plain"));
        }

        await app.StartAsync(cancellationToken);
        return new DesktopApiRuntime { App = app, BaseUrl = baseUrl };
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