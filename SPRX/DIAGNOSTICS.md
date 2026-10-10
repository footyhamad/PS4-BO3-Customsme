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
- GoldHEN-target logs explicitly identify the module_start and plugin_load callback stages. The GoldHEN .prx target must expose plugin_load/plugin_unload through the dynamic symbol table; the generic .sprx uses module_start. Toasts are deliberately deferred out of module_start to avoid calling the notification API during loader startup.
- OpenOrbis executable-base fallback based on the BO3 1.33 title marker and notification API return codes. The generic SPRX sends its startup canary from the worker thread; the GoldHEN PRX sends its canary from `plugin_load`, after the loader's `module_start` stage. Constructor-array bounds and each constructor's before/after record identify early initialization failures. A detection-timeout notification is also sent when the console notification service accepts requests.

## How to triage a crash

Open the log and inspect the final records. Find the latest `[ERROR]` or `[FATAL]`; the adjacent `[BUILD]`, `[DETOUR]`, `[MAPS]`, `[IMAGES]`, `[LUA]` and `[KBM]` records should tell you which signature or hook failed. The heartbeat is produced by a separate monitor thread. `frame_state=stalled/no-frame-progress` means the main frame hook had been installed and frame calls then stopped advancing for at least ten seconds. If every heartbeat stops, the process may have terminated or the diagnostics thread itself may have stopped.

This is persistent crash-triage logging, **not a native PS4 crash dump**. If the OS terminates the process without running module shutdown code, the SPRX cannot write a post-crash call stack. No other BO3 version is supported: failed 1.33 preflight must leave fixed-offset hooks and patches disabled.


## OpenOrbis build limitation (Fork 1.2.1.17)

The OpenOrbis build intentionally omits the bundled `libjbc.cpp` kernel credential/memory helper. Automatic USB/extended-drive sandbox mounting is therefore disabled in that build and a warning is written at startup. Maps under the accessible local `/data/BO3-Customs` path remain supported. External roots are scanned only if they are already visible inside the process sandbox. The existing Sony SDK/Visual Studio project continues to use its original source list; test that path separately.


## OpenOrbis portability update (Fork 1.2.1.18)

The OpenOrbis path now reads virtual-query address/protection/name fields through a size-checked local view of the v0.5.4 ABI layout, avoiding compiler dependence on inconsistent member names. The local notification payload structure also has a unique name to avoid collisions with SDK declarations. These are compile-compatibility changes; successful compilation and console runtime behavior still need confirmation.


## OpenOrbis stat alias (Fork 1.2.1.19)

Added the missing compatibility alias from Sony SDK naming to OpenOrbis' `OrbisKernelStat`, used by custom image/file reads.


## OpenOrbis map-scan compatibility (Fork 1.2.1.20)

Added a directory-entry alias for OpenOrbis' FreeBSD-style records, supplied a portable directory-type fallback, replaced the unavailable `strnlen` use with a bounded loop, and corrected BO3 1.33 signature diagnostics to use the existing `Prologue::what` field.


## OpenOrbis dirent field correction (Fork 1.2.1.21)

Removed a compatibility macro that incorrectly rewrote `d_fileno` to `d_ino`. The OpenOrbis directory-entry type already exposes `d_fileno`, which is what the scanner uses.


## OpenOrbis dirent macro fix (Fork 1.2.1.22)

The OpenOrbis compatibility header now undefines a `d_fileno` macro inherited from its directory headers because it rewrote the native `struct dirent` member to an unavailable `d_ino` member.


## OpenOrbis CRT entrypoints (Fork 1.2.1.23)

The OpenOrbis linker includes `crtlib.o`, which also defines `module_start` and `module_stop`. The link now allows duplicate CRT entrypoint symbols so the project's entrypoints (listed before `crtlib.o`) remain selected. The project's `module_start` explicitly runs the OpenOrbis `__init_array_start` to `__init_array_end` constructors before startup, matching the initialization work that the CRT wrapper normally performs. Verify the resulting binary and runtime on a BO3 1.33 PS4.


## Keyboard/mouse binding settings (Fork 1.2.1.34)

The existing Mouse/KBM settings datasource now receives per-action key selectors for movement, jump, sprint, prone, interact, reload, attack/aim, melee, weapon switching, inventory, grenades/tactical, specialist, weapon slots, scoreboard and pause. Changed key selections are reflected immediately in the runtime key table and persisted to `/data/BO3-Customs/kbm.cfg`. The key choices exclude Escape, the console key, backspace, uppercase duplicate key codes, and controller button codes so settings/menu navigation is not overwritten. If the selectors do not appear, confirm the console package's `ui_scripts/mouse.lua` is installed and inspect the `[KBM-UI]` record in `diagnostics.log`.


## Keyboard and mouse input triage (Fork 1.2.1.35)

The `[INPUT]` records are intentionally bounded to the first 80 non-repeat key events per module session. Each record identifies the raw and normalized key, mapped command, whether the game is in menu mode, and the last dispatch stage reached. If a crash occurs during a key event, inspect the last `key begin` / `stage=before-KeyEvent` lines. `[MOUSE]` records show read errors and aggregated button transitions. `[KBM] config loaded` reports how many key mappings were active.
