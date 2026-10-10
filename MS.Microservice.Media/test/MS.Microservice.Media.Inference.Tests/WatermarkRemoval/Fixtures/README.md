# Synthetic controls

These three locally generated PNGs come from the reproducible CPU proof (seed 72951) in `artifacts/media-proof/proof.py`.

- `positive-repeated.png`: synthetic scene with white repeated text at 35% opacity, rotated by -23 degrees.
- `negative-scene.png`: the same scene with no added watermark.
- `negative-subtitle.png`: the scene with ordinary subtitle content. The tested detector incorrectly selects subtitle pixels; the test preserves this evidence and requires a review warning instead of silently filtering the example.

No private photographs or model weights are included. The model integration tests run only when explicit local Python/model environment variables are supplied.
