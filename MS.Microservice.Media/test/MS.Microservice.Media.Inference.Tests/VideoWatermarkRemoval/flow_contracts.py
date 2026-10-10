"""Regression invariants for the packaged flow and rational rectangle layers, without model calls."""
import sys
from fractions import Fraction
import numpy as np
sys.path.insert(0, sys.argv[1])
from video_flow import fuse
from video_watermark_worker import masks

settings = {"maximumForwardBackwardError": 1.5, "maximumKnownRegionError": 32, "minimumTexturedFraction": .01,
            "minimumGradient": 4, "generatedNeighborWeight": .25}
height, width = 96, 128
yy, xx = np.mgrid[:height, :width]
photo = np.stack([(xx * 3 + yy * 2) % 256, (xx * 7 + yy) % 256, (xx + yy * 5) % 256], axis=2).astype(np.uint8)
selected = np.zeros((height, width), bool)
selected[35:60, 40:88] = True
noise = np.random.default_rng(20261010).integers(0, 256, (height, width, 3), dtype=np.uint8)
checker = np.repeat((((xx + yy) // 5 % 2) * 255).astype(np.uint8)[:, :, None], 3, axis=2)


def state(reference, mask):
    image = reference.copy()
    image[mask] = 255
    repaired = image.copy()
    repaired[mask] = 73
    return {"image": image, "mask": mask, "repair": repaired}


for name, current, neighbor, mask, accepted in [
    ("red_blue_cut", np.full(photo.shape, (20, 20, 220), np.uint8), np.full(photo.shape, (220, 20, 20), np.uint8), selected, False),
    ("inversion_cut", photo, 255 - photo, selected, False),
    ("checker_noise_cut", checker, noise, selected, False),
    ("textureless", np.full(photo.shape, 100, np.uint8), np.full(photo.shape, 100, np.uint8), selected, False),
    ("full_mask", photo, photo, np.ones(selected.shape, bool), False),
    ("textured_same", photo, photo, selected, True),
]:
    current_state, other = state(current, mask), state(neighbor, mask)
    other["repair"][mask] = 31
    result, diagnostics = fuse(current_state, [other], settings)
    assert np.array_equal(result[~mask], current_state["image"][~mask]), name
    if accepted:
        assert diagnostics[0]["contributedPixels"] > 0 and not np.array_equal(result, current_state["repair"]), name
    else:
        assert diagnostics[0]["contributedPixels"] == 0 and np.array_equal(result, current_state["repair"]), name

empty = state(photo, np.zeros(selected.shape, bool))
assert np.array_equal(fuse(empty, [state(noise, selected)], settings)[0], photo)
border = np.zeros(selected.shape, bool)
border[:5, :5] = border[-5:, -5:] = True
current = state(photo, border)
assert np.array_equal(fuse(current, [current], settings)[0][~border], photo[~border])


def rectangle(x, w):
    return {"x": x, "y": 0, "width": w, "height": 10}


for start, end, time, expected in [
    (rectangle(0, 10), rectangle(10, 20), Fraction(40, 1000), 140),
    (rectangle(0, 10), rectangle(10, 20), Fraction(33, 1000), 140),
    (rectangle(118, 10), rectangle(0, 20), Fraction(30, 1000), 140),
    (rectangle(0, 10), rectangle(10, 20), Fraction(1, 10), 200),
    (rectangle(0, 10), rectangle(10, 20), Fraction(5, 10), 200),
]:
    mask_at = masks({"staticMaskPath": None, "rectangleKeyframes": [
        {"timeTicks": 0, "rectangles": [start]}, {"timeTicks": 1_000_000, "rectangles": [end]}]}, (height, width))
    assert int(mask_at(time).sum()) == expected, (time, expected)
print("8 flow and 5 rational-coordinate contract cases passed")
