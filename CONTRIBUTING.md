CONTRIBUTING
============

Thanks for contributing to Zer0Talk! This document explains the preferred process for filing issues, proposing changes, and submitting pull requests so maintainers can review your work quickly.

1. Report issues first
---------------------
- Use the Issue templates in `.github/ISSUE_TEMPLATE/` to report bugs or request features.
- Provide reproduction steps, environment details, and small test files where applicable.

2. Development setup
---------------------
- We ask contributors to validate changes with both Debug and Release builds. From the repository root run:

```pwsh
dotnet build ./Zer0Talk.sln -c Debug
dotnet build ./Zer0Talk.sln -c Release
```

- If your change affects federation/relay code, run the provided smoke or reliability scripts in `./scripts/` as appropriate.

3. Tests
--------
- Add unit tests for new behavior. Run the test suite before submitting a PR.

4. Commit style
---------------
- Keep commits small and focused. Use clear subject lines and explain why a change was made in the body.
- Update `CHANGELOG.md` with a short entry under the Unreleased section for user-visible changes.

5. Pull requests
----------------
- Open a PR against `main` (or the branch requested in an issue). Include a short description, motivation, and steps to test.
- Use the PR template to ensure checklist items are addressed.

6. Code review and CI
---------------------
- All PRs must pass CI and at least one approving review from a maintainer before merge.

7. Code of conduct
------------------
By contributing you agree to follow the project's Code of Conduct in `CODE_OF_CONDUCT.md`.

Thank you — maintainers
