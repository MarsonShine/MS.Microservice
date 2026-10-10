"""Pure decoding/model-integrity boundary tests, separate from real-model end-to-end tests."""
import importlib.util
import hashlib
import os
import pathlib
import shutil
import tempfile
import types
import unittest
from unittest.mock import patch

import numpy as np

worker = pathlib.Path(__file__).resolve().parents[3] / "src/MS.Microservice.Media.Inference/worker/ocr_pipeline.py"
spec = importlib.util.spec_from_file_location("ocr_pipeline", worker)
pipeline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pipeline)


class OcrContracts(unittest.TestCase):
    def test_ctc_removes_blank_and_only_adjacent_duplicate_tokens(self):
        tokens = [0, 1, 1, 0, 1, 2, 2, 0]
        probabilities = np.zeros((len(tokens), 3), np.float32)
        probabilities[np.arange(len(tokens)), tokens] = 0.9
        self.assertEqual(pipeline.decode_ctc(probabilities, ["blank", "A", "\u4e2d"])[0], "AA\u4e2d")

    def test_empty_decoding_is_zero_confidence(self):
        self.assertEqual(pipeline.decode_ctc(np.array([[1, 0], [1, 0]], np.float32), ["blank", "A"]), ("", 0.0))

    def test_space_is_a_real_character_and_survives_blank_separation(self):
        tokens = [1, 2, 2, 0, 2, 1]
        probabilities = np.zeros((len(tokens), 3), np.float32)
        probabilities[np.arange(len(tokens)), tokens] = 1
        self.assertEqual(pipeline.decode_ctc(probabilities, ["blank", "A", " "]), ("A  A", 1.0))

    def test_confidence_uses_only_emitted_tokens(self):
        probabilities = np.array([[1, 0, 0], [0, .8, .2], [0, .99, .01], [1, 0, 0], [0, .1, .9]], np.float32)
        text, score = pipeline.decode_ctc(probabilities, ["blank", "A", "B"])
        self.assertEqual(text, "AB")
        self.assertAlmostEqual(score, .85, places=6)

    def test_invalid_tensor_dictionary_and_probabilities_fail(self):
        for tensor in [np.zeros((2, 3)), np.array([[0, np.nan]]), np.array([[0, np.inf]]), np.array([[0, 2.]])]:
            with self.subTest(tensor=tensor), self.assertRaises(ValueError):
                pipeline.decode_ctc(tensor, ["blank", "A"])

    def test_wrong_size_artifact_is_rejected_before_runtime_load(self):
        with tempfile.TemporaryDirectory() as root:
            (pathlib.Path(root) / pipeline.ARTIFACTS[0][0]).write_bytes(b"bad")
            with self.assertRaisesRegex(ValueError, "size mismatch"):
                pipeline.verify_artifacts(root)

    def test_snapshot_checks_actual_lengths_and_hashes(self):
        expected = b"same"
        manifest = (("artifact", len(expected), hashlib.sha256(expected).hexdigest()),)
        with tempfile.TemporaryDirectory() as root, patch.object(pipeline, "ARTIFACTS", manifest):
            file = pathlib.Path(root) / "artifact"
            for payload in (b"", b"sam", b"same!", b"wrong-size-" * 1000, b"diff"):
                file.write_bytes(payload)
                with self.subTest(payload_length=len(payload)), self.assertRaises(ValueError):
                    pipeline.verify_artifacts(root)
            file.write_bytes(expected)
            snapshots = pipeline.verify_artifacts(root)
            file.write_bytes(b"replaced")
            self.assertEqual(snapshots, [expected])


@unittest.skipUnless(os.environ.get("MS_MEDIA_OCR_MODELS"), "Configure real OCR artifacts for the replacement regression.")
class OcrSnapshotIntegration(unittest.TestCase):
    def test_replacement_after_validation_cannot_change_actual_model_or_dictionary(self):
        import cv2
        import onnxruntime as ort
        from PIL import Image
        real_factory = ort.InferenceSession
        with tempfile.TemporaryDirectory() as root:
            for name, _, _ in pipeline.ARTIFACTS:
                shutil.copyfile(pathlib.Path(os.environ["MS_MEDIA_OCR_MODELS"]) / name, pathlib.Path(root) / name)
            loaded = []

            def factory(payload, *args, **kwargs):
                if not loaded:
                    for name, _, _ in pipeline.ARTIFACTS:
                        (pathlib.Path(root) / name).write_bytes(b"changed-after-validation")
                loaded.append(payload)
                return real_factory(payload, *args, **kwargs)

            with patch.object(ort, "InferenceSession", factory):
                engine = pipeline.OcrEngine(root, 2)
            self.assertTrue(all(isinstance(payload, bytes) for payload in loaded))
            options = types.SimpleNamespace(det_side=1280, rec_width=2048, max_regions=128, max_characters=16384,
                                            det_threshold=.3, box_threshold=.5, min_confidence=.5,
                                            max_pixels=40000000, max_dimension=16384)
            with Image.open(pathlib.Path(__file__).parent / "Fixtures/bilingual.png") as source:
                result = engine.recognize(cv2.cvtColor(np.asarray(source), cv2.COLOR_RGB2BGR), options)
            self.assertEqual([region["text"] for region in result["regions"]], ["Hello OCR 2026", "\u4e2d\u6587\u8bc6\u522b\u6d4b\u8bd5"])


if __name__ == "__main__":
    unittest.main()
