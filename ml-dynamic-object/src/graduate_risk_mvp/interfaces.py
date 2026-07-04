"""Replaceable interfaces for the MVP pipeline."""

from __future__ import annotations

from abc import ABC, abstractmethod
from collections.abc import Iterable, Sequence
from collections.abc import Mapping

from .models import (
    AssessedObject,
    Detection,
    Frame,
    MotionEstimate,
    PipelineResult,
    RelativeLocation,
    Risk,
    TrackedObject,
)


class CameraSource(ABC):
    @abstractmethod
    def frames(self) -> Iterable[Frame]:
        """Yield input frames from a camera, video, or synthetic source."""


class Detector(ABC):
    @abstractmethod
    def detect(self, frame: Frame) -> Sequence[Detection]:
        """Return object detections for one frame."""


class Tracker(ABC):
    @abstractmethod
    def update(self, detections: Sequence[Detection]) -> Sequence[TrackedObject]:
        """Associate detections with stable track IDs."""


class LocationEstimator(ABC):
    @abstractmethod
    def estimate(self, tracked: TrackedObject) -> RelativeLocation:
        """Estimate an object's relative location from its normalized bbox."""


class MotionEstimator(ABC):
    @abstractmethod
    def update(
        self,
        tracked_objects: Sequence[TrackedObject],
        timestamp_ms: int,
    ) -> Mapping[int, MotionEstimate]:
        """Estimate relative motion from the recent history of each track."""


class RiskEstimator(ABC):
    @abstractmethod
    def estimate(
        self,
        tracked: TrackedObject,
        location: RelativeLocation,
        motion: MotionEstimate,
    ) -> Risk:
        """Estimate an object's risk."""


class RiskOutputPublisher(ABC):
    @abstractmethod
    def publish(self, result: PipelineResult) -> str:
        """Publish and return a serialized pipeline result."""


def assess(
    tracked: TrackedObject,
    motion: MotionEstimate,
    location: RelativeLocation,
    risk: Risk,
) -> AssessedObject:
    """Small constructor helper useful to adapters."""

    return AssessedObject(
        tracked=tracked,
        motion=motion,
        location=location,
        risk=risk,
    )
