# Repository Guidelines

## Project Structure & Module Organization
- `src/VibeProxy.WinUI/`: WinUI 3 app. UI in `Views/`, state in `ViewModels/`, app services in `Services/`, helper utilities in `Helpers/`, assets in `Assets/`.
- `src/VibeProxy.WinUI/Resources/`: Shipped app resources. `Resources/config.yaml` is the base backend config. `Resources/bin/` is where `thinking-proxy.exe` and `cli-proxy-api-plus.exe` live during runtime/CI.
- `src/VibeProxy.Core/`: Shared business logic and models. Services include server lifecycle, config merging, auth, and settings storage.
- `src/VibeProxy.Proxy/`: Go "thinking proxy" server (defaults to 8317 -> 8318).
- `config/`: Source-of-truth `config/config.yaml` used by CLIProxyAPIPlus. Keep in sync with `src/VibeProxy.WinUI/Resources/config.yaml`.
- `scripts/`: Build helpers for Go binaries and packaging; `ensure-binaries.ps1` is called by Debug builds.
- `assets/` and `.github/`: design assets and CI workflows.

## Build, Test, and Development Commands
- `.\scripts\build-all.ps1`: builds the thinking proxy and CLIProxyAPIPlus into `src\VibeProxy.WinUI\Resources\bin`.
- `.\scripts\build-thinking-proxy.ps1`: builds `thinking-proxy.exe` from `src\VibeProxy.Proxy` (Go required).
- `.\scripts\build-cli-proxy.ps1`: clones `CLIProxyAPIPlus` into `%TEMP%` (default) and builds `cli-proxy-api-plus.exe` (Git + Go required).
- `.\scripts\ensure-binaries.ps1`: used by Debug builds to populate `Resources\bin` if missing. Skip with `-p:VibeProxyEnsureBinaries=false`.
- `dotnet build .\src\VibeProxy.WinUI\VibeProxy.WinUI.csproj -c Release`: builds the WinUI app.
- `dotnet build .\src\VibeProxy.WinUI\VibeProxy.WinUI.csproj -c Release -p:GenerateAppxPackageOnBuild=true`: produces MSIX packages.
- Visual Studio: open `VibeProxy.sln`, set `VibeProxy.WinUI` as startup, choose `x64`, press `F5`.

## Coding Style & Naming Conventions
- C#: file-scoped namespaces, PascalCase for types/methods, `_camelCase` for private fields, 4-space indentation.
- Go: run `gofmt`; keep proxy code in `src/VibeProxy.Proxy`.

## Testing Guidelines
- There is no automated test suite in this repo today.
- If adding tests, use standard conventions: Go tests in `src\VibeProxy.Proxy` with `_test.go` files (`go test ./...`), and .NET tests in a `tests\` project like `VibeProxy.Core.Tests` (`dotnet test`).

## Commit & Pull Request Guidelines
- Commit history favors short, direct subjects (e.g., "Fix main window startup..."). Keep to one line, imperative voice, <70 characters. No enforced conventional-commit prefixes.
- PRs should include a clear summary, validation steps, and screenshots/GIFs for UI changes. Link related issues when available.

## Security & Configuration Tips
- Base config lives in `config\config.yaml`; local overrides should be named `config\local*` (ignored by git).
- Runtime auth/config files live in `%USERPROFILE%\.cli-proxy-api` (includes `merged-config.yaml` and `proxy-config.json`).
- Settings are stored in `%LOCALAPPDATA%\VibeProxy\settings.json`.
- Signing assets (`*.pfx`, `*.snk`) must never be committed; CI expects secrets via GitHub Actions.
- Runtime ports: thinking proxy `http://localhost:8317`, backend `http://localhost:8318`.
- Avoid committing build outputs (`bin\`, `obj\`, `*.msix`, `*.exe`).
