# Database Viewer

基于 WPF、ASP.NET Core 和 Vue 的桌面数据库浏览器。

## 目录结构

- `DatabaseViewer.slnx`：解决方案入口
- `DatabaseViewer.App`：WPF 桌面壳程序，负责承载 WebView2
- `DatabaseViewer.Api`：本地 ASP.NET Core 后端，同时负责托管前端静态文件
- `DatabaseViewer.Core`：共享模型和数据库服务
- `database-viewer-web`：Vue 前端项目

## 环境要求

- .NET SDK 8.0
- Node.js 20 或更高版本
- `pnpm`
- 机器已安装 WebView2 Runtime，或者在 `DatabaseViewer.App/WebView2Runtime` 下放置固定版本运行时

## 安装与构建

前端依赖安装与构建：

```powershell
Set-Location database-viewer-web
pnpm install
pnpm build
```

整仓构建：

```powershell
Set-Location ..
dotnet build DatabaseViewer.slnx
```

## 运行方式

运行桌面程序：

```powershell
dotnet run --project DatabaseViewer.App -c Release
```

仅启动前端开发服务器：

```powershell
Set-Location database-viewer-web
pnpm dev
```

## 说明

- `DatabaseViewer.Api` 会在 .NET 构建前自动执行 `database-viewer-web` 下的 `pnpm build`
- 开发环境下，API 会直接从 `database-viewer-web/dist` 提供前端文件；发布时会使用复制到输出目录中的 `dist`
- 如果后续再次调整仓库结构，请同步更新 `DatabaseViewer.slnx`、`DatabaseViewer.Api/DatabaseViewer.Api.csproj` 和 `DatabaseViewer.Api/Services/FrontendLocator.cs`