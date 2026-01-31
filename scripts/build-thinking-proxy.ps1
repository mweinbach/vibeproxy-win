param(
    [string]$OutputDir = "src\VibeProxy.WinUI\Resources\bin"
)

$ErrorActionPreference = "Stop"

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

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

Write-Host "Building thinking proxy..."
Push-Location "src\VibeProxy.Proxy"
& $goExe build -o (Join-Path $PWD "..\..\$OutputDir\thinking-proxy.exe")
Pop-Location

Write-Host "Thinking proxy build complete."
