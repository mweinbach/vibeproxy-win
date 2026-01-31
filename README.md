# VibeProxy (Windows)

Native Windows companion for VibeProxy with feature parity to the macOS app.

## Features
- System tray app with start/stop, copy URL, settings window, and update link
- OAuth flows for Claude Code, Codex, Gemini, Qwen, Antigravity, GitHub Copilot
- Z.AI GLM API key support
- Vercel AI Gateway toggle for Claude
- Thinking Proxy (Go) on port 8317 + CLIProxyAPIPlus backend on port 8318
- Auth file monitoring in `%USERPROFILE%\.cli-proxy-api`

## Requirements
- Windows 10+ (Desktop)
- .NET 8/10 SDK
- Go 1.22+
- Git

## Repo Layout
```
src/
  VibeProxy.WinUI   # WinUI 3 app
  VibeProxy.Core    # Shared logic
  VibeProxy.Proxy   # Go thinking proxy
config/
  config.yaml       # base CLIProxyAPIPlus config
scripts/
  build-all.ps1     # builds proxy + backend into app resources
```

## Build (local)
1. Build the Go thinking proxy and CLIProxyAPIPlus binaries:
   ```
   .\scripts\build-all.ps1
   ```
2. Build the WinUI app:
   ```
   dotnet build .\src\VibeProxy.WinUI\VibeProxy.WinUI.csproj -c Release
   ```

## Packaging (MSIX)
- Configure the publisher in `src/VibeProxy.WinUI/Package.appxmanifest` to match your signing cert.
- Release URLs currently point to `https://github.com/mweinbach/vibeproxy-win` and can be adjusted if the repo moves.
- Build with MSIX enabled:
  ```
  dotnet build .\src\VibeProxy.WinUI\VibeProxy.WinUI.csproj -c Release -p:GenerateAppxPackageOnBuild=true
  ```

## CI / Releases
The workflow in `.github/workflows/windows-release.yml`:
- Builds the Go proxy and CLIProxyAPIPlus from source
- Creates a signed MSIX + appinstaller
- Uploads artifacts to GitHub Releases

### Required Secrets
- `PFX_BASE64` — base64-encoded signing certificate
- `PFX_PASSWORD` — password for the PFX file

## Notes
- Thinking Proxy listens on `http://localhost:8317`
- Backend listens on `http://localhost:8318`
- Auth files are stored in `%USERPROFILE%\.cli-proxy-api`
