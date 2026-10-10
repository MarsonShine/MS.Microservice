# Ruled-table fixtures

Fixtures were generated locally with Pillow 12.3.0 and the Windows Microsoft YaHei font, then transformed with OpenCV 4.13.0. The normal image is 700×440. Grid lines are three pixels thick, with x coordinates 40, 240, 440, 640 and y coordinates 40, 160, 280, 400. Cell text uses a 36-pixel font at (60+column×200, 65+row×120).

The normal 3×3 cell texts, in row order, are `名称 / 数量 / Price`, `苹果 / 12 / 3.50`, and `香蕉 / 8 / 2.00`. `colspan.png` removes the first vertical separator in row zero and replaces its first two cells with `商品清单`; `rowspan.png` merges column zero in rows one and two with `水果`; `merged-block.png` merges the top-left 2×2 block with `Merged`.

Other variants remove the left outer border, break part of an internal line, create a nonrectangular merge, rotate by 8°, or apply a perspective transform. These must not become accepted cropped or invented grids. The transparent fixture stores transparent background RGB as black. `exif-6.jpg` stores counterclockwise-rotated pixels with EXIF orientation 6; its normalized grid must match the original 700×440 layout exactly.

`grid-glyph.png` contains only the 60-pixel Chinese character `田`, whose visible strokes form a 44×44 grid. Its 22-pixel cell spacing is below the default morphology-kernel length of 40 and must not be accepted as a table. Larger grid-shaped glyphs can remain geometrically indistinguishable from tables; recognition always recommends review.

`two-tables.png` places two copies side by side. `multiline-outside.png` has `Line one` and `Line two` in the first cell, eight empty cells, and `Outside` above the table. `crossing-text.png` puts `Crossing` across a vertical divider; grid-line removal can damage recognition, so this case asserts that actual recognized text remains unassigned rather than claiming exact transcription. `plain-text.png` contains `Ordinary text 2026` without any frame. These images test actual pinned OCR inference; separate Python tests isolate the geometry and assignment contracts.
