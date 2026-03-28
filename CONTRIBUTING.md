**Contributing to Zer0Talk**

Thanks — we appreciate contributions. This file gives a quick path for reporting issues, developing, testing, and submitting PRs.

**Quick Start**
- **Report first:** Use the issue templates in `.github/ISSUE_TEMPLATE/` (bug or feature).
- **Build & test:** Validate locally in both Debug and Release before opening a PR.
- **Follow the PR checklist** in `.github/PULL_REQUEST_TEMPLATE.md`.

**Report an Issue**
- **Use template:** Choose `Bug report` or `Feature request` under Issues → New.
- **Include:** concise description, exact steps to reproduce, expected vs actual behavior, logs, OS/version, and screenshots when helpful.

**Development Setup**
- Clone the repo and run both builds from the repo root:

```pwsh
dotnet build ./Zer0Talk.sln -c Debug
dotnet build ./Zer0Talk.sln -c Release
```

- If you change relay/federation code, run the smoke or reliability scripts in `./scripts/` as needed:

```pwsh
# example
pwsh .\scripts\federation_smoke_check.ps1
```

**Testing**
- Add unit tests for new behavior and run the test suite locally before opening a PR.
- Where available, include short instructions to reproduce tests in your PR description.

**Commit Guidelines**
- Keep commits small and focused. Use imperative subject lines (e.g., "Add X feature").
- In the commit body explain *why* the change was made, not just *what* changed.
- Update `CHANGELOG.md` under the Unreleased section for user‑facing changes.

**Pull Requests**
- Open a PR against `main` (unless an issue requests a different branch).
- In your PR include: summary, motivation, test steps, and any screenshots or logs.
- Complete the PR checklist in `.github/PULL_REQUEST_TEMPLATE.md` before requesting review.

**Code Review & CI**
- All PRs must pass CI and receive at least one maintainer approval before merge.
- Address review comments promptly and re-run the tests where applicable.

**Security / Sensitive Code**
- For encryption or protocol changes, ping maintainers and include design notes. Refer to `docs/RELAY-FIX-PLAN.md` when relevant.

**Code of Conduct**
- By contributing you agree to follow the project's `CODE_OF_CONDUCT.md`.

**Need Help?**
- For repo access or admin actions, contact a maintainer listed in `CODEOWNERS` or open an issue tagged `area/repo`.

Thank you — the maintainers
