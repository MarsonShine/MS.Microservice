"""Real image geometry contracts; model integration lives in the C# fixture tests."""
import pathlib
import sys
import types
import unittest

import cv2
import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[3] / "src/MS.Microservice.Media.Inference/worker"))
from table_pipeline import detect_tables, assign_regions


def options(**changes):
    defaults = dict(line_length=40, line_tolerance=3, boundary_coverage=.9, max_tables=8,
                    max_rows=64, max_columns=64, max_grid_cells=512)
    defaults.update(changes)
    return types.SimpleNamespace(**defaults)


def grid():
    image = np.full((440, 700, 3), 255, np.uint8)
    for y in (40, 160, 280, 400):
        cv2.line(image, (40, y), (640, y), (0, 0, 0), 3)
    for x in (40, 240, 440, 640):
        cv2.line(image, (x, 40), (x, 400), (0, 0, 0), 3)
    return image


class TableContracts(unittest.TestCase):
    def test_normal_closed_grid_has_exact_row_column_and_pixel_geometry(self):
        tables, _ = detect_tables(grid(), options())
        self.assertEqual(len(tables), 1)
        self.assertEqual((tables[0]["rowCount"], tables[0]["columnCount"]), (3, 3))
        self.assertEqual(tables[0]["bounds"], dict(x=40, y=40, width=600, height=360))
        self.assertEqual(len(tables[0]["cells"]), 9)

    def test_each_missing_outer_border_rejects_the_whole_network(self):
        for start, end in [((40, 35), (40, 405)), ((640, 35), (640, 405)), ((35, 40), (645, 40)), ((35, 400), (645, 400))]:
            image = grid()
            cv2.line(image, start, end, (255, 255, 255), 7)
            with self.subTest(start=start):
                self.assertEqual(detect_tables(image, options())[0], [])

    def test_disconnected_text_strokes_inside_frame_are_not_grid_dividers(self):
        for start, end in [((291, 106), (332, 106)), ((100, 80), (180, 80)), ((300, 70), (300, 120))]:
            image = grid()
            cv2.line(image, start, end, (0, 0, 0), 2)
            table = detect_tables(image, options())[0]
            self.assertEqual(len(table), 1)
            self.assertEqual((table[0]["rowCount"], table[0]["columnCount"]), (3, 3))

    def test_colspan_rowspan_and_two_dimensional_merge_are_rectangular(self):
        for edges, expected in [([((240, 44), (240, 156))], (0, 0, 1, 2)),
                                ([((44, 160), (236, 160))], (0, 0, 2, 1)),
                                ([((240, 44), (240, 276)), ((44, 160), (436, 160))], (0, 0, 2, 2))]:
            image = grid()
            for start, end in edges:
                cv2.line(image, start, end, (255, 255, 255), 7)
            table = detect_tables(image, options())[0][0]
            first = table["cells"][0]
            self.assertEqual((first["row"], first["column"], first["rowSpan"], first["columnSpan"]), expected)

    def test_partial_break_nonrectangular_merge_and_dangling_internal_line_are_rejected(self):
        for edges in [[((80, 160), (150, 160))], [((240, 44), (240, 156)), ((44, 160), (236, 160))],
                      [((240, 44), (240, 276)), ((44, 160), (236, 160))]]:
            image = grid()
            for start, end in edges:
                cv2.line(image, start, end, (255, 255, 255), 7)
            self.assertEqual(detect_tables(image, options())[0], [])

    def test_blank_text_and_isolated_box_do_not_create_tables(self):
        blank = np.full((440, 700, 3), 255, np.uint8)
        text = blank.copy()
        cv2.putText(text, 'This is ordinary text', (40, 100), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 0), 2)
        box = blank.copy()
        cv2.rectangle(box, (40, 40), (640, 400), (0, 0, 0), 3)
        for image in [blank, text, box]:
            self.assertEqual(detect_tables(image, options())[0], [])

    def test_one_row_and_one_column_are_supported_grids(self):
        for vertical in (True, False):
            image = np.full((440, 700, 3), 255, np.uint8)
            cv2.rectangle(image, (40, 40), (640, 400), (0, 0, 0), 3)
            cv2.line(image, (240, 40) if vertical else (40, 160), (240, 400) if vertical else (640, 160), (0, 0, 0), 3)
            table = detect_tables(image, options())[0][0]
            self.assertEqual((table["rowCount"], table["columnCount"]), (1, 2) if vertical else (2, 1))

    def test_cell_spacing_matches_kernel_scale_at_boundary_and_when_explicitly_lowered(self):
        for spacing, line_length, succeeds in [(39, 40, False), (40, 40, True), (22, 40, False), (22, 20, True)]:
            image = np.full((200, 200, 3), 255, np.uint8)
            for coordinate in (40, 40 + spacing, 40 + spacing * 2):
                cv2.line(image, (40, coordinate), (40 + spacing * 2, coordinate), (0, 0, 0), 3)
                cv2.line(image, (coordinate, 40), (coordinate, 40 + spacing * 2), (0, 0, 0), 3)
            tables, _ = detect_tables(image, options(line_length=line_length))
            with self.subTest(spacing=spacing, line_length=line_length):
                self.assertEqual(len(tables), 1 if succeeds else 0)

    def test_grid_shaped_text_is_not_accepted_below_default_resolvable_cell_scale(self):
        image = cv2.imread(str(pathlib.Path(__file__).parent / "Fixtures/grid-glyph.png"))
        self.assertEqual(detect_tables(image, options())[0], [])

    def test_grid_budget_fails_without_truncation(self):
        for limits in [dict(max_grid_cells=8), dict(max_rows=2), dict(max_columns=2)]:
            with self.assertRaisesRegex(ValueError, "budget"):
                detect_tables(grid(), options(**limits))

    def test_cross_boundary_and_outside_ocr_remain_unassigned_exactly_once(self):
        tables, _ = detect_tables(grid(), options())
        def region(text, x0, y0, x1, y1):
            return dict(text=text, confidence=.9, corners=[dict(x=x0, y=y0), dict(x=x1, y=y0), dict(x=x1, y=y1), dict(x=x0, y=y1)])
        ocr = dict(width=700, height=440, regions=[region('inside', 60, 60, 200, 100), region('crosses', 210, 60, 270, 100), region('outside', 10, 10, 100, 25)])
        result = assign_regions(ocr, tables)
        self.assertEqual(result["tables"][0]["cells"][0]["regionIndices"], [0])
        self.assertEqual(result["unassignedRegionIndices"], [1, 2])
        self.assertEqual(sorted([i for t in tables for c in t["cells"] for i in c["regionIndices"]] + result["unassignedRegionIndices"]), [0, 1, 2])


if __name__ == "__main__":
    unittest.main()
