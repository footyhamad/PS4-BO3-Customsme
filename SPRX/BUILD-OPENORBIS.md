# Build BO3 Customs for OpenOrbis and GoldHEN

Targets **PS4 Black Ops III 1.33 only**. There are two different loading interfaces and two artifacts; do not treat them as interchangeable.

- **BO3-Customs.prx**: GoldHEN Plugins system build. It exports the callbacks GoldHEN expects: plugin_load and plugin_unload. Use this artifact with GoldHEN's plugins.ini mechanism.
- **BO3-Customs.sprx**: generic OpenOrbis library build for a loader that invokes its module entrypoint directly. It is not the GoldHEN plugin callback build.

The original Visual Studio/Sony SDK project remains present.

## Ubuntu / Linux terminal

Install the host dependencies:

~~~bash
sudo apt update
sudo apt install -y clang-18 lld-18 make curl file binutils python3 git
~~~

Download and extract OpenOrbis v0.5.4, then point OO_PS4_TOOLCHAIN to the directory containing link.x, include/ and lib/:

~~~bash
mkdir -p "$HOME/OpenOrbis"
curl -fL --retry 3 "https://github.com/OpenOrbis/OpenOrbis-PS4-Toolchain/releases/download/v0.5.4/toolchain-llvm-18.tar.gz" -o /tmp/openorbis-toolchain.tar.gz
tar -xzf /tmp/openorbis-toolchain.tar.gz -C "$HOME/OpenOrbis"
find "$HOME/OpenOrbis" -name link.x -print
export OO_PS4_TOOLCHAIN="$HOME/OpenOrbis/OpenOrbis/PS4Toolchain"
~~~

Clone the pinned, open-source GoldHEN Plugins SDK revision for its PRX CRT:

~~~bash
git clone https://github.com/GoldHEN/GoldHEN_Plugins_SDK.git "$HOME/GoldHEN_Plugins_SDK"
git -C "$HOME/GoldHEN_Plugins_SDK" checkout bcea3c7ef01dac6d7f9f49ebf9e90fe66d86f5f7
export GOLDHEN_SDK="$HOME/GoldHEN_Plugins_SDK"
~~~

Build both variants from the repository root:

~~~bash
make -C SPRX clean all
make -C SPRX -f Makefile.goldhen clean all
~~~

Outputs:

- SPRX/build/openorbis/BO3-Customs.sprx (+ SHA-256)
- SPRX/build/goldhen/BO3-Customs.prx (+ SHA-256)

If the archive extracts under an extra directory, use the actual folder containing link.x for OO_PS4_TOOLCHAIN.

## GitHub Actions

.github/workflows/build-sprx.yml builds both targets. For the generic SPRX it checks module entrypoints and constructor-array bounds. For the GoldHEN PRX it also verifies plugin_load/plugin_unload in the dynamic symbol table and that the ELF entrypoint is _init. A successful build is still not proof of runtime compatibility; test on BO3 1.33.

## GoldHEN Plugins installation

Copy BO3-Customs.prx to /data/GoldHEN/plugins/BO3-Customs.prx and add this filename to /data/GoldHEN/plugins.ini using the format expected by your installed GoldHEN version. Enable GoldHEN Plugins and restart BO3. Do not use the generic .sprx as a substitute in GoldHEN's plugin list.

The mod does not safely detach every installed game hook. Its plugin unload callback refuses hot-unload after BO3 is detected; exit/restart BO3 before replacing or removing the plugin.

## OpenOrbis security/feature boundary

Both open-source builds omit libjbc.cpp kernel credential/memory helper because it is not compatible with OpenOrbis userland headers. Automatic USB/extended-drive sandbox mounts are disabled in these builds. Local custom map discovery under /data/BO3-Customs remains enabled. External roots are scanned only when they are already visible inside the process sandbox.

The OpenOrbis mouse header also does not provide every Sony SDK mouse prototype used by this project, so isolated compatibility declarations remain in openorbis_compat/.

## Diagnostics

Persistent log: /data/BO3-Customs/diagnostics.log, falling back to /data/bo3customs-diagnostics.log. The log records whether module_start ran, whether the GoldHEN plugin_load callback ran, constructor addresses, game detection, BO3 1.33 signature checks, hook-install results and heartbeat status. See SPRX/DIAGNOSTICS.md.
