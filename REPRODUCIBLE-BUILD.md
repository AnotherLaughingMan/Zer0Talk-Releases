# Reproducible Build Guide

This guide is for people who want to rebuild Zer0Talk from source and verify, as closely as the current pipeline allows, that the resulting binaries match the code they inspected.

The short version:

- You can reproducibly rebuild the managed application outputs from a pinned source checkout and pinned .NET SDK.
- You cannot currently expect every shipped release artifact to be bit-for-bit identical across machines.
- The current release packaging intentionally introduces variability through timestamps, ZIP metadata, installer embedding, and code-signing steps.

That means Zer0Talk currently supports a practical rebuild-and-verify workflow, not a formal fully deterministic release pipeline.

---

## 1. What This Document Is Claiming

This document is intentionally narrow.

It is claiming that a careful reviewer can:

1. Check out a specific commit.
2. Build it with the expected SDK and platform.
3. Compare the locally produced binaries against the inspected source and published hashes.
4. Understand which parts of the release process are deterministic and which parts are not.

It is not claiming that every published Zer0Talk release artifact is currently reproducible byte-for-byte from source on arbitrary machines.

---

## 2. Current Reproducibility Boundary

### Closest to Reproducible

These steps are the closest thing to a reproducible build path in the current repository:

- clean checkout of a specific commit or tag
- pinned .NET SDK from `global.json`
- `dotnet restore`
- `dotnet build` or `dotnet publish` using the documented commands
- hashing the resulting `.exe` and `.dll` outputs locally

### Not Fully Reproducible Yet

These parts of the current release flow are intentionally variable and will break bit-for-bit equivalence:

- timestamped file names in release packaging scripts
- ZIP container metadata and archive creation timestamps
- installer packaging that embeds a generated ZIP payload
- Authenticode signing steps
- self-contained single-file bundling and compression outputs, which are more sensitive to toolchain and environment details
- NuGet restore is not locked with `packages.lock.json` today

Because of those factors, the safest verification target is the built program binaries and publish directory contents before signing, re-zipping, and installer wrapping.

---

## 3. Source of Truth

The current build assumptions come from these files:

- `global.json` pins the expected SDK line (`9.0.305` at time of writing)
- `Directory.Build.props` defines the shared app version and build-time version consistency checks
- `.github/workflows/quality-gate.yml` defines the CI build and test gate
- `.github/workflows/release-auto-update.yml` defines the release publish path used by the releases repository
- `scripts/build_debug_release_clean_lock.ps1`, `scripts/alpha-strike.ps1`, and `scripts/build-installme-lite.ps1` define local packaging and installer flows

If those files change, this guide should be updated with them.

---

## 4. Environment Requirements

For the closest possible rebuild match, use the same broad environment class as CI and release automation:

- Windows 10 or Windows 11 x64
- .NET SDK `9.0.305`
- PowerShell 7 (`pwsh`)
- Git

Recommended discipline:

- build on a clean machine or disposable VM
- avoid background tools that rewrite binaries or attach metadata
- do not use post-build signing tools unless you are intentionally reproducing a signing step
- do not build from a dirty worktree

---

## 5. Strict Rebuild Procedure

### Step 1: Clone the Exact Source

Use the exact commit or tag you want to inspect.

```powershell
git clone https://github.com/AnotherLaughingMan/Zer0Talk-Releases.git
cd .\Zer0Talk-Releases
git checkout <tag-or-commit>
git rev-parse HEAD
git status --short
```

`git status --short` should print nothing before you build.

### Step 2: Verify the SDK

The repository currently pins the SDK through `global.json`.

```powershell
dotnet --version
Get-Content .\global.json
```

For the strictest comparison, use `9.0.305` exactly.

If you have multiple SDKs installed, do not rely on a newer one just because `rollForward` permits it. For hyper-paranoid verification, install and use the exact pinned SDK.

### Step 3: Clear Old Outputs

```powershell
Remove-Item .\bin, .\obj, .\publish, .\TestResults -Recurse -Force -ErrorAction SilentlyContinue
git clean -fdx
```

Only run `git clean -fdx` if you are in a disposable or verified-clean checkout. It deletes untracked files.

### Step 4: Restore

```powershell
dotnet restore .\Zer0Talk.sln
```

Important limitation: the repository does not currently ship `packages.lock.json`, so restore determinism depends on the resolved package graph remaining stable upstream. That is acceptable for ordinary builds, but it is not the strongest possible reproducibility posture.

### Step 5: Build the Same Way CI Does

```powershell
dotnet build .\Zer0Talk.sln -c Debug --no-restore
dotnet build .\Zer0Talk.sln -c Release --no-restore
dotnet test .\Tests\Zer0Talk.Tests.csproj -c Debug --results-directory .\TestResults --logger "trx;LogFileName=tests.trx"
```

Those commands mirror the quality gate closely.

### Step 6: Publish Release Outputs for Comparison

For a release-style publish without the installer wrapper:

```powershell
dotnet publish .\Zer0Talk.csproj -c Release -r win-x64 -p:SelfContained=true -o .\publish\verify-win-x64-Release-sc --nologo
dotnet publish .\Zer0Talk.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -o .\publish\verify-win-x64-Release-single --nologo
```

If the relay server sources are present in the checkout you are verifying, publish them separately:

```powershell
dotnet publish .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj -c Release -r win-x64 -p:SelfContained=true -o .\publish\verify-relay-win-x64-Release-sc --nologo
```

---

## 6. What To Compare

### Best Comparison Targets

Prefer comparing:

- `Zer0Talk.exe`
- `Zer0Talk.dll`
- other published `.dll` files in the output directory
- file lists in the publish folder
- SHA-256 hashes of the individual binaries
- version metadata reported by the assemblies

Example:

```powershell
Get-FileHash .\publish\verify-win-x64-Release-sc\Zer0Talk.exe -Algorithm SHA256
Get-FileHash .\publish\verify-win-x64-Release-sc\Zer0Talk.dll -Algorithm SHA256
```

### What Not To Treat As a Strong Equivalence Signal

Do not expect exact equality for:

- the final installer `.exe`
- timestamped `.zip` names
- archive byte layout from `Compress-Archive`
- outputs after local re-signing

Those artifacts are useful for distribution, not as the strongest reproducibility checkpoint.

---

## 7. Release Pipeline Reality Check

The current automated release workflow does this:

1. resolves the release tag
2. publishes self-contained and single-file client outputs
3. zips those outputs with timestamped names
4. embeds the single-file ZIP into `InstallMe.Lite`
5. emits an installer executable with a timestamped name
6. generates an update manifest with SHA-256 for the installer

This is a sane release pipeline for shipping software.

It is not yet a canonical reproducible-build pipeline.

If you are auditing a release, the strongest current method is:

1. verify the source commit/tag
2. rebuild locally with the pinned SDK
3. compare local publish outputs and assembly metadata
4. compare published release hashes where available
5. treat signing and packaging as a separate trust layer

---

## 8. Known Gaps Preventing Full Bit-For-Bit Reproducibility

At the time of writing, these are the main blockers:

- no `packages.lock.json` checked in
- timestamp-based artifact naming in packaging scripts
- ZIP creation through `Compress-Archive` and `ZipFile.CreateFromDirectory`, both of which preserve variable metadata
- installer construction that embeds a generated ZIP payload
- signing workflow that changes the final binary bytes
- no documented canonical artifact manifest for pre-sign, pre-zip binary hashes

None of that means the software is unverifiable. It means the verification story is currently stronger at the binary-and-source level than at the final installer-byte level.

---

## 9. If You Want the Strongest Current Verification Path

Use this checklist:

1. Build from a clean checkout of the exact release commit.
2. Use the exact SDK pinned in `global.json`.
3. Run the same restore, build, test, and publish commands as CI.
4. Hash the published `Zer0Talk.exe` and `Zer0Talk.dll` outputs.
5. Compare version metadata against `Directory.Build.props` and the release notes.
6. Inspect the GitHub Actions workflow file used for that release.
7. Treat installer/signing differences as expected unless the project later publishes canonical pre-sign hashes.

---

## 10. If You Want Full Reproducible Releases in the Future

The project would need additional engineering work, likely including:

- enabling and validating a stricter deterministic build configuration for release artifacts
- checking in `packages.lock.json`
- publishing canonical hash manifests for pre-sign binaries
- removing timestamp-based naming from the canonical verification path
- standardizing ZIP creation with normalized metadata
- clearly separating reproducible unsigned artifacts from signed distribution artifacts

Until then, the honest claim is:

Zer0Talk supports source-auditable rebuilds and strong practical verification, but not a formally bit-for-bit reproducible release process end to end.

---

## 11. Related Documents

- `README.md`
- `SECURITY.md`
- `THREAT-MODEL.md`
- `DEVELOPER-BIBLE.md`
- `.github/workflows/quality-gate.yml`
- `.github/workflows/release-auto-update.yml`