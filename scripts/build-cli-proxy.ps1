param(
    [string]$OutputDir = "src\VibeProxy.WinUI\Resources\bin",
    [string]$RepoUrl = "https://github.com/router-for-me/CLIProxyAPIPlus",
    [string]$RepoPath = "$env:TEMP\CLIProxyAPIPlus"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

if (-not (Test-Path $RepoPath)) {
    git clone $RepoUrl $RepoPath
}

Push-Location $RepoPath
Write-Host "Building CLIProxyAPIPlus..."
go build -o (Join-Path $PWD "cli-proxy-api-plus.exe") .\cmd\server
Copy-Item (Join-Path $PWD "cli-proxy-api-plus.exe") (Join-Path (Resolve-Path $OutputDir) "cli-proxy-api-plus.exe") -Force
Pop-Location

Write-Host "CLIProxyAPIPlus build complete."