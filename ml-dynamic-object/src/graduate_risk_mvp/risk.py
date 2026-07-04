"""Explainable heuristic risk estimation for the MVP."""

from __future__ import annotations

from math import hypot

from .interfaces import RiskEstimator
from .models import MotionEstimate, RelativeLocation, Risk, TrackedObject


class WeightedRiskEstimator(RiskEstimator):
    """Prioritizes persistent approach, TTC, proximity, and collision path."""

    _LABEL_RISK = {
        "car": 1.0,
        "truck": 1.0,
        "bus": 0.95,
        "motorcycle": 0.9,
        "bicycle": 0.75,
        "person": 0.65,
        "dog": 0.45,
    }

    def estimate(
        self,
        tracked: TrackedObject,
        location: RelativeLocation,
        motion: MotionEstimate,
    ) -> Risk:
        detection = tracked.detection
        bbox = detection.bbox

        proximity_factor = {
            "far": 0.15,
            "mid": 0.55,
            "near": 1.0,
        }[location.distance_band]
        approach_factor = (
            min(max(motion.scale_rate_per_sec, 0.0) / 0.15, 1.0)
            * motion.reliability
            if motion.state == "approaching"
            else 0.0
        )
        ttc_factor = self._ttc_factor(motion)
        label_factor = self._LABEL_RISK.get(detection.label.lower(), 0.3)
        center_distance = hypot(bbox.cx - 0.5, bbox.cy - 0.5)
        centrality_factor = max(0.0, 1.0 - center_distance / 0.5)
        center_motion_boost = min(
            motion.center_approach_rate_per_sec / 0.3, 1.0
        )
        collision_path_factor = min(
            centrality_factor + 0.25 * center_motion_boost, 1.0
        )

        score = (
            0.30 * proximity_factor
            + 0.30 * approach_factor
            + 0.20 * ttc_factor
            + 0.15 * collision_path_factor
            + 0.05 * label_factor
        )
        score += 0.15 * proximity_factor * approach_factor
        score *= 0.5 + 0.5 * detection.confidence
        if motion.state == "receding":
            score *= 0.55
        score = round(max(0.0, min(score, 1.0)), 3)

        reasons: list[str] = []
        if location.distance_band == "near":
            reasons.append("near_object")
        if motion.state == "approaching":
            reasons.append("approach_confirmed")
        elif motion.state == "receding":
            reasons.append("receding")
        elif motion.state == "unknown":
            reasons.append("insufficient_motion_history")
        if motion.ttc_sec_approx is not None:
            if motion.ttc_sec_approx <= 2.0:
                reasons.append("ttc_under_2s")
            elif motion.ttc_sec_approx <= 4.0:
                reasons.append("ttc_under_4s")
        if label_factor >= 0.6:
            reasons.append(detection.label.lower())
        if collision_path_factor >= 0.7:
            reasons.append("collision_corridor")
        if not reasons:
            reasons.append("low_risk")

        return Risk(
            score=score,
            level=self.level_for_score(score, motion),
            reasons=tuple(reasons),
        )

    @staticmethod
    def _ttc_factor(motion: MotionEstimate) -> float:
        if (
            motion.state != "approaching"
            or motion.ttc_sec_approx is None
        ):
            return 0.0
        if motion.ttc_sec_approx <= 2.0:
            return 1.0
        if motion.ttc_sec_approx >= 15.0:
            return 0.0
        return (15.0 - motion.ttc_sec_approx) / 13.0

    @staticmethod
    def level_for_score(score: float, motion: MotionEstimate) -> str:
        if score < 0.25:
            return "safe"
        if score < 0.50:
            return "caution"
        if score < 0.75:
            return "warning"
        return "danger"
