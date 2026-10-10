"""Optical-flow-assisted image repairs, gated by known-region evidence. Not a learned temporal model."""
import cv2
import numpy as np


def fuse(current, neighbors, settings):
    image, mask, repaired = current["image"], current["mask"], current["repair"]
    if not mask.any():
        return repaired, []
    height, width = mask.shape
    yy, xx = np.mgrid[:height, :width].astype(np.float32)
    gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
    gradient = np.hypot(cv2.Sobel(gray, cv2.CV_32F, 1, 0), cv2.Sobel(gray, cv2.CV_32F, 0, 1))
    expanded = cv2.dilate(mask.astype(np.uint8), np.ones((3, 3), np.uint8)) != 0
    value, weights = repaired.astype(np.float32), np.ones(mask.shape, dtype=np.float32)
    diagnostics = []
    for other in neighbors:
        other_gray = cv2.cvtColor(other["image"], cv2.COLOR_RGB2GRAY)
        forward = cv2.calcOpticalFlowFarneback(gray, other_gray, None, .5, 3, 15, 3, 5, 1.2, 0)
        backward = cv2.calcOpticalFlowFarneback(other_gray, gray, None, .5, 3, 15, 3, 5, 1.2, 0)
        mx, my = xx + forward[:, :, 0], yy + forward[:, :, 1]
        back = cv2.remap(backward, mx, my, cv2.INTER_LINEAR)
        valid = (np.linalg.norm(forward + back, axis=2) <= settings["maximumForwardBackwardError"]) & (mx >= 0) & (mx < width - 1) & (my >= 0) & (my < height - 1)
        other_expanded = cv2.dilate(other["mask"].astype(np.uint8), np.ones((3, 3), np.uint8))
        known = valid & ~expanded & (cv2.remap(other_expanded, mx, my, cv2.INTER_NEAREST) == 0)
        support = int((known & (gradient >= settings["minimumGradient"])).sum())
        warped_original = cv2.remap(other["image"], mx, my, cv2.INTER_LINEAR)
        error = np.abs(warped_original.astype(np.float32) - image).mean(axis=2)
        median = float(np.median(error[known])) if known.any() else None
        reason = "accepted"
        if support < max(16, int(np.ceil(width * height * settings["minimumTexturedFraction"]))):
            reason = "insufficient_known_textured_support"
        elif median > settings["maximumKnownRegionError"]:
            reason = "known_region_photometric_mismatch"
        selected = mask & valid if reason == "accepted" else np.zeros(mask.shape, dtype=bool)
        other_selected = cv2.remap(other["mask"].astype(np.uint8), mx, my, cv2.INTER_NEAREST) != 0
        confidence = selected * np.where(other_selected, settings["generatedNeighborWeight"], 1.).astype(np.float32)
        warped_repair = cv2.remap(other["repair"], mx, my, cv2.INTER_LINEAR)
        value += warped_repair * confidence[:, :, None]
        weights += confidence
        diagnostics.append({"reason": reason, "knownPixels": int(known.sum()), "texturedKnownPixels": support,
                            "medianKnownError": median, "contributedPixels": int(selected.sum())})
    result = np.where(mask[:, :, None], np.rint(value / weights[:, :, None]).clip(0, 255).astype(np.uint8), image)
    return result, diagnostics
