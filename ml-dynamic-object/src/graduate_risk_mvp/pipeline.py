"""Pipeline orchestration."""

from __future__ import annotations

from collections.abc import Iterable

from .interfaces import (
    CameraSource,
    Detector,
    LocationEstimator,
    MotionEstimator,
    RiskEstimator,
    Tracker,
)
from .models import AssessedObject, Frame, PipelineResult


class RiskPipeline:
    def __init__(
        self,
        *,
        detector: Detector,
        tracker: Tracker,
        motion_estimator: MotionEstimator,
        location_estimator: LocationEstimator,
        risk_estimator: RiskEstimator,
    ) -> None:
        self._detector = detector
        self._tracker = tracker
        self._motion_estimator = motion_estimator
        self._location_estimator = location_estimator
        self._risk_estimator = risk_estimator

    def process_frame(self, frame: Frame) -> PipelineResult:
        detections = self._detector.detect(frame)
        tracked_objects = self._tracker.update(detections)
        motion_by_track = self._motion_estimator.update(
            tracked_objects, frame.timestamp_ms
        )
        assessed: list[AssessedObject] = []

        for tracked in tracked_objects:
            motion = motion_by_track[tracked.track_id]
            location = self._location_estimator.estimate(tracked)
            risk = self._risk_estimator.estimate(tracked, location, motion)
            assessed.append(
                AssessedObject(
                    tracked=tracked,
                    motion=motion,
                    location=location,
                    risk=risk,
                )
            )

        return PipelineResult(
            timestamp_ms=frame.timestamp_ms,
            objects=tuple(assessed),
        )

    def run(self, source: CameraSource) -> Iterable[PipelineResult]:
        for frame in source.frames():
            yield self.process_frame(frame)
