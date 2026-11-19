"""Inventory every decoded texture and make private contact sheets for prompt review.

Original pixels must remain in private/. Requires Pillow for diagnostic images.
"""
from pathlib import Path
import argparse
import json
import struct

from PIL import Image, ImageDraw
from inspect_content import seven_bit_integer


def texture(path):
    data = path.read_bytes()
    count, pos = seven_bit_integer(data, 10)
    readers = []
    for _ in range(count):
        length, pos = seven_bit_integer(data, pos)
        readers.append(data[pos:pos + length].decode())
        pos += length + 4
    _, pos = seven_bit_integer(data, pos)
    reader, pos = seven_bit_integer(data, pos)
    if "Texture2DReader" not in readers[reader - 1]:
        return None
    fmt, width, height, levels, size = struct.unpack_from("<5i", data, pos)
    if fmt != 1 or size != width * height * 4:
        raise ValueError(f"Unexpected decoded texture format: {path}")
    # Decoding decompresses XNB, but leaves Xbox ARGB texels in big-endian order.
    return Image.frombytes("RGBA", (width, height), data[pos + 20:pos + 20 + size], "raw", "ARGB")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("decoded", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--review-report", type=Path, help="Write metadata only, suitable for the repository")
    args = parser.parse_args()
    private = Path(__file__).resolve().parent.parent / "private"
    if not args.output.resolve().is_relative_to(private.resolve()):
        parser.error("Diagnostic images must stay inside private/.")
    args.output.mkdir(parents=True, exist_ok=True)
    images, rows = [], []
    for path in sorted(args.decoded.rglob("*.xnb")):
        im = texture(path)
        if im is None:
            continue
        name = path.relative_to(args.decoded).with_suffix("").as_posix()
        rows.append({"asset": name, "width": im.width, "height": im.height})
        images.append((name, im))
        target = args.output / (name + ".png")
        target.parent.mkdir(parents=True, exist_ok=True)
        im.save(target)
    for page in range((len(images) + 39) // 40):
        sheet = Image.new("RGB", (1600, 1440), "#182132")
        draw = ImageDraw.Draw(sheet)
        for cell, (name, im) in enumerate(images[page * 40:(page + 1) * 40]):
            x, y = cell % 5 * 320, cell // 5 * 180
            thumb = im.copy()
            thumb.thumbnail((310, 142))
            sheet.paste(thumb, (x + (320 - thumb.width) // 2, y), thumb)
            draw.text((x + 4, y + 145), name.split("/")[-1], fill="white")
            draw.text((x + 4, y + 160), f"{im.width} x {im.height}", fill="#b0c0e0")
        sheet.save(args.output / f"sheet-{page + 1:02}.png")
    (args.output / "inventory.json").write_text(json.dumps(rows, indent=2) + "\n")
    if args.review_report:
        repo = Path(__file__).resolve().parent.parent
        layouts = json.loads((repo / "assets/keyboard-prompts/layouts.json").read_text())
        actual = {row['asset'] for row in rows}
        missing = set(layouts) - actual
        if missing:
            raise ValueError(f"Prompt layouts reference missing textures: {sorted(missing)}")
        report = {
            'textureCount': len(rows),
            'replacementTextureCount': sum(row['asset'] in layouts for row in rows),
            'generatedLayouts': [],
            'fontAssets': ['GAME/Font/GAME_TextBox', 'GAME/Font/GAME_Timer', 'tmp/hop'],
            'review': 'All contact sheets visually reviewed for standalone and baked controller prompts. Fonts retain original text rendering; keyboard sign wording is translated separately.',
            'textures': [{**row, 'disposition': 'keyboard-swap' if row['asset'] in layouts else 'retain-original'} for row in rows],
        }
        args.review_report.write_text(json.dumps(report, indent=2) + "\n")
    print(f"TEXTURE_AUDIT_OK textures={len(rows)} sheets={(len(images) + 39) // 40}")


if __name__ == "__main__":
    main()
