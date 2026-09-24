# Engine Refresh RC2 HF2 — Packaging provenance fix

HF2 changes packaging metadata only. Runtime networking, TUN/DNS, Control Plane, Direct Bridge, sing-box 1.14.1 and Xray 26.9.8 binaries are unchanged.

Fixes:
- publish `engine\xray.provenance.txt` as an external engine artifact;
- require the provenance file during MSBuild core checks;
- verify the published provenance contains the pinned official Xray 26.9.8 archive SHA-256 before creating the portable ZIP.

FileVersion remains 4.5.0.7.
