# OCR fixtures

These small raster fixtures were generated locally with Pillow 12.3.0 using the Windows Microsoft YaHei font. They contain the printed strings `Hello OCR 2026`, `中文识别测试` and, in the long-line fixture, `Long text recognition 0123456789 Long text recognition 0123456789`.

- `bilingual.png`: 1000×260, black text on white, drawn at (35,30) and (35,130), 44-pixel font.
- `tilted.png`: the bilingual image rotated 8° counterclockwise with a white expanded canvas.
- `low-contrast.png`: the bilingual image mapped into gray levels 220–255.
- `transparent-text.png`: the same text on transparent pixels whose stored RGB channels are black. Discarding alpha would create an all-black background; compositing on white preserves the text.
- `exif-3.jpg` and `exif-6.jpg`: stored pixels rotated opposite to their EXIF orientation so the display-normalized image matches the original dimensions and layout. JPEG quality is 95.
- `long-line.png`: one long horizontal line, 1535×110.
- `compressed.png`: a 1000×1000 white 1-bit PNG, 512 encoded bytes. Its normalized RGBA PNG exceeds the original encoded byte length.

The model tests run actual pinned PP-OCRv5 mobile detection and recognition. Pure CTC tests exercise decoding semantics separately; they do not substitute simulated output for OCR integration tests.
