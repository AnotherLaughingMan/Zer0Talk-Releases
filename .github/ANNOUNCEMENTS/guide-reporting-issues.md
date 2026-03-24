## Guide: How to Report Issues for Zer0Talk

Thank you for taking the time to report an issue — clear reports help us diagnose and fix problems faster.

Please follow this checklist when opening a GitHub Issue for Zer0Talk:

1. Search existing issues first
   - Use the repository's Issues list and Discussions to ensure the problem hasn't already been reported.

2. Use a clear, descriptive title
   - Example: "Crash when opening settings after upgrade to 1.2.3 on Windows 11"

3. Provide environment details
   - Zer0Talk version (from `About` or `AppInfo.cs`), OS and version, and whether you used the Relay server.

4. Describe steps to reproduce (short, numbered)
   - Exact steps you took, from a fresh start when possible.
   - Include any configuration changes, plugins, or third-party software involved.

5. Include expected vs actual behavior
   - What you expected to happen and what actually happened.

6. Attach logs and error output (text only)
   - Copy relevant logs from `Logs/` and paste plain text into the issue (do NOT attach archives containing secrets).
   - If the app prints a stack trace or exception, paste it using fenced code blocks.

7. Do NOT attach images or private files
   - For privacy and security, avoid attaching screenshots or files with personal data. If an image is essential, provide a text description instead.

8. Provide a minimal test case if applicable
   - If your issue can be reproduced with a small sequence or sample file, include that as text or steps.

9. Add reproducibility and severity
   - How often does this happen? (always / sometimes / one-time)
   - How critical is the impact? (blocker / major / minor / cosmetic)

10. Optional: Developer notes
   - Any debugging steps you already tried, and any relevant config values.

Issue template (copy this into the issue body):

```
### Environment
- Zer0Talk version: 
- OS: 

### Steps to Reproduce
1.
2.
3.

### Expected Behavior

### Actual Behavior

### Logs / Stack Trace
```

Thank you — a good report helps us ship a fix faster.

*Zer0Talk Team*
