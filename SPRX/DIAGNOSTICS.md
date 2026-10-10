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
- A heartbeat every ten seconds while the game's hooked frame tick is progressing, including uptime, executable base, map count and frame-tick counter.

## How to triage a crash

Open the log and inspect the final records. Find the latest `[ERROR]` or `[FATAL]`; the adjacent `[BUILD]`, `[DETOUR]`, `[MAPS]`, `[IMAGES]`, `[LUA]` and `[KBM]` records should tell you which signature or hook failed. A recent heartbeat means the frame tick was still running at that point. If it stops after a heartbeat, that is evidence the frame stopped advancing or the process terminated soon afterward.

This is persistent crash-triage logging, **not a native PS4 crash dump**. If the OS terminates the process without running module shutdown code, the SPRX cannot write a post-crash call stack. No other BO3 version is supported: failed 1.33 preflight must leave fixed-offset hooks and patches disabled.
