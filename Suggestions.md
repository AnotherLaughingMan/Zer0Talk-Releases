# Feature Improvement Suggestions

Below is a curated list of larger feature opportunities observed during a quick audit of the current codebase. Each item explains the motivation, ties back to the existing implementation, and outlines a concrete direction for future work.

## 1. Conversation Search, Filters, and Smart Jumping
The current `MainWindowViewModel` does not track any free-text search term or filter state, and messages are only surfaced through the `Messages` observable collection. Users cannot quickly locate prior conversations, jump to particular dates, or filter by delivery state (e.g., unread or pending). Introducing a `SearchQuery` property backed by a lightweight index (even a simple in-memory scan with highlighting) would dramatically improve usability. Pair that with UI affordances such as "Jump to next unread" buttons and delivery-status filters to make large conversation histories manageable.

## 2. Rich Text Rendering, Markdown, and Link Previews
`Models/Message.cs` still carries a `TODO` for Markdown and URL rendering. Right now the pipeline treats `Message.Content` as a plain string, so URLs or emphasis markers render as literal text. Wiring up a Markdown renderer (Avalonia supports custom `FlowDocument`-style controls or third-party Markdown views) would unlock bold/italic formatting and allow inline code snippets—critical for technical users. On top of that, a small metadata probe service could fetch OpenGraph information for links and display thumbnails or titles directly in the chat bubble.

## 3. Attachments, Inline Media, and File Transfers
Messages are constrained to text-only payloads; there is no attachment model, thumbnail pipeline, or storage abstraction for blobs. Enhancing `Message` with attachment metadata (content type, encrypted blob reference, size) and adding a `FileTransferService` would open the door to screenshots, voice notes, and documents. The `OutboxService` already manages pending sends, so extending it to enqueue file uploads with progress and retry logic would align with existing patterns.

## 4. Message Reactions, Quick Replies, and Threading
Every message currently exposes only delivery status. Social cues like 👍, 😂, or ✅ reactions—and even lightweight reply threads—are missing. Adding a `Reactions` collection to `Message` plus a small reaction aggregation overlay in `MainWindow.axaml` would reduce clutter when acknowledging messages. For power users, inline replies (with quoted previews) let them respond to earlier context without leaving the current scroll position.

## 5. Desktop Notifications, Do-Not-Disturb, and Focus Rules
There is no centralized notification service. Incoming messages while the window is minimized quietly update the list with no OS-level toast or summary badge. Implementing a cross-platform notification abstraction (Windows toast, macOS notification center on future ports) alongside per-contact mute windows and Focus Assist integration would make the app feel alive even when the window is hidden. Combine this with the existing `PresenceRefreshService` to automatically respect DND modes.

## 6. Multi-Device Sync, Backups, and Recovery Keys
All data resides locally in encrypted containers (`Containers/MessageContainer.cs`, `SettingsService`). If a device is lost, the user loses identity keys, contacts, and history. Providing optional multi-device replication (perhaps via peer-to-peer backup or exportable recovery bundles protected by the passphrase) would close a critical reliability gap. Even a manual "Export secure backup" feature that packages contacts, keys, and recent messages into a passphrase-locked archive would be a big win.

## 7. Retention, Purge Controls, and Evidence Locker Mode
While there is a `RetentionService`, the app lacks human-facing controls for configuring message retention policies. A settings panel could expose auto-delete windows (e.g., 24 hours, 7 days, keep forever) and a one-click "burn conversation" workflow. Advanced users might want an "evidence locker" mode that pins certain conversations from deletion while everything else auto-purges—leveraging the existing `MessagePurgeSummary` structures and giving the retention logic clear UX bindings.

## 8. Accessibility, Localization, and Theming Enhancements
The UI presently offers a Dark and Light theme swap, but there are no high-contrast palettes, screen reader landmarks, or localization scaffolding. Adding `AutomationProperties` to key controls, ensuring keyboard navigation for hover-only menus (like `MessageHoverBar`), and extracting user-facing strings into resx files would make the app accessible and ready for translation. A theme extension allowing larger font presets or dyslexia-friendly fonts would further broaden usability.

## 9. Security Hardening: Contact Verification and Trust Flows
Trust is binary today via `Contacts.SetTrusted`, but there is little ceremony around verifying a peer. Consider adding an interactive verification flow (QR code or key fingerprint exchange) with explicit audit logs stored via `EventHub`. Integrate ephemeral session warnings when a peer rotates keys unexpectedly (there are hooks in `NetworkService` such as `_handshakePeerKeys`). Supplement this with optional two-factor unlock for the local vault and alerts when NAT traversal exposes ports unexpectedly.

## 10. Observability Dashboard and Log Viewer
Diagnostics currently require spelunking through `bin/<Config>/net9.0/logs`. Surface these insights in-app: a dedicated "Diagnostics" tab can stream recent log lines, graph NAT status, and summarize peer connectivity. Leveraging `EventHub` to publish structured diagnostics notifications would help support staff reproduce issues without digging into the filesystem.

## 11. Automated Testing, CI Guardrails, and Regression Suites
The solution contains no unit or integration tests, making it difficult to ensure network and messaging flows stay intact. Establishing a test project with mocks for `NetworkService`, loopback peer scenarios, and retention logic would allow confident refactoring. Pair this with simple GitHub Actions or Azure DevOps pipelines that run `dotnet test`, `dotnet format`, and the publishing scripts in dry-run mode to catch regressions before manual QA.

## 12. Plugin-Ready Simulation Sandbox
`MainWindowViewModel` exposes simulated contacts controlled via commands, but the simulation layer is tightly coupled to the main VM. Abstracting the simulator behind an interface (e.g., `ISimulationScenario`) would let QA or community contributors script richer scenarios: bulk invites, message storms, presence flapping. Coupling this with telemetry capture would turn the simulation mode into a regression harness for future features like attachments or reactions.
