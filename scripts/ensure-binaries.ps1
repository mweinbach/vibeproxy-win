param(
    [string]$OutputDir = "src\VibeProxy.WinUI\Resources\bin"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputPath = Join-Path $repoRoot $OutputDir

$thinkingProxy = Join-Path $outputPath "thinking-proxy.exe"
$cliProxy = Join-Path $outputPath "cli-proxy-api-plus.exe"

if ((Test-Path $thinkingProxy) -and (Test-Path $cliProxy)) {
    Write-Host "Binaries already present: $OutputDir"
    exit 0
}

Write-Host "Missing binaries; building into $OutputDir"
try {
    & "$PSScriptRoot\build-all.ps1"
} catch {
    Write-Warning "Could not build proxy/backend binaries. The app UI can still run, but the server won't start until prerequisites (Go/Git) are installed."
    Write-Warning $_.Exception.Message
    exit 0
}

if (-not (Test-Path $thinkingProxy)) {
    Write-Warning "thinking-proxy.exe still missing at: $thinkingProxy"
    exit 0
}

if (-not (Test-Path $cliProxy)) {
    Write-Warning "cli-proxy-api-plus.exe still missing at: $cliProxy"
    exit 0
}

Write-Host "Binaries ready."
