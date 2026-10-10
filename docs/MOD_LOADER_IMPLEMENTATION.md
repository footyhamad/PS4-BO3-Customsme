# BO3 PC Mod Loader — implementation contract

Target game build: **Black Ops III PS4 v1.33**. The working GoldHEN PRX and existing custom-map path are the compatibility baseline. This document distinguishes discovery, conversion, and runtime activation; passing one stage must never be reported as passing the others.

## Surfaces

### Windows FF Porter

The Mod Loader discovery window enumerates `mods/<name>/zone/*.ff` and `usermaps/<name>/zone/*.ff` beneath the selected PC game folder. It can queue candidate folders through the existing conversion pipeline. A candidate is **not** considered PS4-compatible merely because fastfiles were found.

For each candidate, the eventual compatibility report must record:
- discovered fastfiles and their relationships;
- loose files, XPAKs, sound banks, videos, language-specific assets, and script-related files;
- donor/reference requirements and conversion outputs;
- unsupported or unverified content, with the relevant file path and diagnostic reason;
- a stable package ID, output directory, file hashes, and conversion-tool version.

### In-game PS4 Mod Loader

The PRX needs a separate runtime catalog and an in-game menu. Its package states must be explicit: `discovered`, `converted`, `ready`, `enabled`, `loaded`, or `failed`. The menu must not label a mod as loaded just because its folder exists or its toggle was saved.

The runtime must preserve the current map loader and avoid loading unvalidated files into BO3. Package activation must only be implemented once the v1.33 zone/asset load path and dependency/priority rules are verified. Reuse the existing guarded hooks and signature checks; do not add guessed offsets or assume PC zone fastfiles are directly readable by the PS4 build.

## Package manifest contract

A converted package should carry a UTF-8 `mod.json` manifest with a schema version, stable ID, display name, target build, conversion status, required packages, ordered zone files, and optional sidecar assets. Paths must be relative to the package root, normalized, and rejected if they escape that root. Unknown schema versions and missing dependencies fail closed.

Example shape (illustrative only; this is not yet a runtime-supported manifest):

```json
{
  "schema": 1,
  "id": "example_mod",
  "name": "Example Mod",
  "target": "bo3-ps4-1.33",
  "conversion": "ready",
  "requires": [],
  "zones": [
    { "file": "zone/example_mod.ff", "role": "mod", "priority": 100 }
  ],
  "assets": []
}
```

## Required verification before claiming support

1. Windows scan tests: missing directories, empty folders, inaccessible paths, duplicate names, multiple zones, and mixed map/mod folders.
2. Conversion tests: PC fastfiles with and without companion assets, missing donors, script-heavy mods, and malformed packages.
3. Console tests on BO3 PS4 v1.33: catalog scan, menu open/close, enable/disable persistence, missing-file handling, dependency failure, zone load order, and recovery after a failed package.
4. Every runtime stage must write a timestamped diagnostic record to the existing diagnostics log. Failures must identify the package and file; they must not silently alter the existing map loader.

## Current status

- Windows: first-pass candidate discovery and queueing UI added. Its status is intentionally conservative and does not claim compatibility.
- PS4: runtime catalog/menu and safe activation are still pending. Existing map hooks are not a generic PC mod loader, so arbitrary mod activation must not be claimed until those pieces are implemented and tested on-console.
