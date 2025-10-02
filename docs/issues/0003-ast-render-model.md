Title: AST → Safe serializable representation (render model)

Status: Open
Priority: High
Estimate: 2 days
Labels: markdown, perf, design

Description

Create a minimal, serializable 'render-model' that represents a safe subset of markdown blocks and inlines (Paragraph, Heading, Emphasis, InlineCode, FencedCode, Link, Blockquote, List). Implement a mapper from Markdig's AST to this render-model so the UI renderer can consume it without holding Markdig-specific types.

Acceptance Criteria

- Render-model classes defined and JSON-serializable
- Mapping from MarkdownDocument → RenderModel implemented
- Unit tests assert mapping correctness for sample markdown inputs

Notes

RenderModel is the canonical intermediate data used for caching and background parsing. Do not store UI controls in the model.