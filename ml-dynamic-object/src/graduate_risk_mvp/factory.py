"""Convenient construction of the default MVP components."""

from pathlib import Path

from .detector import FakeDetector, OpenCVFaceDetector, OnnxPersonDetector
from .location import RelativeLocationEstimator
from .motion import HistoryMotionEstimator
from .pipeline import RiskPipeline
from .risk import WeightedRiskEstimator
from .tracker import SimpleTracker


def create_default_pipeline() -> RiskPipeline:
    return RiskPipeline(
        detector=FakeDetector(),
        tracker=SimpleTracker(),
        motion_estimator=HistoryMotionEstimator(),
        location_estimator=RelativeLocationEstimator(),
        risk_estimator=WeightedRiskEstimator(),
    )


def create_opencv_pipeline(detector=None) -> RiskPipeline:
    """Build a pipeline for actual OpenCV image frames."""

    return RiskPipeline(
        detector=detector if detector is not None else OpenCVFaceDetector(),
        tracker=SimpleTracker(),
        motion_estimator=HistoryMotionEstimator(),
        location_estimator=RelativeLocationEstimator(),
        risk_estimator=WeightedRiskEstimator(),
    )


def create_person_pipeline(
    model_path: str | Path | None = None,
    *,
    confidence_threshold: float = 0.4,
) -> RiskPipeline:
    """Build the default full-person ONNX camera pipeline."""

    if model_path is None:
        model_path = (
            Path(__file__).resolve().parents[2]
            / "models"
            / "yolov8n.onnx"
        )
    return create_opencv_pipeline(
        detector=OnnxPersonDetector(
            model_path,
            confidence_threshold=confidence_threshold,
        )
    )
