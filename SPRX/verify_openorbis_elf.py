#!/usr/bin/env python3
"""Fail the OpenOrbis build if SPRX entrypoints or init-array bounds are wrong."""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import subprocess
import sys


def run(command: list[str]) -> str:
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode != 0:
        raise RuntimeError(f"command failed ({result.returncode}): {' '.join(command)}\n{result.stdout}")
    return result.stdout


def fail(message: str) -> int:
    print(f"ERROR: OpenOrbis ELF validation: {message}", file=sys.stderr)
    return 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--nm", default="nm")
    parser.add_argument("--readelf", default="readelf")
    parser.add_argument("elf")
    args = parser.parse_args()

    elf = str(Path(args.elf))
    try:
        all_symbols = run([args.nm, "-n", elf])
        global_symbols = run([args.nm, "-g", "--defined-only", elf])
        sections = run([args.readelf, "-SW", elf])
    except RuntimeError as exc:
        return fail(str(exc))

    global_rows = []
    for line in global_symbols.splitlines():
        parts = line.split()
        if len(parts) >= 3:
            global_rows.append((parts[0], parts[-2], parts[-1]))

    for required in ("module_start", "module_stop"):
        matches = [(address, kind) for address, kind, name in global_rows if name == required]
        if len(matches) != 1 or matches[0][1] != "T":
            return fail(
                f"expected exactly one exported text symbol {required}; found {matches}. "
                "Do not mask duplicate CRT entrypoints with --allow-multiple-definition."
            )

    symbol_pattern = re.compile(r"^\s*([0-9a-fA-F]+)\s+(\S)\s+(\S+)\s*$")
    found: dict[str, list[int]] = {"__init_array_start": [], "__init_array_end": []}
    for line in all_symbols.splitlines():
        match = symbol_pattern.match(line)
        if not match:
            continue
        address, _kind, name = match.groups()
        if name in found:
            found[name].append(int(address, 16))

    for name, values in found.items():
        if len(values) != 1:
            return fail(f"expected one resolved symbol {name}; found {values}")

    init_start = found["__init_array_start"][0]
    init_end = found["__init_array_end"][0]
    if init_end < init_start or (init_end - init_start) % 8 != 0:
        return fail(
            f"invalid init-array range [{init_start:#x}, {init_end:#x}); "
            "range must be ordered and aligned to 8-byte function pointers"
        )

    section_match = None
    section_pattern = re.compile(
        r"^\s*\[\s*\d+\]\s+\.init_array\s+\S+\s+([0-9a-fA-F]+)\s+[0-9a-fA-F]+\s+([0-9a-fA-F]+)"
    )
    for line in sections.splitlines():
        match = section_pattern.match(line)
        if match:
            section_match = (int(match.group(1), 16), int(match.group(2), 16))
            break
    if section_match is None:
        return fail("readelf could not find the .init_array section")

    section_start, section_size = section_match
    section_end = section_start + section_size
    if init_start != section_start or init_end != section_end:
        return fail(
            f"init-array symbols [{init_start:#x}, {init_end:#x}) do not match "
            f".init_array section [{section_start:#x}, {section_end:#x})"
        )

    entrypoints = {name: address for address, kind, name in global_rows
                   if kind == "T" and name in ("module_start", "module_stop")}
    print("OpenOrbis ELF validation passed")
    print(f"  exported module_start = {entrypoints['module_start']}")
    print(f"  exported module_stop  = {entrypoints['module_stop']}")
    print(f"  .init_array range     = [{init_start:#x}, {init_end:#x}) ({section_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
