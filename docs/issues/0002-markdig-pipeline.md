Title: Implement Markdig pipeline (safe defaults)

Status: Open
Priority: High
Estimate: 1 day
Labels: markdown, infra, security

Description

Add Markdig as the parsing pipeline. Configure with safe defaults (DisableHtml) and a minimal trusted extension set. Expose a small API (ParseMarkdown) returning a Markdig MarkdownDocument for later mapping.

Acceptance Criteria

- Markdig package added to project and restored
- Pipeline configured with HTML disabled and only safe extensions enabled
- ParseMarkdown(markdown) API implemented and returns MarkdownDocument
- Unit test validates parsing expected AST nodes for representative input (paragraphs, headings, code fences, inline emphasis)

Notes

This is a low-risk infrastructure change; no UI changes in this task.