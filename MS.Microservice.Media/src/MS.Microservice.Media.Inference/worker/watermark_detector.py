"""One local CPU detection operation. No model downloads or remote-code loading."""
import argparse
import json
import sys
import time
from watermark_model import read_verified_checkpoint, WatermarkDetector


def main():
    started = time.perf_counter()
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--mask-output", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--profile", choices=["rgb_0_1", "imagenet"], required=True)
    parser.add_argument("--threshold", type=float, required=True)
    parser.add_argument("--threads", type=int, required=True)
    parser.add_argument("--max-pixels", type=int, required=True)
    parser.add_argument("--max-dimension", type=int, required=True)
    args = parser.parse_args()
    if not 0 < args.threshold < 1 or min(args.threads, args.max_pixels, args.max_dimension) < 1:
        raise ValueError("Invalid probability threshold, thread count or image limits")
    checkpoint = read_verified_checkpoint(args.model)
    import numpy as np
    from PIL import Image, ImageOps
    with Image.open(args.input) as source:
        width, height = source.size
        if width < 1 or height < 1 or max(width, height) > args.max_dimension or width * height > args.max_pixels:
            raise ValueError("The input exceeds the configured raster limits")
        if getattr(source, "n_frames", 1) != 1:
            raise ValueError("The detector requires a static image")
        image = ImageOps.exif_transpose(source).convert("RGB")
    detector = WatermarkDetector(checkpoint, args.profile, args.threshold, args.threads)
    del checkpoint
    selected, metadata = detector.predict(image)
    Image.fromarray(selected.astype(np.uint8) * 255).save(args.mask_output, format="PNG")
    metadata["workerSeconds"] = time.perf_counter() - started
    print(json.dumps(metadata), flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"error": type(error).__name__, "message": str(error)}), file=sys.stderr, flush=True)
        sys.exit(2)
