Title: Rewrite Markdown Renderer (Markdig-based, safe incremental rollout)

Status: Open
Priority: High
Estimate: 2-4 sprints (phase 1 minimal) — break into smaller tasks
Assignee: TBD
Labels: markdown, ux, security, perf, backlog

Summary

Users expect markdown in chat. The current implementation was stubbed out to preserve app stability. This issue tracks the complete rewrite of the markdown renderer so we can reintroduce markdown safely.

Goal

Replace the stubbed plain-text viewer with a robust, secure, and performant renderer that:
- Uses Markdig for parsing (with HTML disabled by default)
- Produces fresh Avalonia UI elements per message (never reuses controls)
- Avoids visual-parent conflicts and preserves autoscroll and input responsiveness
- Is feature-flagged (MainWindowViewModel.UseMarkdig) so rollout can be staged

Acceptance Criteria (Phase 1 - Minimal)

1. Markdig pipeline implemented (DisableHtml, safe extensions only)
2. Renderer produces correct visual output for: paragraphs, headings, bold/italic, inline code, fenced code blocks, links (non-clickable), and blockquotes
3. Renderer creates fresh controls per message; no visual-parent exceptions in logs
4. Input selection and typing remain responsive (no noticeable freezes) while messages are displayed or composed
5. Autoscroll behavior unaffected when new messages arrive
6. Feature flag present and defaults to false; QA can enable it for testing
7. Unit tests cover parse -> representation mapping for representative markdown samples
8. Integration tests exercise autoscroll + selection responsiveness while rendering messages

Phases

Phase 1 (Minimal safe renderer):
- Implement Markdig pipeline and a renderer that maps Markdig AST to fresh Avalonia controls per message
- Keep inline features minimal (bold/italic, inline code, links as text)
- Add feature flag and logging
- Add unit/integration tests

Phase 2 (Inline & features):
- Add nested emphasis, lists, tables, autolinks
- Implement spoiler support (user-reveal)
- Add safe link previews as asynchronous, non-blocking probes

Phase 3 (Performance & UX):
- Add a serializable cache of parsed/render data (NOT UI elements)
- Offload heavy parsing tasks to background workers and marshal minimal UI updates onto UI thread
- Add telemetry for rendering time and failures

Risks and Mitigations

- Visual-parent conflicts: mitigate by creating fresh UI elements for each render
- Input freeze: mitigate by keeping UI work minimal and by avoiding long-running tasks on UI thread
- Security (embedding remote resources): Block by default; require explicit user action to load external images

Notes

- Do not cache Avalonia Controls between messages; only cache plain serializable render data
- Rollout behind feature flag; enable internally first

References

- docs/markdown-rewrite-plan.md

Next actions

- Break Phase 1 into 3-5 tasks and create separate issues for implementation, tests, and QA
- Assign owners and schedule into upcoming sprint
