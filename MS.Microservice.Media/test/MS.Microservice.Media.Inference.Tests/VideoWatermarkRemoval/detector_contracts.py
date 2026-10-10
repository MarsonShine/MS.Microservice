"""Actual checkpoint loading from verified bytes, plus repeat prediction without loading again."""
import io
import shutil
import sys
import tempfile
from pathlib import Path
sys.path.insert(0, sys.argv[1])
from watermark_model import read_verified_checkpoint, WatermarkDetector
from PIL import Image
import torch

with tempfile.TemporaryDirectory(prefix="ms-watermark-checkpoint-") as directory:
    path = Path(directory) / "checkpoint.pth"
    shutil.copyfile(sys.argv[2], path)
    verified = read_verified_checkpoint(path)
    with open(path, "r+b") as changed:
        changed.seek(20)
        value = changed.read(1)
        changed.seek(20)
        changed.write(bytes([value[0] ^ 1]))
    try:
        read_verified_checkpoint(path)
        raise AssertionError("An equal-length changed checkpoint must fail SHA validation")
    except ValueError as error:
        assert "SHA-256" in str(error)
    loads, original_load = [], torch.load

    def observed_load(source, *args, **kwargs):
        assert isinstance(source, io.BytesIO) and kwargs.get("weights_only") is True
        loads.append(1)
        return original_load(source, *args, **kwargs)

    torch.load = observed_load
    detector = WatermarkDetector(verified, "rgb_0_1", .5, 2)
    image = Image.open(Path(sys.argv[3]) / "positive-repeated.png").convert("RGB")
    first, first_metadata = detector.predict(image)
    second, second_metadata = detector.predict(image)
    assert len(loads) == 1 and (first == second).all()
    assert first_metadata["selectedPixelCount"] == second_metadata["selectedPixelCount"] == 25057
    path.write_bytes(b"bad")
    try:
        read_verified_checkpoint(path)
        raise AssertionError("Truncated weights must fail before loading")
    except ValueError:
        pass
print("Verified-byte replacement, equal-length SHA mismatch, truncation and one actual model load passed")
