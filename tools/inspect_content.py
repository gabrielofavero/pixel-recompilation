"""Inventory type-reader names from locally decoded, supported Pixel XNB files."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys


REPO = Path(__file__).resolve().parent.parent
CONTENT_PREFIX = "584E07D1/Content/"


def seven_bit_integer(data: bytes, position: int) -> tuple[int, int]:
    value = 0
    for shift in range(0, 35, 7):
        if position >= len(data):
            raise ValueError("truncated 7-bit integer")
        byte = data[position]
        position += 1
        value |= (byte & 0x7F) << shift
        if byte & 0x80 == 0:
            return value, position
    raise ValueError("invalid 7-bit integer")


def readers(data: bytes, name: str) -> list[dict]:
    if (len(data) < 12 or data[:4] != b"XNBx" or data[4] != 4 or data[5] & 0x80 or
            struct.unpack_from("<I", data, 6)[0] != len(data)):
        raise ValueError(f"unsupported or invalid decoded XNB: {name}")
    count, position = seven_bit_integer(data, 10)
    if not 1 <= count <= 64:
        raise ValueError(f"invalid reader count in {name}")
    result = []
    for _ in range(count):
        length, position = seven_bit_integer(data, position)
        if not 1 <= length <= 4096 or position + length + 4 > len(data):
            raise ValueError(f"invalid reader name length in {name}")
        reader = data[position:position + length].decode("utf-8")
        position += length
        version = struct.unpack_from("<I", data, position)[0]
        position += 4
        result.append({"name": reader, "version": version})
    if position >= len(data):
        raise ValueError(f"XNB has no object data: {name}")
    return result


def inspect(content: Path, inventory: Path) -> dict:
    source = json.loads(inventory.read_text(encoding="utf-8"))
    manifest = json.loads((REPO / "config" / "supported-versions.json").read_text(encoding="utf-8"))
    if source["input"]["sha256"] not in {p["packageSha256"] for p in manifest["packages"]}:
        raise ValueError("inventory is not for a supported package")
    expected = {row["path"][len(CONTENT_PREFIX):] for row in source["files"]
                if row["path"].startswith(CONTENT_PREFIX) and row["path"].lower().endswith(".xnb")}
    actual = {p.relative_to(content).as_posix() for p in content.rglob("*.xnb")}
    if actual != expected:
        raise ValueError(f"decoded XNB set differs from inventory: missing={len(expected - actual)} extra={len(actual - expected)}")
    rows = []
    all_readers = set()
    for relative in sorted(expected):
        data = (content / relative).read_bytes()
        type_readers = readers(data, relative)
        all_readers.update(row["name"] for row in type_readers)
        rows.append({
            "path": relative,
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
            "readers": type_readers,
        })
    return {
        "schemaVersion": 1,
        "sourcePackageSha256": source["input"]["sha256"],
        "decodedXnbCount": len(rows),
        "readerTypes": sorted(all_readers),
        "files": rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("decoded_content", type=Path)
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        report = inspect(args.decoded_content, args.inventory)
    except (OSError, ValueError, KeyError, UnicodeError, json.JSONDecodeError) as error:
        print(f"CONTENT_INVENTORY_FAIL: {error}", file=sys.stderr)
        return 1
    result = json.dumps(report, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(result, encoding="utf-8")
        print(f"CONTENT_INVENTORY_OK files={report['decodedXnbCount']} readerTypes={len(report['readerTypes'])}")
    else:
        print(result, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
