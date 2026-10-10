"""One local CPU detection operation. No model downloads or remote-code loading."""
import argparse
import hashlib
import importlib.metadata
import io
import json
import sys
import time
from pathlib import Path


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
    manifest = json.loads(Path(__file__).with_name("modelmanifest.json").read_text(encoding="utf-8"))
    for package in ["segmentation-models-pytorch", "timm"]:
        observed = importlib.metadata.version(package)
        if observed != manifest[package]:
            raise RuntimeError(f"Expected {package}=={manifest[package]}, found {observed}")
    with open(args.model, "rb") as source:
        checkpoint_bytes = source.read(manifest["bytes"] + 1)
    if len(checkpoint_bytes) != manifest["bytes"] or hashlib.sha256(checkpoint_bytes).hexdigest() != manifest["sha256"]:
        raise ValueError("The watermark checkpoint length or SHA-256 does not match the fixed model manifest")

    import numpy as np
    import torch
    import segmentation_models_pytorch as smp
    from PIL import Image, ImageOps, __version__ as pillow_version

    torch.set_num_threads(args.threads)
    torch.set_num_interop_threads(1)
    with Image.open(args.input) as source:
        width, height = source.size
        if width < 1 or height < 1 or max(width, height) > args.max_dimension or width * height > args.max_pixels:
            raise ValueError("The input exceeds the configured raster limits")
        if getattr(source, "n_frames", 1) != 1:
            raise ValueError("The detector requires a static image")
        image = ImageOps.exif_transpose(source).convert("RGB")
    width, height = image.size
    tensor = np.asarray(image.resize((512, 512), Image.Resampling.BILINEAR)).astype(np.float32) / 255
    if args.profile == "imagenet":
        tensor = (tensor - np.array([.485, .456, .406], dtype=np.float32)) / np.array([.229, .224, .225], dtype=np.float32)
    tensor = torch.from_numpy(tensor.transpose(2, 0, 1)[None].copy())
    model = smp.UnetPlusPlus(encoder_name="efficientnet-b4", encoder_weights=None, in_channels=3, classes=1)
    checkpoint = torch.load(io.BytesIO(checkpoint_bytes), map_location="cpu", weights_only=True)
    model.load_state_dict(checkpoint, strict=True)
    del checkpoint, checkpoint_bytes
    model.eval()
    inference_started = time.perf_counter()
    with torch.inference_mode():
        probability = model(tensor).sigmoid()[0, 0].numpy()
    inference_seconds = time.perf_counter() - inference_started
    probability = np.asarray(Image.fromarray(probability).resize((width, height), Image.Resampling.BILINEAR))
    if not np.isfinite(probability).all():
        raise ValueError("The model returned non-finite probabilities")
    selected = probability >= args.threshold
    Image.fromarray(selected.astype(np.uint8) * 255).save(args.mask_output, format="PNG")
    print(json.dumps({
        "modelId": manifest["modelId"], "sha256": manifest["sha256"], "profile": args.profile,
        "threshold": args.threshold, "width": width, "height": height,
        "selectedPixelCount": int(selected.sum()), "maximumProbability": float(probability.max()),
        "meanProbability": float(probability.mean()), "inferenceSeconds": inference_seconds,
        "workerSeconds": time.perf_counter() - started,
        "runtime": f"torch={torch.__version__};smp={smp.__version__};pillow={pillow_version};cpu_threads={args.threads}"
    }), flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"error": type(error).__name__, "message": str(error)}), file=sys.stderr, flush=True)
        sys.exit(2)
