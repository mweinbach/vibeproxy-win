param(
    [string]$OutputDir = "src\VibeProxy.WinUI\Resources\bin"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

Write-Host "Building thinking proxy..."
Push-Location "src\VibeProxy.Proxy"
go build -o (Join-Path $PWD "..\..\$OutputDir\thinking-proxy.exe")
Pop-Location

Write-Host "Thinking proxy build complete."