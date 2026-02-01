param(
    [string]$OutputDir = "src\VibeProxy.WinUI\Resources\bin",
    [string]$RepoUrl = "https://github.com/router-for-me/CLIProxyAPIPlus",
    [string]$RepoPath = "$env:TEMP\CLIProxyAPIPlus"
)

$ErrorActionPreference = "Stop"

$gitCmd = Get-Command git -ErrorAction SilentlyContinue
if (-not $gitCmd) {
    throw "Git not found on PATH. Install Git for Windows or add it to PATH."
}

$goCmd = Get-Command go -ErrorAction SilentlyContinue
if ($goCmd) {
    $goExe = $goCmd.Source
}
if (-not $goExe) {
    $defaultGo = "C:\Program Files\Go\bin\go.exe"
    if (Test-Path $defaultGo) {
        $goExe = $defaultGo
    } else {
        throw "Go not found on PATH. Install Go or add it to PATH."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputPath = Join-Path $repoRoot $OutputDir
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
}

if (-not (Test-Path $RepoPath)) {
    git clone $RepoUrl $RepoPath
}

Push-Location $RepoPath
Write-Host "Building CLIProxyAPIPlus..."
& $goExe build -o (Join-Path $PWD "cli-proxy-api-plus.exe") .\cmd\server
Copy-Item (Join-Path $PWD "cli-proxy-api-plus.exe") (Join-Path $outputPath "cli-proxy-api-plus.exe") -Force
Pop-Location

Write-Host "CLIProxyAPIPlus build complete."
