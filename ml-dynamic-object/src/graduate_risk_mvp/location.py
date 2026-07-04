"""Relative location estimation using only a normalized bounding box."""

from __future__ import annotations

from .interfaces import LocationEstimator
from .models import RelativeLocation, TrackedObject


class RelativeLocationEstimator(LocationEstimator):
    """Maps image position and bbox area to coarse user-relative zones."""

    def __init__(
        self,
        *,
        horizontal_fov_deg: float = 150.0,
        far_area_threshold: float = 0.03,
        near_area_threshold: float = 0.06,
    ) -> None:
        self._horizontal_fov_deg = horizontal_fov_deg
        self._far_area_threshold = far_area_threshold
        self._near_area_threshold = near_area_threshold

    def estimate(self, tracked: TrackedObject) -> RelativeLocation:
        bbox = tracked.detection.bbox
        horizontal = self._horizontal_zone(bbox.cx)
        vertical = self._third(bbox.cy, "top", "center", "bottom")

        if bbox.area < self._far_area_threshold:
            distance_band = "far"
        elif bbox.area < self._near_area_threshold:
            distance_band = "mid"
        else:
            distance_band = "near"

        return RelativeLocation(
            screen_zone=f"{vertical}-{horizontal}",
            user_zone=f"front-{horizontal}",
            distance_band=distance_band,
            bearing_deg_approx=round(
                (bbox.cx - 0.5) * self._horizontal_fov_deg, 1
            ),
        )

    @staticmethod
    def _third(value: float, low: str, middle: str, high: str) -> str:
        if value < 1.0 / 3.0:
            return low
        if value < 2.0 / 3.0:
            return middle
        return high

    @staticmethod
    def _horizontal_zone(value: float) -> str:
        if value < 0.4:
            return "left"
        if value <= 0.6:
            return "center"
        return "right"
