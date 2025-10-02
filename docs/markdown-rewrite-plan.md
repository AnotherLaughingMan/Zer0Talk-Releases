# Markdown Renderer: Rewrite Plan (Deferred)

Status: STUBBED in `Controls/Markdown/ZTalkMarkdownViewer.cs` — plain-text rendering only.

Goal
- Replace the current stub with a robust, secure, and performant markdown renderer that:
  - Uses Markdig for parsing
  - Produces fresh Avalonia UI elements per message (no reuse of visual elements)
  - Avoids visual-parent conflicts and preserves autoscroll and input responsiveness
  - Is feature-flagged for incremental rollout

Constraints
- Must never reuse UI elements between messages
- Must avoid long-running work on UI thread
- Must not load arbitrary HTML or external resources by default (security)
- Must preserve app responsiveness (no selection/freezing)

Phased Implementation

Phase 1 — Safe Minimal Renderer
- Implement Markdig pipeline configured with only safe features (DisableHtml)
- Render only: paragraphs, headings, inline emphasis (bold/italic), inline code, fenced code blocks (monospace), links as text, blockquotes
- Render each message by constructing new Avalonia controls per block (TextBlock, Border)
- Feature flag behind `UseMarkdig` (disabled by default)
- Unit tests for parsing -> control outputs for several sample messages
- Acceptance: no visual-parent errors; autoscroll intact; no input freezes

Phase 2 — Extended Inline Support
- Implement nested emphasis, lists, tables, autolinks
- Add spoiler support and a safe way to reveal spoilers (user action)
- Add link previews as optional asynchronous operations (do not block rendering)

Phase 3 — Performance and UX
- Add optional lightweight cache of rendered *data* (not UI elements) to avoid reparsing identical messages
- Offload parsing to background threads and marshal small, final UI creation tasks onto UI thread
- Add telemetry/logging for parse failures and rendering time

Phase 4 — Security Harden & Polishing
- Ensure no external image loading by default; replace with placeholders and explicit user action
- Add sanitization layers for any user-supplied HTML (if enabled)
- Accessibility: ensure semantic text, tooltips for code language, and keyboard-reveal for spoilers

Testing and CI
- Add unit tests for parsing and control-generation logic (Markdig -> serialized representation)
- Add integration/UI tests to ensure autoscroll and input selection remain responsive when rendering messages

Rollout Strategy
- Merge behind `UseMarkdig` feature flag set to `false` by default
- Enable for internal QA build, then staged enablement for beta users

Notes
- DO NOT store or cache Avalonia controls. Cache only serializable representations (e.g., simplified AST or rendered HTML string for reuse)

Owner & Timeline
- Owner: [assign later]
- Timeline: Phase 1 implementation (2-3 sprints) depending on priority and QA coverage

"TODO": Reintroduce Markdig and renderer safely following the phased plan above. Ensure team review before enabling feature flag for users.
