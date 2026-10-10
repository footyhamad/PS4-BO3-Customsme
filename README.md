A tool to convert BO3 customs to PS4 and a SPRX to load them on HEN consoles.

## FF Porter fork version

The fork starts at **Fork 1.00** and is based on **ItsJokerZz v2.00**. The desktop tool includes **CHECK VERSION** and **UPDATE TOOL**. Updates are built by GitHub Actions and downloaded from this fork's rolling `fork-latest` release.

## Getting started

Everything you need is on the [Releases](../../releases) page: the tool, SPRX, and dependencies. 
You will need a copy of **BO3 installed on PC** + whatever maps you wish to port.
Ensure you have **version 1.33** of the game installed on your console (**any region**).

1. Extract the `Console.zip` from the releases and copy it to `/data/` on your console.
2. Next now place the SPRX wherever you wish, perferably in `/data/BO3-Customs/`.
3. Run the tool, drag and drop a steam map into the tool and then wait for it to finish.
4. Copy the converted map into `/data/BO3-Customs/usermaps/` however you may wish.
5. Finally, launch the game and load the appropriate build for your loader:
   - **GoldHEN Plugins system:** use `BO3-Customs.prx` from the SPRX rolling release and add it to `/data/GoldHEN/plugins/` plus `/data/GoldHEN/plugins.ini`.
   - **A loader that directly starts generic SPRX module entrypoints:** use `BO3-Customs.sprx`.

Maps can also go on a USB drive or extended storage (`/mnt/usb0-7/`, `/mnt/ext0-7/`), in `BO3-Customs/usermaps/`
at the root of the drive (a `BO3-Customs/zone/` there works too). Everything else stays in `/data/BO3-Customs/`.

## Console Layout
```text
/data/BO3-Customs/
├── BO3-Customs.sprx   # generic SPRX loader
├── BO3-Customs.prx    # GoldHEN Plugins system build
├── ui_scripts/
│   ├── graphics.lua
│   ├── kbm_strings.lua
│   ├── mapselect.lua
│   ├── maptable.lua
│   ├── mouse.lua
│   └── restart.lua
├── lui/
│   └── ui/
│       └── t7/
│           └── utility/
│               └── pcutility.lua
├── zone/
├── snd/
│   ├── all/
│   │   ├── zm_levelcommon.all.sabl
│   │   └── zm_levelcommon.all.sabs
│   └── <lang>/
│       ├── zm_levelcommon.<lang>.sabl
│       └── zm_levelcommon.<lang>.sabs
├── en_zm_levelcommon.ff
├── en_zm_levelcommon.xpak
├── zm_levelcommon.ff
├── zm_levelcommon.xpak
└── usermaps/
    └── <map>/
        ├── previewimage.png
        ├── loadingimage.png
        ├── workshop.json
        ├── <map>.ff
        ├── <map>.xpak
        ├── <lang>_<map>.ff
        ├── <lang>_<map>.xpak
        ├── snd/
        │   └── <lang>/
        │       ├── <map>.<lang>.sabl
        │       └── <map>.<lang>.sabs
        └── video/
            └── <name>.mkv
```

## Building Requirements

**Open-source builds**
- OpenOrbis PS4 Toolchain v0.5.4, Clang/LLD 18, Make and Python 3.
- The workflow also builds a GoldHEN Plugins-compatible `.prx` using the official GoldHEN Plugin SDK CRT; see `SPRX/BUILD-OPENORBIS.md`.


## Credits

- **ItsJokerZz** — SPRX and port development.
- **flatz** — `downgrade_elf.py` and `make_fself.py`.

## License

MIT — see [LICENSE](LICENSE).
