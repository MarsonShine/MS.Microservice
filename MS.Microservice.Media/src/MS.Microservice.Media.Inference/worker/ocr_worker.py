"""One local CPU OCR request; input is a bounded, EXIF-normalized PNG produced by ImageCodec."""
import argparse
import json
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--models", required=True)
    for name in ("threads", "max-pixels", "max-dimension", "det-side", "rec-width", "max-regions", "max-characters"):
        parser.add_argument("--" + name, required=True, type=int)
    for name in ("det-threshold", "box-threshold", "min-confidence"):
        parser.add_argument("--" + name, required=True, type=float)
    options = parser.parse_args()
    if not (1 <= options.threads <= 64 and options.max_pixels >= 1 and options.max_dimension >= 1
            and 32 <= options.det_side <= 2048 and options.det_side % 32 == 0 and 320 <= options.rec_width <= 4096
            and 1 <= options.max_regions <= 256 and 1 <= options.max_characters <= 32768
            and 0 < options.det_threshold < 1 and 0 <= options.box_threshold <= 1 and 0 <= options.min_confidence <= 1):
        raise ValueError("Invalid OCR worker budgets or thresholds.")
    import cv2
    import numpy as np
    from PIL import Image
    from ocr_pipeline import OcrEngine
    cv2.setNumThreads(options.threads)
    Image.MAX_IMAGE_PIXELS = options.max_pixels
    with Image.open(options.input) as source:
        width, height = source.size
        if width < 1 or height < 1 or max(width, height) > options.max_dimension or width * height > options.max_pixels:
            raise ValueError("OCR input exceeds raster limits.")
        if getattr(source, "n_frames", 1) != 1:
            raise ValueError("Animated OCR inputs are not supported.")
        rgba = source.convert("RGBA")
        rgb = Image.alpha_composite(Image.new("RGBA", source.size, "white"), rgba).convert("RGB")
        image = cv2.cvtColor(np.asarray(rgb), cv2.COLOR_RGB2BGR)
    result = OcrEngine(options.models, options.threads).recognize(image, options)
    payload = json.dumps(result, ensure_ascii=False, allow_nan=False, separators=(",", ":"))
    if len(payload) > 240000:
        raise ValueError("OCR exceeds the result serialization budget.")
    sys.stdout.reconfigure(encoding="utf-8")
    print(payload)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"OCR failed: {type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(1)
