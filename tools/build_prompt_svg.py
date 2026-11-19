"""Build the editable, port-authored vector alphabet and circular prompt reference."""
from pathlib import Path


# Block-letter construction, drawn by hand against the game's visual vocabulary.
GLYPHS = {
    'A': ['01110','11011','11011','11111','11011','11011','11011'],
    'B': ['11110','11011','11011','11110','11011','11011','11110'],
    'C': ['01111','11000','11000','11000','11000','11000','01111'],
    'D': ['11110','11011','11011','11011','11011','11011','11110'],
    'E': ['11111','11000','11000','11110','11000','11000','11111'],
    'F': ['11111','11000','11000','11110','11000','11000','11000'],
    'G': ['01111','11000','11000','11011','11011','11011','01111'],
    'H': ['11011','11011','11011','11111','11011','11011','11011'],
    'I': ['111','010','010','010','010','010','111'],
    'J': ['00111','00011','00011','00011','11011','11011','01110'],
    'K': ['11011','11011','11110','11100','11110','11011','11011'],
    'L': ['11000','11000','11000','11000','11000','11000','11111'],
    'M': ['11011','11111','11111','11011','11011','11011','11011'],
    'N': ['11001','11101','11111','11011','11011','11011','11011'],
    'O': ['01110','11011','11011','11011','11011','11011','01110'],
    'P': ['11110','11011','11011','11110','11000','11000','11000'],
    'Q': ['01110','11011','11011','11011','11011','11110','00111'],
    'R': ['11110','11011','11011','11110','11110','11011','11011'],
    'S': ['01111','11000','11000','01110','00011','00011','11110'],
    'T': ['11111','00110','00110','00110','00110','00110','00110'],
    'U': ['11011','11011','11011','11011','11011','11011','01110'],
    'V': ['11011','11011','11011','11011','11011','01110','00100'],
    'W': ['11011','11011','11011','11011','11111','11111','11011'],
    'X': ['11011','11011','01110','00100','01110','11011','11011'],
    'Y': ['11011','11011','11011','01110','00110','00110','00110'],
    'Z': ['11111','00011','00110','01100','11000','11000','11111'],
    '0': ['01110','11011','11011','11011','11011','11011','01110'],
    '1': ['010','110','010','010','010','010','111'],
    '2': ['01110','11011','00011','00110','01100','11000','11111'],
    '3': ['11110','00011','00011','01110','00011','00011','11110'],
    '4': ['11011','11011','11011','11111','00011','00011','00011'],
    '5': ['11111','11000','11000','11110','00011','00011','11110'],
    '6': ['01110','11000','11000','11110','11011','11011','01110'],
    '7': ['11111','00011','00110','00110','01100','01100','01100'],
    '8': ['01110','11011','11011','01110','11011','11011','01110'],
    '9': ['01110','11011','11011','01111','00011','00011','01110'],
}


def symbol(identifier, rows):
    width, height = len(rows[0]), len(rows)
    rects = ''.join(f'<rect x="{x}" y="{y}" width="1" height="1"/>'
                    for y, row in enumerate(rows) for x, cell in enumerate(row) if cell == '1')
    return f'<symbol id="{identifier}" viewBox="0 0 {width} {height}" preserveAspectRatio="none">{rects}</symbol>'


def main():
    symbols = [symbol('glyph-' + char, rows) for char, rows in GLYPHS.items()]
    # The original face buttons use five rows, with a nine-cell stepped ring.
    for char, rows in {
        'A': ['01110','11001','11001','11111','11001'],
        'B': ['11110','11001','11110','11001','11110'],
        'X': ['11011','01110','00100','01110','11011'],
        'Y': ['11011','11011','01110','00100','00100'],
    }.items():
        symbols.append(symbol('face-' + char, rows))
    ring = ['001111100','010000010','100000001','100000001','100000001',
            '100000001','100000001','010000010','001111100']
    rects = ''.join(f'<rect x="{28 + x * 8}" y="{28 + y * 8}" width="7" height="7"/>'
                    for y, row in enumerate(ring) for x, cell in enumerate(row) if cell == '1')
    symbols.append(f'<symbol id="circle" viewBox="0 0 128 128">{rects}</symbol>')
    preview = []
    for index, char in enumerate(GLYPHS):
        x, y = 28 + index % 18 * 43, 44 + index // 18 * 64
        preview.append(f'<use href="#glyph-{char}" x="{x}" y="{y}" width="30" height="42"/>')
    labels = ['A', 'E', 'Q', 'W', 'S', 'D', 'P', 'SPC', 'ESC', 'SFT', 'TAB', 'BSP', 'ENT', 'PGU', 'PGD']
    for index, label in enumerate(labels):
        x, y = 5 + index % 8 * 96, 190 + index // 8 * 100
        pieces = [f'<use href="#circle" x="{x}" y="{y}" width="128" height="128"/>']
        if label == 'A':
            pieces.append(f'<use href="#face-A" x="{x + 44}" y="{y + 44}" width="40" height="40"/>')
        else:
            sx = 7 if len(label) == 1 else 3
            width = sum(len(GLYPHS[c][0]) for c in label) + len(label) - 1
            start = x + 64 - width * sx / 2
            for char in label:
                gw = len(GLYPHS[char][0])
                pieces.append(f'<use href="#glyph-{char}" x="{start}" y="{y + 46}" width="{gw * sx}" height="35" preserveAspectRatio="none"/>')
                start += (gw + 1) * sx
        preview.append('<g filter="url(#glow)">' + ''.join(pieces) + '</g>')
    definitions = '\n    '.join(symbols)
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="820" height="430" viewBox="0 0 820 430">
  <title>PIXEL keyboard prompt alphabet and circle</title>
  <desc>Port-authored vector construction. Runtime reads symbol rectangles; previews reuse those same symbols. Circle: 8px pitch, 7px cells, 72px body on a 128px canvas. Labels support one to three characters.</desc>
  <defs>
    <filter id="glow" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2"/><feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>
    {definitions}
  </defs>
  <g fill="#fff" shape-rendering="crispEdges">{''.join(preview)}</g>
</svg>
'''
    target = Path(__file__).resolve().parent.parent / 'assets/keyboard-prompts/keyboard.svg'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(svg, encoding='utf-8')
    print(target)


if __name__ == '__main__':
    main()
