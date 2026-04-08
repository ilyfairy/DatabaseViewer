param(
    [ValidateSet('Api', 'App', 'All')]
    [string]$Target = 'All',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts\publish'),

    [string]$AppRuntime = 'win-x64',

    [switch]$SelfContainedApp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-LastCommandSucceeded {
    param(
        [string]$OperationName
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$OperationName failed with exit code $LASTEXITCODE."
    }
}

function Publish-DatabaseViewerApi {
    param(
        [string]$PublishConfiguration,
        [string]$PublishOutputRoot
    )

    $apiOutputPath = Join-Path $PublishOutputRoot 'api'
    New-Item -ItemType Directory -Force -Path $apiOutputPath | Out-Null

    Write-Host "Publishing standalone API to $apiOutputPath"
    dotnet publish (Join-Path $PSScriptRoot 'DatabaseViewer.Api\DatabaseViewer.Api.csproj') `
        -c $PublishConfiguration `
        -o $apiOutputPath `
        -p:Standalone=true

    Assert-LastCommandSucceeded -OperationName 'Publishing standalone API'
}

function Publish-DatabaseViewerApp {
    param(
        [string]$PublishConfiguration,
        [string]$PublishOutputRoot,
        [string]$RuntimeIdentifier,
        [bool]$UseSelfContained
    )

    $appOutputPath = Join-Path $PublishOutputRoot 'app'
    New-Item -ItemType Directory -Force -Path $appOutputPath | Out-Null

    Write-Host "Publishing WPF app to $appOutputPath"
    dotnet publish (Join-Path $PSScriptRoot 'DatabaseViewer.App\DatabaseViewer.App.csproj') `
        -c $PublishConfiguration `
        -o $appOutputPath `
        -r $RuntimeIdentifier `
        --self-contained:$UseSelfContained

    Assert-LastCommandSucceeded -OperationName 'Publishing WPF app'
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $resolvedOutputRoot | Out-Null

switch ($Target) {
    'Api' {
        Publish-DatabaseViewerApi -PublishConfiguration $Configuration -PublishOutputRoot $resolvedOutputRoot
    }
    'App' {
        Publish-DatabaseViewerApp -PublishConfiguration $Configuration -PublishOutputRoot $resolvedOutputRoot -RuntimeIdentifier $AppRuntime -UseSelfContained $SelfContainedApp.IsPresent
    }
    'All' {
        Publish-DatabaseViewerApi -PublishConfiguration $Configuration -PublishOutputRoot $resolvedOutputRoot
        Publish-DatabaseViewerApp -PublishConfiguration $Configuration -PublishOutputRoot $resolvedOutputRoot -RuntimeIdentifier $AppRuntime -UseSelfContained $SelfContainedApp.IsPresent
    }
}

Write-Host "Publish outputs created under $resolvedOutputRoot"