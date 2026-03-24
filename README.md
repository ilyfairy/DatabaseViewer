# Database Viewer

Desktop database explorer built with WPF, ASP.NET Core, and Vue.

## Repository Layout

- `DatabaseViewer.slnx`: solution entry point
- `DatabaseViewer.App`: WPF desktop shell with WebView2 host
- `DatabaseViewer.Api`: local ASP.NET Core backend and static frontend host
- `DatabaseViewer.Core`: shared data models and database services
- `database-viewer-web`: Vue frontend

## Requirements

- .NET SDK 8.0
- Node.js 20+
- `pnpm`
- WebView2 Runtime on the machine, or a bundled fixed runtime under `DatabaseViewer.App/WebView2Runtime`

## Restore And Build

Frontend:

```powershell
Set-Location database-viewer-web
pnpm install
pnpm build
```

Full solution:

```powershell
Set-Location ..
dotnet build DatabaseViewer.slnx
```

## Run

Desktop app:

```powershell
dotnet run --project DatabaseViewer.App -c Release
```

Frontend only during UI work:

```powershell
Set-Location database-viewer-web
pnpm dev
```

## Notes

- `DatabaseViewer.Api` automatically runs `pnpm build` in `database-viewer-web` before .NET builds.
- The API serves frontend files from `database-viewer-web/dist` in development and from a copied `dist` folder in published output.
- If you move the repository again, update `DatabaseViewer.slnx`, `DatabaseViewer.Api/DatabaseViewer.Api.csproj`, and `DatabaseViewer.Api/Services/FrontendLocator.cs` together.