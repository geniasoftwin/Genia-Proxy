# GeniaProxy 4.4.0 Source Import

This document defines the first public-source import procedure. The repository is public, so a credential that reaches Git history must be treated as exposed even if it is deleted in a later commit.

## Import branch

The initial import is staged in:

`import/4.4.0-source`

Do not import the source directly into `main`.

## Before copying files

Keep the original 4.4.0 source package outside the repository and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-SourcePreflight.ps1 -Path "D:\path\to\GeniaProxy_4.4.0_Source"
```

The scanner intentionally reports only the file, line number, and rule. It does not echo the matched secret value.

Every finding must be manually reviewed.

## Values that must not enter Git history

Remove or replace with explicit placeholders:

- production server IP addresses and private hostnames;
- VLESS/client UUIDs used for real access;
- REALITY private keys and other private key material;
- passwords, tokens, API keys, SSH credentials;
- TLS private keys, PFX/P12/JKS/keystore files;
- real proxy access URIs;
- production profile files;
- diagnostic logs containing credentials or private endpoints.

Use documentation-only values such as `203.0.113.10`, `example.com`, `<REDACTED_UUID>`, and `<REDACTED_PRIVATE_KEY>` when an example is required.

## Files that should normally stay out of source control

Do not copy:

- `bin/`, `obj/`, `output/`, `publish/`, logs, runtime data, and temporary files;
- portable release bundles;
- locally generated profiles;
- downloaded engine binaries when they can instead be fetched reproducibly;
- machine-specific IDE state.

The repository `.gitignore` blocks common cases, but it is not a security boundary.

## Engine binaries

Prefer source-controlled download/build scripts containing:

- exact upstream engine version;
- official download source;
- expected SHA-256 checksum;
- architecture;
- required build tags/options when applicable.

This makes releases reproducible without committing opaque runtime binaries.

## Commit procedure

After sanitizing the source:

```powershell
git switch import/4.4.0-source
# Copy the sanitized source tree into the repository.
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-SourcePreflight.ps1 -Path .
git status
git add -A
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-SourcePreflight.ps1 -Path .
git diff --cached --stat
git commit -m "feat: import GeniaProxy 4.4.0 stable source"
```

For additional local protection, install the repository hook once:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-PreCommitHook.ps1
```

## Review before merge

The import PR must verify:

- project/build files are present;
- source version identifies 4.4.0 correctly;
- no credentials or production profiles are included;
- build output is excluded;
- engine version pins/checksums are documented or migrated to reproducible scripts;
- a clean checkout can be built using documented prerequisites;
- stable 4.4.0 behavior has not been modified during the import.

Only after this review should the source be merged to `develop`. A release tag should be created only from a commit that actually reproduces the intended 4.4.0 source state.
