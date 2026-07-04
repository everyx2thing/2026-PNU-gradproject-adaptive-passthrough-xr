"""A lightweight center-point tracker suitable for mock input."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass
from math import hypot

from .interfaces import Tracker
from .models import BBoxNorm, Detection, TrackedObject


@dataclass
class _TrackState:
    label: str
    bbox: BBoxNorm
    missed_frames: int = 0


class SimpleTracker(Tracker):
    """Greedily matches same-label detections by bbox center distance."""

    def __init__(
        self, *, max_center_distance: float = 0.2, max_missed_frames: int = 2
    ) -> None:
        self._max_center_distance = max_center_distance
        self._max_missed_frames = max_missed_frames
        self._next_track_id = 1
        self._tracks: dict[int, _TrackState] = {}

    def update(self, detections: Sequence[Detection]) -> Sequence[TrackedObject]:
        for state in self._tracks.values():
            state.missed_frames += 1

        used_track_ids: set[int] = set()
        output: list[TrackedObject] = []

        for detection in detections:
            track_id = self._nearest_track(detection, used_track_ids)
            previous_bbox: BBoxNorm | None = None

            if track_id is None:
                track_id = self._next_track_id
                self._next_track_id += 1
                self._tracks[track_id] = _TrackState(
                    label=detection.label, bbox=detection.bbox
                )
            else:
                state = self._tracks[track_id]
                previous_bbox = state.bbox
                state.bbox = detection.bbox
                state.missed_frames = 0

            used_track_ids.add(track_id)
            growth_rate = self._area_growth(previous_bbox, detection.bbox)
            output.append(
                TrackedObject(
                    track_id=track_id,
                    detection=detection,
                    previous_bbox=previous_bbox,
                    area_growth_rate=growth_rate,
                )
            )

        expired = [
            track_id
            for track_id, state in self._tracks.items()
            if state.missed_frames > self._max_missed_frames
        ]
        for track_id in expired:
            del self._tracks[track_id]

        return tuple(output)

    def _nearest_track(
        self, detection: Detection, used_track_ids: set[int]
    ) -> int | None:
        candidates: list[tuple[float, int]] = []
        for track_id, state in self._tracks.items():
            if track_id in used_track_ids or state.label != detection.label:
                continue
            distance = hypot(
                state.bbox.cx - detection.bbox.cx,
                state.bbox.cy - detection.bbox.cy,
            )
            if distance <= self._max_center_distance:
                candidates.append((distance, track_id))
        return min(candidates)[1] if candidates else None

    @staticmethod
    def _area_growth(previous: BBoxNorm | None, current: BBoxNorm) -> float:
        if previous is None:
            return 0.0
        return (current.area - previous.area) / previous.area

