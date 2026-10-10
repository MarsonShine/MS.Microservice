"""Shared fixed repeated-text checkpoint and RGB/512 preprocessing for image and video workers."""
import hashlib
import importlib.metadata
import io
import json
import time
from pathlib import Path


def read_verified_checkpoint(path):
    manifest = json.loads(Path(__file__).with_name("modelmanifest.json").read_text(encoding="utf-8"))
    for package in ["segmentation-models-pytorch", "timm"]:
        observed = importlib.metadata.version(package)
        if observed != manifest[package]:
            raise RuntimeError(f"Expected {package}=={manifest[package]}, found {observed}")
    with open(path, "rb") as source:
        checkpoint = source.read(manifest["bytes"] + 1)
    if len(checkpoint) != manifest["bytes"] or hashlib.sha256(checkpoint).hexdigest() != manifest["sha256"]:
        raise ValueError("The watermark checkpoint length or SHA-256 does not match the fixed model manifest")
    return manifest, checkpoint


class WatermarkDetector:
    def __init__(self, verified_checkpoint, profile, threshold, threads):
        import torch
        import segmentation_models_pytorch as smp
        from PIL import __version__ as pillow_version
        if profile not in ["rgb_0_1", "imagenet"] or not 0 < threshold < 1 or threads < 1:
            raise ValueError("Invalid normalization profile, probability threshold or CPU thread count")
        torch.set_num_threads(threads)
        torch.set_num_interop_threads(1)
        self.manifest, checkpoint_bytes = verified_checkpoint
        self.profile, self.threshold, self.torch = profile, threshold, torch
        self.model = smp.UnetPlusPlus(encoder_name="efficientnet-b4", encoder_weights=None, in_channels=3, classes=1)
        checkpoint = torch.load(io.BytesIO(checkpoint_bytes), map_location="cpu", weights_only=True)
        self.model.load_state_dict(checkpoint, strict=True)
        self.model.eval()
        self.runtime = f"torch={torch.__version__};smp={smp.__version__};pillow={pillow_version};cpu_threads={threads}"

    def predict(self, image):
        import numpy as np
        from PIL import Image
        width, height = image.size
        tensor = np.asarray(image.resize((512, 512), Image.Resampling.BILINEAR)).astype(np.float32) / 255
        if self.profile == "imagenet":
            tensor = (tensor - np.array([.485, .456, .406], dtype=np.float32)) / np.array([.229, .224, .225], dtype=np.float32)
        tensor = self.torch.from_numpy(tensor.transpose(2, 0, 1)[None].copy())
        started = time.perf_counter()
        with self.torch.inference_mode():
            probability = self.model(tensor).sigmoid()[0, 0].numpy()
        inference_seconds = time.perf_counter() - started
        probability = np.asarray(Image.fromarray(probability).resize((width, height), Image.Resampling.BILINEAR))
        if not np.isfinite(probability).all():
            raise ValueError("The model returned non-finite probabilities")
        selected = probability >= self.threshold
        return selected, {"modelId": self.manifest["modelId"], "sha256": self.manifest["sha256"], "profile": self.profile,
                          "threshold": self.threshold, "width": width, "height": height, "selectedPixelCount": int(selected.sum()),
                          "maximumProbability": float(probability.max()), "meanProbability": float(probability.mean()),
                          "inferenceSeconds": inference_seconds, "runtime": self.runtime}
