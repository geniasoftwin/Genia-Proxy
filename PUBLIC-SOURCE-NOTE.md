# Public source sanitization note

This tree is derived from the validated GeniaProxy 4.4.0 Final Stable Direct Bridge source package and is prepared for publication.

For repository safety:

- project-specific production/validation IP addresses were removed from browser defaults;
- historical real server IPs in documentation and unit tests were replaced with RFC 5737 TEST-NET addresses;
- bundled `sing-box.exe`, `xray.exe`, and `wintun.dll` are not committed;
- engine acquisition is reproducible and hash-pinned through `Prepare-Engines.ps1`;
- no production profile, UUID, password, token, private key, certificate private material, or real proxy access URI is intentionally included.

These public-source substitutions do not represent deployable server endpoints. Users must supply their own lawful server/profile configuration.

## Package manifest scope

`PUBLIC-SOURCE-PACKAGE-MANIFEST.sha256` records the sanitized package as it existed before GitHub import. It is kept for provenance only and must not be interpreted as a checksum manifest for the current repository tree. The import deliberately preserved repository-owned root files and normalized trailing whitespace in a small set of source/UI files.
