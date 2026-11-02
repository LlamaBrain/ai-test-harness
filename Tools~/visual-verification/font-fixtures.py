"""Generate labeled real-font PNGs without redistributing font binaries.

Requires Pillow. Supply a licensed local font; its digest is retained for reproduction.
Ground truth comes from the separately rendered glyph mask, not OCR or analyzer output.
"""
import argparse
import hashlib
import json
import pathlib
from PIL import Image, ImageDraw, ImageFont, __version__ as pillow_version

parser = argparse.ArgumentParser()
parser.add_argument('--font', required=True)
parser.add_argument('--out', required=True)
args = parser.parse_args()
font_path = pathlib.Path(args.font).resolve()
root = pathlib.Path(args.out).resolve()
root.mkdir(parents=True, exist_ok=False)
records = []
for resolution in (1080, 1440, 2160):
    for label, text, representative in [('representative', 'Agjp Qy', True), ('uppercase', 'MENU', False),
                                        ('numbers', '0123!?', False), ('latin-localized', 'Français Español', False)]:
        for dark in (False, True):
            font = ImageFont.truetype(str(font_path), round(38 * resolution / 1080))
            glyph = Image.new('L', (800, 180))
            ImageDraw.Draw(glyph).text((20, 20), text, font=font, fill=255)
            # White-on-black requires sRGB >=64 for the 2:1 edge criterion.
            # Black-on-white requires the composited sRGB <=183 (mask >=72).
            cutoff = 72 if dark else 64
            selected = glyph.point(lambda value: 255 if value >= cutoff else 0)
            extent = selected.getbbox()
            if extent is None:
                raise ValueError('Font produced no measurable pixels')
            truth = extent[3] - extent[1]
            screenshot = Image.new('RGB', (resolution * 16 // 9, resolution), 'white' if dark else 'black')
            screenshot.paste('black' if dark else 'white', (100, 100), glyph)
            identity = f'{label}-{resolution}-' + ('dark' if dark else 'light')
            filename = identity + '.png'
            screenshot.save(root / filename)
            manifest = {'schemaVersion': 1, 'screenId': identity, 'screenshotId': filename,
                        'resolution': {'width': screenshot.width, 'height': screenshot.height},
                        'uiScale': 1, 'locale': 'fr-ES' if label == 'latin-localized' else 'en-US',
                        'elements': [{'id': 'label', 'bounds': {'x': 100, 'y': 100, 'width': 800, 'height': 180},
                                      'representativeGlyphs': representative}]}
            (root / (identity + '.json')).write_text(json.dumps(manifest, indent=2))
            records.append({'image': filename, 'manifest': identity + '.json', 'text': text,
                            'expectedVisibleHeightPx': truth, 'representativeGlyphs': representative,
                            'imageSha256': hashlib.sha256((root / filename).read_bytes()).hexdigest()})
(root / 'ground-truth.json').write_text(json.dumps({'schemaVersion': 1,
    'fontSha256': hashlib.sha256(font_path.read_bytes()).hexdigest(), 'fontName': font_path.name,
    'pillowVersion': pillow_version, 'scope': 'Uniform-background rendered glyph masks; detection accuracy not asserted',
    'fixtures': records}, indent=2))
print(f'Generated {len(records)} labeled PNGs at {root}')
