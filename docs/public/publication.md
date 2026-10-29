# Source publication boundary

This repository contains the Android receiver, Windows sender, tests, build definitions, Gradle wrapper, original app resources and sanitized documentation.

The following remain local and are excluded by `.gitignore`:

- SDK paths, IDE state, build products, downloaded tools and native FFmpeg distributions.
- `appsettings.json`, signing keys, credential files and environment overrides.
- Raw ADB/logcat/settings/property captures, screenshots, device identifiers and copied/decompiled vendor packages.
- Private workspace instructions, chronological project context and original diagnostic notes. Public setup and architecture notes are under `docs/public/`.
- Locally downloaded third-party TV applications and APKs.

The sender example has an empty host. Copy it to the ignored `appsettings.json` and set your own TV address, or pass `--host`. Existing local configuration is intentionally preserved. Never force-add ignored device captures or configuration.

Before publishing a later change, inspect the exact staged content:

```powershell
git status --short
git diff --cached --stat
git diff --cached
gitleaks git --pre-commit --staged --redact --no-banner .
```

Gitleaks runs locally and is an optional review tool, not an application dependency. Also review screenshots, binary files, commit author metadata, private addresses, account names and absolute personal paths; credential scanners do not cover every kind of private data. A clean scan is evidence, not a guarantee about future files.

Do not include third-party binary distributions without reviewing their redistribution terms. No project license has been selected in this initial source preparation; dependency and tool licenses continue to apply.
