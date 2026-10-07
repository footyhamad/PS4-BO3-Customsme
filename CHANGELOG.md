# PS4 BO3 Customsme — Fork Changelog

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
