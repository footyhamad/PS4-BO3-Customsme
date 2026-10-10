#!/usr/bin/env python3
"""Patch the pinned OpenOrbis linker script with real init-array boundaries."""

from __future__ import annotations

from pathlib import Path
import re
import sys


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: prepare_openorbis_linker.py <toolchain-link.x> <output-link.x>", file=sys.stderr)
        return 2

    source = Path(sys.argv[1])
    output = Path(sys.argv[2])
    text = source.read_text(encoding="utf-8")

    # In OpenOrbis v0.5.4 the CRT declares these names as incomplete arrays,
    # while link.x does not assign them to the actual .init_array section.
    # That leaves one-element BSS objects instead of the section boundaries.
    pattern = re.compile(
        r"(?m)^(?P<indent>[ \t]*)\.init_array[ \t]*:[ \t]*\{\r?\n"
        r"[ \t]*\*\(\.init_array\);[ \t]*\r?\n"
        r"(?P=indent)\}"
    )

    def replacement(match: re.Match) -> str:
        indent = match.group("indent")
        return (
            f"{indent}.init_array : {{\n"
            f"{indent}\t__init_array_start = .;\n"
            f"{indent}\tKEEP(*(SORT_BY_INIT_PRIORITY(.init_array.*)))\n"
            f"{indent}\tKEEP(*(.init_array))\n"
            f"{indent}\t__init_array_end = .;\n"
            f"{indent}}}"
        )

    patched, count = pattern.subn(replacement, text)
    if count != 1:
        print(
            f"ERROR: expected exactly one stock OpenOrbis .init_array stanza in {source}; found {count}. "
            "Refusing to link with potentially invalid constructor boundaries.",
            file=sys.stderr,
        )
        return 1

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(patched, encoding="utf-8")
    print(f"Generated {output}: explicit __init_array_start/end symbols around .init_array")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
