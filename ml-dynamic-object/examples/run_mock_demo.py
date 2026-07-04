"""Run a three-frame, hardware-free risk pipeline demo."""

from __future__ import annotations

import sys
from pathlib import Path

# Make this example runnable directly before the package is installed.
PROJECT_SRC = Path(__file__).resolve().parents[1] / "src"
sys.path.insert(0, str(PROJECT_SRC))

from graduate_risk_mvp.camera import MockCameraSource
from graduate_risk_mvp.factory import create_default_pipeline
from graduate_risk_mvp.models import BBoxNorm, Detection
from graduate_risk_mvp.output import JsonRiskOutputPublisher


def build_mock_source() -> MockCameraSource:
    return MockCameraSource(
        [
            [
                Detection(
                    "person", 0.84, BBoxNorm(0.58, 0.48, 0.10, 0.20)
                ),
                Detection("dog", 0.75, BBoxNorm(0.20, 0.62, 0.08, 0.10)),
            ],
            [
                Detection(
                    "person", 0.86, BBoxNorm(0.60, 0.48, 0.14, 0.27)
                ),
                Detection("dog", 0.76, BBoxNorm(0.22, 0.62, 0.08, 0.10)),
            ],
            [
                Detection(
                    "person", 0.87, BBoxNorm(0.62, 0.48, 0.18, 0.35)
                ),
                Detection("dog", 0.77, BBoxNorm(0.24, 0.61, 0.08, 0.10)),
            ],
            [
                Detection(
                    "person", 0.89, BBoxNorm(0.63, 0.48, 0.22, 0.41)
                ),
                Detection("dog", 0.77, BBoxNorm(0.24, 0.61, 0.08, 0.10)),
            ],
            [
                Detection(
                    "person", 0.91, BBoxNorm(0.64, 0.48, 0.27, 0.49)
                ),
                Detection("dog", 0.77, BBoxNorm(0.24, 0.61, 0.08, 0.10)),
            ],
        ],
        start_timestamp_ms=123456,
        frame_interval_ms=100,
    )


def main() -> None:
    pipeline = create_default_pipeline()
    latest_result = None
    for latest_result in pipeline.run(build_mock_source()):
        pass

    if latest_result is None:
        raise RuntimeError("Mock source produced no frames")
    JsonRiskOutputPublisher().publish(latest_result)


if __name__ == "__main__":
    main()
