"""Data contracts shared by all pipeline stages."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass(frozen=True)
class BBoxNorm:
    """A center-format bounding box with values normalized to the frame."""

    cx: float
    cy: float
    w: float
    h: float

    def __post_init__(self) -> None:
        values = (self.cx, self.cy, self.w, self.h)
        if any(value < 0.0 or value > 1.0 for value in values):
            raise ValueError("Normalized bbox values must be between 0.0 and 1.0")
        if self.w == 0.0 or self.h == 0.0:
            raise ValueError("Bounding box width and height must be positive")

    @property
    def area(self) -> float:
        return self.w * self.h


@dataclass(frozen=True)
class Frame:
    frame_id: int
    timestamp_ms: int
    payload: Any = None
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class Detection:
    label: str
    confidence: float
    bbox: BBoxNorm

    def __post_init__(self) -> None:
        if not 0.0 <= self.confidence <= 1.0:
            raise ValueError("Detection confidence must be between 0.0 and 1.0")


@dataclass(frozen=True)
class TrackedObject:
    track_id: int
    detection: Detection
    previous_bbox: BBoxNorm | None = None
    area_growth_rate: float = 0.0


@dataclass(frozen=True)
class RelativeLocation:
    screen_zone: str
    user_zone: str
    distance_band: str
    bearing_deg_approx: float


@dataclass(frozen=True)
class MotionEstimate:
    state: str
    scale_rate_per_sec: float
    ttc_sec_approx: float | None
    center_approach_rate_per_sec: float
    sample_count: int
    observation_ms: int
    reliability: float


@dataclass(frozen=True)
class Risk:
    score: float
    level: str
    reasons: tuple[str, ...]


@dataclass(frozen=True)
class AssessedObject:
    tracked: TrackedObject
    motion: MotionEstimate
    location: RelativeLocation
    risk: Risk


@dataclass(frozen=True)
class PipelineResult:
    timestamp_ms: int
    objects: tuple[AssessedObject, ...]
