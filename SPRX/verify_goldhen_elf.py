#!/usr/bin/env python3
"""Validate the GoldHEN Plugins ABI before packaging a PRX."""

from __future__ import annotations
import argparse
from pathlib import Path
import re
import subprocess
import sys

def run(command: list[str]) -> str:
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f"command failed ({result.returncode}): {' '.join(command)}\n{result.stdout}")
    return result.stdout

def fail(message: str) -> int:
    print(f"ERROR: GoldHEN PRX validation: {message}", file=sys.stderr)
    return 1

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--nm", default="nm")
    parser.add_argument("--readelf", default="readelf")
    parser.add_argument("elf")
    args = parser.parse_args()
    elf = str(Path(args.elf))
    try:
        global_symbols = run([args.nm, "-g", "--defined-only", elf])
        dynamic_symbols = run([args.readelf, "--dyn-syms", "--wide", elf])
        header = run([args.readelf, "-h", elf])
        all_symbols = run([args.nm, "-n", elf])
        sections = run([args.readelf, "-SW", elf])
    except RuntimeError as exc:
        return fail(str(exc))

    globals_by_name: dict[str, tuple[int, str]] = {}
    for line in global_symbols.splitlines():
        parts = line.split()
        if len(parts) >= 3:
            try:
                value = int(parts[0], 16)
            except ValueError:
                continue
            globals_by_name[parts[-1]] = (value, parts[-2])

    for required in ("_init", "plugin_load", "plugin_unload"):
        if required not in globals_by_name or globals_by_name[required][1] not in ("T", "t"):
            return fail(f"required global text symbol {required} is missing")

    entry_match = re.search(r"Entry point address:\s*(0x[0-9a-fA-F]+)", header)
    if not entry_match:
        return fail("readelf could not parse the ELF entrypoint")
    entry = int(entry_match.group(1), 16)
    init_value = globals_by_name["_init"][0]
    if not entry or entry != init_value:
        return fail(f"ELF entrypoint {entry:#x} does not resolve to _init at {init_value:#x}")

    dynamic = {}
    for line in dynamic_symbols.splitlines():
        match = re.match(r"\s*\d+:\s+([0-9a-fA-F]+)\s+\d+\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)", line)
        if not match:
            continue
        value, typ, bind, visibility, ndx, name = match.groups()
        dynamic[name.split("@")[0]] = (int(value, 16), typ, bind, visibility, ndx)

    for required in ("plugin_load", "plugin_unload"):
        found = dynamic.get(required)
        if not found or found[0] == 0 or found[1] != "FUNC" or found[2] != "GLOBAL" or found[3] != "DEFAULT" or found[4] == "UND":
            return fail(f"{required} is not a defined GLOBAL DEFAULT function in .dynsym: {found}")

    symbol_pattern = re.compile(r"^\s*([0-9a-fA-F]+)\s+(\S)\s+(\S+)\s*$")
    found_bounds: dict[str, list[int]] = {"__init_array_start": [], "__init_array_end": []}
    for line in all_symbols.splitlines():
        match = symbol_pattern.match(line)
        if match and match.group(3) in found_bounds:
            found_bounds[match.group(3)].append(int(match.group(1), 16))
    for name, values in found_bounds.items():
        if len(values) != 1:
            return fail(f"expected exactly one {name}; found {values}")
    start = found_bounds["__init_array_start"][0]
    end = found_bounds["__init_array_end"][0]
    if end < start or (end - start) % 8:
        return fail(f"invalid init-array bounds [{start:#x}, {end:#x})")

    section_match = None
    section_pattern = re.compile(r"^\s*\[\s*\d+\]\s+\.init_array\s+\S+\s+([0-9a-fA-F]+)\s+[0-9a-fA-F]+\s+([0-9a-fA-F]+)")
    for line in sections.splitlines():
        match = section_pattern.match(line)
        if match:
            section_match = (int(match.group(1), 16), int(match.group(2), 16))
            break
    if section_match is None:
        return fail("readelf could not find .init_array")
    section_start, section_size = section_match
    if start != section_start or end != section_start + section_size:
        return fail(
            f"init-array bounds [{start:#x}, {end:#x}) do not match "
            f".init_array [{section_start:#x}, {section_start + section_size:#x})"
        )

    print("GoldHEN PRX ABI validation passed")
    print(f"  ELF entrypoint _init = {entry:#x}")
    print(f"  exported plugin_load = {dynamic['plugin_load'][0]:#x}")
    print(f"  exported plugin_unload = {dynamic['plugin_unload'][0]:#x}")
    print(f"  .init_array range = [{start:#x}, {end:#x}) ({section_size} bytes)")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
