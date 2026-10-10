# PS4 BO3 Customsme — Fork Changelog

## Fork 1.2.1.14 (OpenOrbis SPRX build pipeline; desktop package remains 1.2.1.4)
- Added an OpenOrbis PS4 Toolchain build path using Clang/LLD and the open-source create-fself utility, plus local Makefile instructions and a GitHub Actions build.
- Added isolated SDK compatibility headers for kernel, mouse, IME, sysmodule and user-service declarations; kept the existing Sony SDK/Visual Studio path intact.
- Added separate SPRX artifact/release publishing so SPRX builds do not overwrite the desktop tool release. OpenOrbis compilation and PS4 runtime compatibility must be confirmed by the build/test results.

## Fork 1.2.1.13 (SPRX diagnostics portability; desktop package remains 1.2.1.4)
- Match the atomic frame-counter operand to the `uint64_t` type used by the PS4 SDK and publish the map-count snapshot atomically for the independent monitor.
- Make the storage probe log distinguish “PS5 marker found” from “marker not found” instead of treating an open failure as definitive PS4 identification.

## Fork 1.2.1.12 (SPRX startup and heartbeat diagnostics; desktop package remains 1.2.1.4)
- Initialize persistent diagnostics at module load, before waiting for BO3, and log detection timeouts, thread failures, storage-probe/mount results, subsystem stages, and the main frame-hook signature/outcome.
- Run the ten-second heartbeat from an independent monitor thread so it continues recording when the game's frame hook stalls; include a frame-stalled state when frame calls stop advancing.
- Record logger/monitor lifecycle and startup duration. Actual PS4 SDK compilation and console runtime testing are still required.

## Fork 1.2.1.11 (SPRX diagnostics; desktop package remains 1.2.1.4)
- Added an always-on append-only diagnostics log for Release and Debug builds at `/data/BO3-Customs/diagnostics.log`, with a fallback path and per-record writes/close.
- Added ten-second runtime heartbeats, detailed BO3 1.33 signature mismatch reporting, subsystem stage timing, per-root map-scan totals, and release-build event breadcrumbs.
- Added checked detour writes/memory protection, hook-specific outcomes, rollback attempts, and retryable hook registration after failed attachment.
- Gated fixed-offset logger, map-image, Lua/UI, keyboard/mouse and generic detour installation behind the BO3 1.33 signature preflight.
- Added `SPRX/DIAGNOSTICS.md` with log locations and crash-triage instructions. Actual PS4 SDK compilation and console runtime testing are still required.

## Fork 1.2.1.4
- Fixed rolling-release publishing so an existing fork-latest release is edited instead of incorrectly attempting to recreate it.
- Release publishing now checks native GitHub CLI exit codes and fails the build when release metadata or asset upload fails.

- Added visible updater progress to version checks as well as downloads, verification and restart preparation.
- Corrected the changelog structure from 1.2.1.2.

## Fork 1.2.1.2
- Added a dedicated updater overlay so checking, downloading, verifying and restarting visibly report their current state and progress.
- Kept updater progress separate from conversion UI so the existing queue, conversion controls and startup preparation flow are not affected.

## Fork 1.2.1.1
- Fixed existing installs being blocked by the full-window preparation overlay during startup.
- The preparation overlay now appears only on the first setup run and always clears in a finally block.

## Fork 1.2.1.0
- Fixed the updater flow so the restart prompt appears only after the new EXE is fully downloaded and SHA-256 verified.
- The confirmed restart now closes the app, replaces the old EXE in its original directory, and launches the new build.
- The updater stages the replacement beside the existing EXE so the final replacement uses the same drive.

## Fork 1.2.0.3
- Fixed the desktop build after adding the scan/test controls: restored the testing state field, imported System.Text.Json, and normalized XPAK enumeration.
- Synchronized the visible fork version with the desktop assembly version.

## Fork 1.2.0.1
- Fixed the desktop action-lock state used while scanning/testing.
- Added a CHANGELOG button to the desktop tool.

## Fork 1.2.0.0
- Added MAP pre-flight scanning.
- Added RETRY FAILED with cache-aware smart resume behavior.
- Added conversion profiles: Standard, Maximum Compatibility, Fast, and Debug.
- Added TEST OUTPUT for PS4 loader walks and XPAK verification.
- Added a conversion HISTORY view.
- Added a DIAGNOSTICS view for saved problems and warnings.
- Preserved the per-sound FLAC fallback that silences only undecodable entries.
- The port already exposes live conversion steps, scores, and problem details in the fidelity panel.

## Fork 1.1.0.0
- Normalized the fork to the four-part version scheme: Major.Minor.Patch.Micro.
- Added visible updater download progress.
