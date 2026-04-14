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

发布 API 和桌面程序：

```powershell
./publish.ps1
```

只发布独立 API：

```powershell
./publish.ps1 -Target Api
```

只发布桌面程序：

```powershell
./publish.ps1 -Target App -AppRuntime win-x64
```

仅启动前端开发服务器：

```powershell
Set-Location database-viewer-web
pnpm dev
```

## 说明

- `DatabaseViewer.Api` 会在 .NET 构建前自动执行 `database-viewer-web` 下的 `pnpm build`
- `DatabaseViewer.Api` 构建前会先执行 `database-viewer-web` 下的 `pnpm build`，然后把 `database-viewer-web/dist` 复制到 build/publish 输出目录中的 `wwwroot`
- `DatabaseViewer.Api/appsettings.json` 中的 `ApiHost:ListenUrl` 会复制到程序目录，用来固定监听地址，例如 `http://127.0.0.1:5027`、`http://*:5027` 或 `http://[::]:5027`
- `DatabaseViewer.Api/appsettings.json` 中的 `ApiHost:AllowedNetworks` 用来限制访问来源，支持多个 IP 或 CIDR，例如 `127.0.0.1/32`、`::1/128`、`192.168.0.0/16`、`10.1.1.1/8`；未配置时默认只允许本机回环地址 `127.0.0.1` 和 `::1`
- 当监听地址使用 `*`、`+`、`0.0.0.0` 或 `[::]` 这类通配/任意地址时，嵌入式 WPF 浏览器仍会自动通过本地回环地址访问 API
- 如果后续再次调整仓库结构，请同步更新 `DatabaseViewer.slnx`、`DatabaseViewer.Api/DatabaseViewer.Api.csproj` 和 `DatabaseViewer.Api/Services/FrontendLocator.cs`