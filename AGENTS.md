# Repository Guidelines

## Project Structure & Module Organization
- `src/VibeProxy.WinUI/`: WinUI 3 desktop app (UI, ViewModels, services, resources).
- `src/VibeProxy.Core/`: shared business logic and models used by the app.
- `src/VibeProxy.Proxy/`: Go "thinking proxy" server.
- `config/`: base YAML config files (see `config/config.yaml`).
- `scripts/`: build helpers for Go binaries and packaging.
- `assets/` and `.github/`: design assets and CI workflows.

## Build, Test, and Development Commands
- `.\scripts\build-all.ps1`: builds the Go proxy and CLIProxyAPIPlus into `src\VibeProxy.WinUI\Resources\bin`.
- `.\scripts\build-thinking-proxy.ps1` and `.\scripts\build-cli-proxy.ps1`: build components individually.
- `dotnet build .\src\VibeProxy.WinUI\VibeProxy.WinUI.csproj -c Release`: builds the WinUI app.
- `dotnet build .\src\VibeProxy.WinUI\VibeProxy.WinUI.csproj -c Release -p:GenerateAppxPackageOnBuild=true`: produces MSIX packages.
- Visual Studio: open `VibeProxy.sln`, set `VibeProxy.WinUI` as startup, choose `x64`, press `F5`. Debug builds run `scripts\ensure-binaries.ps1` to fetch missing binaries.

## Coding Style & Naming Conventions
- C#: file-scoped namespaces, PascalCase for types/methods, `_camelCase` for private fields, 4-space indentation.
- Go: run `gofmt`; follow idiomatic naming and keep proxy code in `src/VibeProxy.Proxy`.
- Keep resources/configs in `src\VibeProxy.WinUI\Resources` and `config\`; avoid committing build outputs (`bin\`, `obj\`, `*.msix`).

## Testing Guidelines
- There is no automated test suite in this repo today.
- If adding tests, use standard conventions: Go tests in `src\VibeProxy.Proxy` with `_test.go` files (`go test ./...`), and .NET tests in a `tests\` project like `VibeProxy.Core.Tests` (`dotnet test`).

## Commit & Pull Request Guidelines
- Commit history favors short, direct subjects (e.g., "Fix main window startup..."). Keep to one line, imperative voice, <70 characters. No enforced conventional-commit prefixes.
- PRs should include a clear summary, validation steps, and screenshots/GIFs for UI changes. Link related issues when available.

## Security & Configuration Tips
- Base config lives in `config\config.yaml`; local overrides should be named `config\local*` (ignored by git).
- Signing assets (`*.pfx`, `*.snk`) must never be committed; CI expects secrets via GitHub Actions.
- Runtime ports: thinking proxy `http://localhost:8317`, backend `http://localhost:8318`; auth files live in `%USERPROFILE%\.cli-proxy-api`.
