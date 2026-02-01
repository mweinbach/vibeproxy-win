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

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputPath = Join-Path $repoRoot $OutputDir
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
}

Write-Host "Building thinking proxy..."
Push-Location (Join-Path $repoRoot "src\\VibeProxy.Proxy")
& $goExe build -o (Join-Path $outputPath "thinking-proxy.exe")
Pop-Location

Write-Host "Thinking proxy build complete."
