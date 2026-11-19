"""Build a local side-by-side review report from --probe-signs captures."""
import argparse
from collections import defaultdict
from html import escape
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--contact-sheets", action="store_true", help="Also create PNG comparison sheets (requires Pillow)")
    args = parser.parse_args()
    directory = args.directory
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    groups = defaultdict(dict)
    for row in manifest["images"]:
        if not (directory / row["file"]).is_file():
            raise FileNotFoundError(row["file"])
        groups[row["level"], row["plaque"]][row["input"], row["scene"], row["aspect"]] = row
    parts = ["""<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>PIXEL! — plaque rendering captures</title>
<style>
body{background:#10131b;color:#e7ebf5;font:16px system-ui;margin:24px auto;max-width:1800px;padding:0 20px}
h1{font-size:28px}h2{margin-top:36px}a{color:#98d1ff}p{line-height:1.6}
section{border-top:1px solid #424856;padding-bottom:24px}.pair{display:grid;grid-template-columns:1280fr 1680fr;gap:16px;align-items:start}
figure{margin:0}figcaption{padding:8px 0;color:#c7cfdf}img{width:100%;height:auto;image-rendering:pixelated;background:black}
pre{white-space:pre-wrap;line-height:1.5;background:#191f2b;padding:16px;border-radius:6px}
details{margin:16px 0}summary{cursor:pointer;padding:8px;background:#202736}
nav{display:flex;gap:16px;flex-wrap:wrap}small{color:#aeb8cc}
@media(max-width:800px){.pair{grid-template-columns:1fr}}
</style><h1>PIXEL! — every plaque</h1>
<p>Actual game-rendered frames: <b>16:9 (1280 × 720)</b> and <b>21:9 (1680 × 720)</b>.
The internal fixture visits all seven levels, reveals concealed plaques, teleports beside each,
and invokes the original sign interaction. Gameplay is frozen for capture; these are posed scenes.
The dark overlay is settled at its original final opacity. Click an image for the full-size PNG.</p>
"""]
    parts.append(f'<p>{len(groups)} plaques · {manifest["captures"]} captures · keyboard and controller · prompt and opened dialogue.</p><nav>')
    for level, count in enumerate(manifest["counts"]):
        parts.append(f'<a href="#level-{level}">Level {level}: {count} plaques</a>')
    parts.append('</nav>')
    if args.contact_sheets:
        parts.append('<details><summary>PNG comparison sheets</summary><nav>')
        for level, count in enumerate(manifest['counts']):
            for page in range(1, (count + 3) // 4 + 1):
                parts.append(f'<a href="comparison-level-{level}-{page}.png">Level {level}, sheet {page}</a>')
        parts.append('</nav></details>')
    current_level = None
    for (level, plaque), variants in groups.items():
        if current_level != level:
            current_level = level
            parts.append(f'<h2 id="level-{level}">Level {level}{" — tutorial" if level == 0 else ""}</h2>')
        first = next(iter(variants.values()))
        hidden = ' · concealed plaque revealed by fixture' if first['Hidden'] else ''
        parts.append(f'<section><h3>Plaque {plaque:02d} <small>· tile ({first["X"]}, {first["Y"]}){hidden}</small></h3>')
        for input_mode, scene in [('keyboard', 'open'), ('keyboard', 'prompt'), ('controller', 'open'), ('controller', 'prompt')]:
            primary = (input_mode, scene) == ('keyboard', 'open')
            label = f'{input_mode.title()} — {"opened plaque" if scene == "open" else "interaction prompt"}'
            if not primary:
                parts.append(f'<details><summary>{label}</summary>')
            parts.append('<div class="pair">')
            for aspect in ['16x9', '21x9']:
                row = variants[input_mode, scene, aspect]
                name = escape(row['file'], quote=True)
                caption = f'{aspect.replace("x", ":")} · {label}'
                parts.append(f'<figure><a href="{name}"><img loading="lazy" src="{name}" alt="{escape(caption)}"></a><figcaption>{caption}</figcaption></figure>')
            parts.append('</div>')
            if not primary:
                parts.append('</details>')
        parts.append(f'<details><summary>Plaque text</summary><pre>{escape(variants["keyboard", "open", "16x9"]["rendered"])}</pre></details></section>')
    parts.append('</html>')
    output = directory / 'index.html'
    output.write_text('\n'.join(parts), encoding='utf-8')
    print(f'SIGN_GALLERY_OK plaques={len(groups)} images={manifest["captures"]} path={output.resolve()}')
    if args.contact_sheets:
        from PIL import Image, ImageDraw, ImageFont
        font_path = Path('C:/Windows/Fonts/consola.ttf')
        font = ImageFont.truetype(str(font_path), 22) if font_path.exists() else ImageFont.load_default()
        for level in range(7):
            plaques = [(key, rows) for key, rows in groups.items() if key[0] == level]
            for offset in range(0, len(plaques), 4):
                page = plaques[offset:offset + 4]
                sheet = Image.new('RGB', (1528, 60 + len(page) * 408), '#10131b')
                draw = ImageDraw.Draw(sheet)
                draw.text((16, 12), f'Level {level} | Opened plaques | 16:9 left, 21:9 right | Keyboard hints', fill='#ffffff', font=font)
                for index, ((_, plaque), rows) in enumerate(page):
                    y = 60 + index * 408
                    for aspect, x, width in [('16x9', 16, 640), ('21x9', 672, 840)]:
                        row = rows['keyboard', 'open', aspect]
                        draw.text((x, y), f'Plaque {plaque:02d} | {aspect.replace("x", ":")}', fill='#c7cfdf', font=font)
                        with Image.open(directory / row['file']) as source:
                            sheet.paste(source.convert('RGB').resize((width, 360), Image.Resampling.NEAREST), (x, y + 32))
                sheet.save(directory / f'comparison-level-{level}-{offset // 4 + 1}.png')


if __name__ == '__main__':
    main()
