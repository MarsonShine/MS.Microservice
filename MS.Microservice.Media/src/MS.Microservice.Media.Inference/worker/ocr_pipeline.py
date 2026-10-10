"""Bounded PP-OCRv5 mobile DB rectangles and greedy CTC decoding; no network access."""
import hashlib
import math
from pathlib import Path

ARTIFACTS = (
    ("ch_PP-OCRv5_det_mobile.onnx", 4819576, "4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae"),
    ("ch_PP-OCRv5_rec_mobile.onnx", 16631306, "5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5"),
    ("ppocrv5_dict.txt", 74012, "d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b"),
)


def verify_artifacts(directory):
    snapshots = []
    for name, size, expected in ARTIFACTS:
        path = Path(directory) / name
        with path.open("rb") as source:
            payload = source.read(size + 1)
        if len(payload) != size:
            raise ValueError(f"OCR artifact size mismatch: {name}")
        if hashlib.sha256(payload).hexdigest() != expected:
            raise ValueError(f"OCR artifact SHA-256 mismatch: {name}")
        snapshots.append(payload)
    return snapshots


def decode_ctc(probabilities, characters):
    import numpy as np
    if probabilities.ndim != 2 or probabilities.shape[1] != len(characters) or not np.isfinite(probabilities).all():
        raise ValueError("Invalid OCR recognition tensor or character dictionary.")
    tokens = probabilities.argmax(axis=1)
    selected = (tokens != 0) & np.r_[True, tokens[1:] != tokens[:-1]]
    text = "".join(characters[int(index)] for index in tokens[selected])
    score = float(probabilities.max(axis=1)[selected].mean()) if text else 0.0
    if not 0 <= score <= 1:
        raise ValueError("Recognition output is not a probability tensor.")
    return text, score


class OcrEngine:
    def __init__(self, model_directory, threads):
        snapshots = verify_artifacts(model_directory)
        import onnxruntime as ort
        options = ort.SessionOptions()
        options.intra_op_num_threads = threads
        options.inter_op_num_threads = 1
        options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
        self.detector = ort.InferenceSession(snapshots[0], options, providers=["CPUExecutionProvider"])
        self.recognizer = ort.InferenceSession(snapshots[1], options, providers=["CPUExecutionProvider"])
        dictionary = snapshots[2].decode("utf-8").splitlines()
        if self.recognizer.get_modelmeta().custom_metadata_map.get("character", "").splitlines() != dictionary:
            raise ValueError("Recognition model metadata does not match the verified dictionary.")
        self.characters = ["blank", *dictionary, " "]

    def recognize(self, image, options):
        import cv2
        import numpy as np
        height, width = image.shape[:2]
        ratio = min(1.0, options.det_side / max(height, width))
        resized_height = max(32, int(round(height * ratio / 32)) * 32)
        resized_width = max(32, int(round(width * ratio / 32)) * 32)
        normalized = cv2.resize(image, (resized_width, resized_height)).astype(np.float32) / 127.5 - 1
        prediction = self.detector.run(None, {"x": normalized.transpose(2, 0, 1)[None]})[0]
        if prediction.shape != (1, 1, resized_height, resized_width) or not np.isfinite(prediction).all():
            raise ValueError("Invalid OCR detection tensor.")
        probability = prediction[0, 0]
        bitmap = cv2.dilate((probability > options.det_threshold).astype(np.uint8), np.ones((2, 2), np.uint8))
        contours, _ = cv2.findContours(bitmap, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
        if len(contours) > 4096:
            raise ValueError("OCR detection exceeds the contour budget.")
        boxes = []
        for contour in contours:
            (cx, cy), (a, b), angle = cv2.minAreaRect(contour)
            if min(a, b) < 3:
                continue
            rectangle = cv2.boxPoints(((cx, cy), (a, b), angle))
            x0, y0 = np.maximum(np.floor(rectangle.min(axis=0)).astype(int), 0)
            x1, y1 = np.minimum(np.ceil(rectangle.max(axis=0)).astype(int), [resized_width - 1, resized_height - 1])
            mask = np.zeros((y1 - y0 + 1, x1 - x0 + 1), np.uint8)
            cv2.fillPoly(mask, [(rectangle - [x0, y0]).astype(np.int32)], 1)
            if cv2.mean(probability[y0:y1 + 1, x0:x1 + 1], mask)[0] < options.box_threshold:
                continue
            # Rectangle offset at DB's area/perimeter distance; this intentionally does not recover curved text polygons.
            distance = a * b * 1.6 / (2 * (a + b))
            rectangle = cv2.boxPoints(((cx, cy), (a + 2 * distance, b + 2 * distance), angle))
            rectangle[:, 0] = np.clip(rectangle[:, 0] * width / resized_width, 0, width - 1)
            rectangle[:, 1] = np.clip(rectangle[:, 1] * height / resized_height, 0, height - 1)
            points = rectangle[np.argsort(rectangle[:, 0])]
            left = points[:2][np.argsort(points[:2, 1])]
            right = points[2:][np.argsort(points[2:, 1])]
            boxes.append(np.float32([left[0], right[0], right[1], left[1]]))
            if len(boxes) > options.max_regions:
                raise ValueError("OCR detection exceeds the region budget.")
        boxes.sort(key=lambda box: (float(box[:, 1].min()), float(box[:, 0].min())))
        regions = []
        characters = 0
        for points in boxes:
            crop_width = max(1, round(max(np.linalg.norm(points[1] - points[0]), np.linalg.norm(points[2] - points[3]))))
            crop_height = max(1, round(max(np.linalg.norm(points[3] - points[0]), np.linalg.norm(points[2] - points[1]))))
            if crop_width < 2 or crop_height < 2:
                continue
            if max(crop_width, crop_height) > options.max_dimension or crop_width * crop_height > options.max_pixels:
                raise ValueError("A rectified text region exceeds the raster limits.")
            recognition_width = max(320, math.ceil(crop_width / crop_height * 48))
            if recognition_width > options.rec_width:
                raise ValueError("A text region exceeds the recognition width budget; split the image or raise the bounded limit.")
            transform = cv2.getPerspectiveTransform(points, np.float32([[0, 0], [crop_width - 1, 0], [crop_width - 1, crop_height - 1], [0, crop_height - 1]]))
            crop = cv2.warpPerspective(image, transform, (crop_width, crop_height), borderMode=cv2.BORDER_REPLICATE)
            resized_width = math.ceil(crop_width / crop_height * 48)
            tensor = np.zeros((1, 3, 48, recognition_width), np.float32)
            tensor[0, :, :, :resized_width] = (cv2.resize(crop, (resized_width, 48)).astype(np.float32) / 127.5 - 1).transpose(2, 0, 1)
            output = self.recognizer.run(None, {"x": tensor})[0]
            if output.ndim != 3 or output.shape[0] != 1:
                raise ValueError("Invalid OCR recognition output dimensions.")
            text, confidence = decode_ctc(output[0], self.characters)
            if not text or confidence < options.min_confidence:
                continue
            characters += len(text.encode("utf-16-le")) // 2
            if characters > options.max_characters:
                raise ValueError("OCR exceeds the text character budget.")
            regions.append({"text": text, "confidence": confidence,
                            "corners": [{"x": float(x), "y": float(y)} for x, y in points]})
        return {"width": width, "height": height, "regions": regions}
