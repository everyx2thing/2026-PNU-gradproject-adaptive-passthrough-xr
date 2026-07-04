"""Track-history based relative approach estimation."""

from __future__ import annotations

from collections import deque
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from math import log, sqrt
from statistics import median

from .interfaces import MotionEstimator
from .models import MotionEstimate, TrackedObject


@dataclass(frozen=True)
class _Observation:
    timestamp_ms: int
    scale: float
    center_distance: float


class HistoryMotionEstimator(MotionEstimator):
    """Estimates approach from a robust trend over multiple bbox observations."""

    def __init__(
        self,
        *,
        history_window_ms: int = 1500,
        minimum_observations: int = 4,
        minimum_observation_ms: int = 250,
        approach_rate_threshold: float = 0.04,
        receding_rate_threshold: float = -0.04,
        smoothing_alpha: float = 0.35,
    ) -> None:
        self._history_window_ms = history_window_ms
        self._minimum_observations = minimum_observations
        self._minimum_observation_ms = minimum_observation_ms
        self._approach_rate_threshold = approach_rate_threshold
        self._receding_rate_threshold = receding_rate_threshold
        self._smoothing_alpha = smoothing_alpha
        self._histories: dict[int, deque[_Observation]] = {}
        self._smoothed_rates: dict[int, float] = {}
        self._last_seen_ms: dict[int, int] = {}

    def update(
        self,
        tracked_objects: Sequence[TrackedObject],
        timestamp_ms: int,
    ) -> Mapping[int, MotionEstimate]:
        self._remove_expired_tracks(timestamp_ms)
        estimates: dict[int, MotionEstimate] = {}

        for tracked in tracked_objects:
            track_id = tracked.track_id
            bbox = tracked.detection.bbox
            history = self._histories.setdefault(track_id, deque())
            self._last_seen_ms[track_id] = timestamp_ms

            if not history or timestamp_ms > history[-1].timestamp_ms:
                history.append(
                    _Observation(
                        timestamp_ms=timestamp_ms,
                        # Geometric bbox scale still changes when height is
                        # clipped by the frame boundary during close approach.
                        scale=max(sqrt(bbox.area), 1e-6),
                        center_distance=abs(bbox.cx - 0.5),
                    )
                )

            cutoff_ms = timestamp_ms - self._history_window_ms
            while history and history[0].timestamp_ms < cutoff_ms:
                history.popleft()

            estimates[track_id] = self._estimate(track_id, history)

        return estimates

    def _estimate(
        self,
        track_id: int,
        history: deque[_Observation],
    ) -> MotionEstimate:
        sample_count = len(history)
        observation_ms = (
            history[-1].timestamp_ms - history[0].timestamp_ms
            if sample_count >= 2
            else 0
        )
        raw_scale_rate = self._median_log_rate(history, "scale")
        center_rate = self._median_linear_rate(history, "center_distance")

        previous_rate = self._smoothed_rates.get(track_id)
        scale_rate = (
            raw_scale_rate
            if previous_rate is None
            else self._smoothing_alpha * raw_scale_rate
            + (1.0 - self._smoothing_alpha) * previous_rate
        )
        self._smoothed_rates[track_id] = scale_rate

        enough_history = (
            sample_count >= self._minimum_observations
            and observation_ms >= self._minimum_observation_ms
        )
        reliability = min(
            1.0,
            min(sample_count / max(self._minimum_observations + 2, 1), 1.0)
            * min(
                observation_ms / max(self._minimum_observation_ms * 2, 1),
                1.0,
            ),
        )

        if not enough_history:
            state = "unknown"
        elif scale_rate >= self._approach_rate_threshold:
            state = "approaching"
        elif scale_rate <= self._receding_rate_threshold:
            state = "receding"
        else:
            state = "steady"

        ttc = 1.0 / scale_rate if state == "approaching" else None
        return MotionEstimate(
            state=state,
            scale_rate_per_sec=round(scale_rate, 3),
            ttc_sec_approx=round(ttc, 2) if ttc is not None else None,
            center_approach_rate_per_sec=round(max(-center_rate, 0.0), 3),
            sample_count=sample_count,
            observation_ms=observation_ms,
            reliability=round(reliability, 3),
        )

    @staticmethod
    def _median_log_rate(
        history: deque[_Observation], attribute: str
    ) -> float:
        rates: list[float] = []
        for previous, current in zip(history, list(history)[1:]):
            elapsed_seconds = (
                current.timestamp_ms - previous.timestamp_ms
            ) / 1000.0
            if elapsed_seconds <= 0:
                continue
            previous_value = max(getattr(previous, attribute), 1e-6)
            current_value = max(getattr(current, attribute), 1e-6)
            rates.append(
                (log(current_value) - log(previous_value)) / elapsed_seconds
            )
        return median(rates) if rates else 0.0

    @staticmethod
    def _median_linear_rate(
        history: deque[_Observation], attribute: str
    ) -> float:
        rates: list[float] = []
        for previous, current in zip(history, list(history)[1:]):
            elapsed_seconds = (
                current.timestamp_ms - previous.timestamp_ms
            ) / 1000.0
            if elapsed_seconds <= 0:
                continue
            rates.append(
                (getattr(current, attribute) - getattr(previous, attribute))
                / elapsed_seconds
            )
        return median(rates) if rates else 0.0

    def _remove_expired_tracks(self, timestamp_ms: int) -> None:
        expired = [
            track_id
            for track_id, last_seen_ms in self._last_seen_ms.items()
            if timestamp_ms - last_seen_ms > self._history_window_ms * 2
        ]
        for track_id in expired:
            self._histories.pop(track_id, None)
            self._smoothed_rates.pop(track_id, None)
            self._last_seen_ms.pop(track_id, None)
