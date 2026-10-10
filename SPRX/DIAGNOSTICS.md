# PS4 BO3 Customs SPRX Diagnostics

## Log files

The always-on log is `/data/BO3-Customs/diagnostics.log`. If that path is unavailable, logging falls back to `/data/bo3customs-diagnostics.log`. These files are append-only: starting another game session does **not** truncate this diagnostic history. Each event opens the file in append mode, completes partial writes, and closes the file immediately to keep recent evidence on disk.

Debug builds still create the older `/data/BO3-Customs/log.txt` for extra stack/error details. That legacy debug file is separate from `diagnostics.log`.

## What is recorded

- Version/build date, log-path initialization and BO3 1.33 signature preflight.
- Signature name, offset, expected bytes and observed bytes when build validation fails.
- Hook attach attempts and outcomes; target, hook and trampoline addresses; patch size; decoder/relocation errors; memory-protection and write-verification failures; rollback attempts.
- Map discovery totals from `/data/BO3-Customs` and each removable storage root, elapsed scan time, package/file/map/movie counts.
- Lua/UI patch outcomes, image hooks, keyboard/mouse signatures and mouse initialization, script failures, zone events and level transitions.
- An independent monitor thread emits a heartbeat every ten seconds, including startup phase, uptime, executable base, last stable map-count snapshot, frame-hook status and frame-call count; it can report when frame calls stop advancing.
- Module-load diagnostics before game detection, one-second wait progress, initialization-thread return codes, storage probe/mount results, subsystem start/end breadcrumbs and detailed main-frame-hook signature failures.

## How to triage a crash

Open the log and inspect the final records. Find the latest `[ERROR]` or `[FATAL]`; the adjacent `[BUILD]`, `[DETOUR]`, `[MAPS]`, `[IMAGES]`, `[LUA]` and `[KBM]` records should tell you which signature or hook failed. The heartbeat is produced by a separate monitor thread. `frame_state=stalled/no-frame-progress` means the main frame hook had been installed and frame calls then stopped advancing for at least ten seconds. If every heartbeat stops, the process may have terminated or the diagnostics thread itself may have stopped.

This is persistent crash-triage logging, **not a native PS4 crash dump**. If the OS terminates the process without running module shutdown code, the SPRX cannot write a post-crash call stack. No other BO3 version is supported: failed 1.33 preflight must leave fixed-offset hooks and patches disabled.
