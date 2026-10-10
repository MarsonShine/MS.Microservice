"""Closed axis-aligned ruled grids, rectangular merges, and unique OCR-region assignment."""
import cv2
import numpy as np


def cluster(values, tolerance):
    groups = []
    for value in sorted(values):
        if not groups or value - groups[-1][-1] > tolerance:
            groups.append([value])
        else:
            groups[-1].append(value)
    return [round(sum(group) / len(group)) for group in groups]


def line_segments(mask, network_labels, horizontal, options):
    count, _, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    if count > 4097:
        raise ValueError("Table detection exceeds the line component budget.")
    segments = {}
    for x, y, width, height, _ in stats[1:]:
        length, thickness = (width, height) if horizontal else (height, width)
        if length >= options.line_length and thickness <= 2 * options.line_tolerance + 1:
            py, px = np.argwhere(mask[y:y + height, x:x + width] != 0)[0]
            network = int(network_labels[y + py, x + px])
            segment = ((round(y + (height - 1) / 2), int(x), int(x + width - 1)) if horizontal
                       else (round(x + (width - 1) / 2), int(y), int(y + height - 1)))
            segments.setdefault(network, []).append(segment)
    return segments


def grid_cells(xs, ys, horizontal, vertical, options):
    rows, columns = len(ys) - 1, len(xs) - 1
    tolerance = options.line_tolerance

    def boundary(mask, coordinate, start, end, transpose=False):
        if end - start <= 2 * tolerance + 2:
            return None
        if transpose:
            patch = mask[start + tolerance + 1:end - tolerance, max(0, coordinate - tolerance):coordinate + tolerance + 1].T
        else:
            patch = mask[max(0, coordinate - tolerance):coordinate + tolerance + 1, start + tolerance + 1:end - tolerance]
        coverage = float(np.any(patch != 0, axis=0).mean())
        if coverage >= options.boundary_coverage:
            return True
        if coverage <= 1 - options.boundary_coverage:
            return False
        return None

    h = [[boundary(horizontal, y, xs[c], xs[c + 1]) for c in range(columns)] for y in ys]
    v = [[boundary(vertical, x, ys[r], ys[r + 1], True) for x in xs] for r in range(rows)]
    if any(value is None for line in h + v for value in line) or not all(h[0] + h[-1] + [line[0] for line in v] + [line[-1] for line in v]):
        return None
    remaining = {(r, c) for r in range(rows) for c in range(columns)}
    cells = []
    while remaining:
        first = min(remaining)
        group, pending = set(), [first]
        while pending:
            r, c = pending.pop()
            if (r, c) not in remaining:
                continue
            remaining.remove((r, c))
            group.add((r, c))
            for nr, nc, open_edge in ((r - 1, c, r > 0 and not h[r][c]), (r + 1, c, r + 1 < rows and not h[r + 1][c]),
                                      (r, c - 1, c > 0 and not v[r][c]), (r, c + 1, c + 1 < columns and not v[r][c + 1])):
                if open_edge:
                    pending.append((nr, nc))
        r0, r1 = min(r for r, _ in group), max(r for r, _ in group) + 1
        c0, c1 = min(c for _, c in group), max(c for _, c in group) + 1
        if len(group) != (r1 - r0) * (c1 - c0):
            return None
        if any(h[r][c] for r in range(r0 + 1, r1) for c in range(c0, c1)) or any(v[r][c] for r in range(r0, r1) for c in range(c0 + 1, c1)):
            return None
        cells.append({"row": r0, "column": c0, "rowSpan": r1 - r0, "columnSpan": c1 - c0,
                      "bounds": {"x": xs[c0], "y": ys[r0], "width": xs[c1] - xs[c0], "height": ys[r1] - ys[r0]},
                      "text": "", "regionIndices": []})
    return sorted(cells, key=lambda cell: (cell["row"], cell["column"]))


def detect_tables(image, options):
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    binary = cv2.adaptiveThreshold(gray, 255, cv2.ADAPTIVE_THRESH_MEAN_C, cv2.THRESH_BINARY_INV, 31, 15)
    horizontal = cv2.morphologyEx(binary, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_RECT, (options.line_length, 1)))
    vertical = cv2.morphologyEx(binary, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_RECT, (1, options.line_length)))
    union = horizontal | vertical
    count, labels, stats, _ = cv2.connectedComponentsWithStats(union, 8)
    if count > 4097:
        raise ValueError("Table detection exceeds its candidate network budget.")
    h_segments = line_segments(horizontal, labels, True, options)
    v_segments = line_segments(vertical, labels, False, options)
    tables, total_cells = [], 0
    line_mask = np.zeros(gray.shape, np.uint8)
    for network, (x, y, width, height, _) in enumerate(stats[1:], 1):
        xs = cluster([p for p, _, _ in v_segments.get(network, [])], options.line_tolerance)
        ys = cluster([p for p, _, _ in h_segments.get(network, [])], options.line_tolerance)
        if len(xs) < 2 or len(ys) < 2:
            continue
        rows, columns = len(ys) - 1, len(xs) - 1
        envelope_tolerance = 2 * options.line_tolerance + 1
        if rows * columns < 2 or any(abs(a - b) > envelope_tolerance for a, b in
                                    zip((xs[0], ys[0], xs[-1], ys[-1]), (x, y, x + width - 1, y + height - 1))):
            continue
        if min(np.diff(xs)) < options.line_length or min(np.diff(ys)) < options.line_length:
            continue
        if rows > options.max_rows or columns > options.max_columns or rows * columns > options.max_grid_cells:
            raise ValueError("Table detection exceeds its grid dimensions or cell budget.")
        cells = grid_cells(xs, ys, horizontal, vertical, options)
        if cells is None:
            continue
        total_cells += rows * columns
        if len(tables) >= options.max_tables or total_cells > options.max_grid_cells:
            raise ValueError("Table detection exceeds its total table or cell budget.")
        tables.append({"rowCount": rows, "columnCount": columns,
                       "bounds": {"x": xs[0], "y": ys[0], "width": xs[-1] - xs[0], "height": ys[-1] - ys[0]}, "cells": cells})
        line_mask[y:y + height, x:x + width] |= np.where(labels[y:y + height, x:x + width] == network, union[y:y + height, x:x + width], 0).astype(np.uint8)
    tables.sort(key=lambda table: (table["bounds"]["y"], table["bounds"]["x"]))
    clean = image.copy()
    clean[cv2.dilate(line_mask, np.ones((3, 3), np.uint8)) != 0] = 255
    return tables, clean


def assign_regions(ocr, tables):
    unassigned = []
    cells = [cell for table in tables for cell in table["cells"]]
    for index, region in enumerate(ocr["regions"]):
        polygon = np.float32([[p["x"], p["y"]] for p in region["corners"]])
        area = abs(cv2.contourArea(polygon))
        candidates = []
        for cell in cells:
            b = cell["bounds"]
            rectangle = np.float32([[b["x"], b["y"]], [b["x"] + b["width"], b["y"]],
                                    [b["x"] + b["width"], b["y"] + b["height"]], [b["x"], b["y"] + b["height"]]])
            overlap, _ = cv2.intersectConvexConvex(polygon, rectangle)
            if area > 0 and overlap / area >= .8:
                candidates.append(cell)
        if len(candidates) == 1:
            candidates[0]["regionIndices"].append(index)
        else:
            unassigned.append(index)
    for cell in cells:
        cell["regionIndices"].sort(key=lambda index: (min(p["y"] for p in ocr["regions"][index]["corners"]), min(p["x"] for p in ocr["regions"][index]["corners"])))
        cell["text"] = "\n".join(ocr["regions"][index]["text"] for index in cell["regionIndices"])
    return {"ocr": ocr, "tables": tables, "unassignedRegionIndices": unassigned}
