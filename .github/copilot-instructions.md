# Copilot Instructions for P2PTalk

## Project Architecture
- **Avalonia UI (.NET 9 desktop app):** MVVM pattern with Views (`Views/`), ViewModels (`ViewModels/`), and Models (`Models/`).
- **Services:** Core logic is in `Services/` (e.g., `LockService`, `SettingsService`, `IdentityService`, `NetworkService`, `EventHub`, `ThemeService`).
- **Containers:** Data grouping and persistence in `Containers/` (e.g., `MessageContainer`, `OutboxContainer`).
- **Utilities:** Converters and helpers in `Utilities/` (e.g., `UidToAvatarConverter`, value converters for XAML bindings).
- **Assets:** Icons and sounds in `Assets/`.
- **Styles:** Theme and palette overrides in `Styles/` (see `ThemeSovereignty*.axaml`, `DarkThemeOverrides.axaml`).

## Build & Run
- **Standard build:**
  ```pwsh
  dotnet build .\P2PTalk.csproj
  dotnet run --project .\P2PTalk.csproj
  ```
- **Scripted builds:**
  - Use PowerShell scripts in `scripts/` for advanced publish, checkpoint, and cleanup:
    - `scripts/alpha-strike.ps1` (multi-variant publish)
    - `scripts/publish-debug.ps1` (Debug/Release zip publish)
    - `scripts/checkpoint-build.ps1` (checkpointed build)
  - Example:
    ```pwsh
    pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/publish-debug.ps1 -Configuration Debug -Rid win-x64 -Single -KeepZips 2
    ```
- **Logs:**
  - Runtime logs are in `bin/Debug/net9.0/logs/` (e.g., `error.log`, `startup.log`).
  - Prune large logs regularly for error tracking speed.

## Key Patterns & Conventions
- **Lock/Unlock:**
  - `LockService` manages overlay, blur, and unlock gating. Unlock flow reuses MainWindow instance and restores `ShutdownMode`.
  - Passphrase and DPAPI sidecar logic in `SettingsService`.
- **Contacts List:**
  - Discord-style grouping (Online/Offline) via DataTemplates in `MainWindow.axaml`.
  - Custom converters for avatars, presence, and verification.
- **Theme:**
  - ThemeService swaps palettes at runtime; theme overrides in `Styles/`.
- **Error Handling:**
  - Use async logging to `logs/` for all startup and runtime errors.
  - Check `error.log` after unlock or build failures.
- **Resource Registration:**
  - Converters must be registered in `App.axaml` or `Window.Resources` for DataTemplates to resolve.
- **Settings:**
  - Encrypted settings at `%AppData%\Roaming\P2PTalk\settings.p2e`.

## Integration & External
- **No external DB:** All data is local, encrypted, or in containers.
- **No web API:** Peer-to-peer networking via `NetworkService` and `PeerManager`.

## Examples
- **Add a new Service:** Place in `Services/`, inject via `AppServices`, and wire up events in `App.axaml.cs`.
- **Add a new View:** Create `.axaml` in `Views/`, ViewModel in `ViewModels/`, and register DataTemplates in the parent window.

---
If any conventions or workflows are unclear, ask for clarification or examples from the user before proceeding.
