"""One bounded ruled-table request using the existing OCR model pipeline."""
import sys

from ocr_pipeline import OcrEngine
from ocr_worker import create_parser, validate_options, load_image, print_result
from table_pipeline import detect_tables, assign_regions


def main():
    parser = create_parser()
    for name in ("line-length", "line-tolerance", "max-tables", "max-rows", "max-columns", "max-grid-cells"):
        parser.add_argument("--" + name, type=int, required=True)
    parser.add_argument("--boundary-coverage", type=float, required=True)
    options = parser.parse_args()
    validate_options(options)
    if not (8 <= options.line_length <= 512 and 1 <= options.line_tolerance <= 8 and .75 <= options.boundary_coverage <= 1
            and 1 <= options.max_tables <= 16 and 1 <= options.max_rows <= 128 and 1 <= options.max_columns <= 128 and 1 <= options.max_grid_cells <= 2048):
        raise ValueError("Invalid table geometry or output budgets.")
    tables, clean = detect_tables(load_image(options), options)
    ocr = OcrEngine(options.models, options.threads).recognize(clean, options)
    print_result(assign_regions(ocr, tables))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Table recognition failed: {type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(1)
