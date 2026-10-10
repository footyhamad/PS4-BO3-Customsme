# Build BO3 Customs SPRX with OpenOrbis

This path targets **PS4 Black Ops III 1.33 only**. It uses the open-source OpenOrbis PS4 Toolchain v0.5.4 and its create-fself program. The original Visual Studio/Sony SDK project is left in place.

## Ubuntu / Linux terminal

Install the host build dependencies:

~~~bash
sudo apt update
sudo apt install -y clang-18 lld-18 make curl file
~~~

Download and extract the OpenOrbis v0.5.4 release, then point OO_PS4_TOOLCHAIN at the extracted directory that contains link.x, include/ and lib/:

~~~bash
mkdir -p "$HOME/OpenOrbis"
curl -fL --retry 3 "https://github.com/OpenOrbis/OpenOrbis-PS4-Toolchain/releases/download/v0.5.4/toolchain-llvm-18.tar.gz" -o /tmp/openorbis-toolchain.tar.gz
tar -xzf /tmp/openorbis-toolchain.tar.gz -C "$HOME/OpenOrbis"
find "$HOME/OpenOrbis" -name link.x -print
export OO_PS4_TOOLCHAIN="$HOME/OpenOrbis"
# If link.x is inside a child folder, set OO_PS4_TOOLCHAIN to that folder instead.
make -C SPRX clean all
~~~

The build should produce SPRX/build/openorbis/BO3-Customs.sprx and a SHA-256 file. If your archive extracts into a top-level subdirectory, set OO_PS4_TOOLCHAIN to that subdirectory.

## GitHub Actions

.github/workflows/build-sprx.yml runs the same Makefile on pushes touching SPRX/** and can also be run manually from the Actions tab. Successful main builds publish to the separate sprx-latest release; this does not replace the desktop fork-latest release.

## Important validation note

A successful link proves that the sources compile against OpenOrbis, not that every proprietary SDK ABI or PS4 runtime behavior is identical. The OpenOrbis mouse header currently omits full mouse prototypes, so this project supplies isolated declarations in openorbis_compat/. Install and test the result only on a controlled PS4 running BO3 1.33; specifically verify mouse/keyboard input, map loading, UI hooks and the persistent diagnostics log. Do not treat a build artifact alone as proof of runtime stability.


## OpenOrbis security/feature boundary

The OpenOrbis Makefile excludes `libjbc.cpp`, which contains a bundled kernel credential/memory helper incompatible with the OpenOrbis host headers. The OpenOrbis binary therefore does not include that helper, and startup logs explicitly report that automatic external-drive sandbox mounts are disabled. Local custom-map discovery remains enabled; external roots must already be mounted and visible to the process. The original Visual Studio/Sony SDK project is left unchanged.


Fork 1.2.1.18 adds a size/offset-checked ABI view for OpenOrbis virtual-query metadata and uniquely names the notification payload type to avoid public SDK header collisions.


Fork 1.2.1.19 adds the `SceKernelStat` compatibility alias for OpenOrbis' `OrbisKernelStat` so file-size checks in map-image loading compile against the open toolchain.


Fork 1.2.1.20 adapts the map scanner to OpenOrbis directory-entry declarations and removes a non-portable bounded-string call. It also fixes a diagnostic field-name typo that prevented `t7_maps.cpp` from compiling.


Fork 1.2.1.21 removes an unnecessary `d_fileno` macro fallback because the OpenOrbis directory-entry structure already exposes the required `d_fileno` field.


Fork 1.2.1.22 undefines the `d_fileno` compatibility macro in the OpenOrbis shim so the map scan compiles against the native directory-entry member used by this SDK.


## CRT entrypoint handling (Fork 1.2.1.23)

OpenOrbis v0.5.4's `crtlib.o` provides its own hidden `module_start` and `module_stop` symbols. This fork's build allows the duplicates, with application objects ordered first; the custom `module_start` explicitly walks the SDK-provided init array before launching the SPRX's worker threads. This avoids losing C++ static initialization while retaining the custom shutdown handler. A successful link must still be verified on console.
