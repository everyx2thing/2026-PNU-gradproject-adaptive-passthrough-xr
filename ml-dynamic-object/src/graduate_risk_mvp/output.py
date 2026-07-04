"""JSON output publisher."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import TextIO

from .interfaces import RiskOutputPublisher
from .models import AssessedObject, PipelineResult


class JsonRiskOutputPublisher(RiskOutputPublisher):
    def __init__(self, stream: TextIO | None = None, *, indent: int | None = 2):
        self._stream = stream if stream is not None else sys.stdout
        self._indent = indent

    def publish(self, result: PipelineResult) -> str:
        output = self.to_dict(result)
        serialized = json.dumps(
            output, ensure_ascii=False, indent=self._indent
        )
        print(serialized, file=self._stream)
        return serialized

    @classmethod
    def to_dict(cls, result: PipelineResult) -> dict[str, object]:
        objects = [cls._object_to_dict(item) for item in result.objects]
        max_object = max(result.objects, key=lambda item: item.risk.score, default=None)

        summary = {
            "maxRiskScore": max_object.risk.score if max_object else 0.0,
            "maxRiskLevel": max_object.risk.level if max_object else "safe",
            "mainRiskZone": max_object.location.user_zone if max_object else None,
        }
        return {
            "timestampMs": result.timestamp_ms,
            "objects": objects,
            "summary": summary,
        }

    @staticmethod
    def _object_to_dict(item: AssessedObject) -> dict[str, object]:
        detection = item.tracked.detection
        bbox = detection.bbox
        return {
            "trackId": item.tracked.track_id,
            "label": detection.label,
            "confidence": detection.confidence,
            "bboxNorm": {
                "cx": bbox.cx,
                "cy": bbox.cy,
                "w": bbox.w,
                "h": bbox.h,
            },
            "relativeLocation": {
                "screenZone": item.location.screen_zone,
                "userZone": item.location.user_zone,
                "distanceBand": item.location.distance_band,
                "bearingDegApprox": item.location.bearing_deg_approx,
            },
            "motion": {
                "state": item.motion.state,
                "scaleRatePerSec": item.motion.scale_rate_per_sec,
                "ttcSecApprox": item.motion.ttc_sec_approx,
                "centerApproachRatePerSec": (
                    item.motion.center_approach_rate_per_sec
                ),
                "sampleCount": item.motion.sample_count,
                "observationMs": item.motion.observation_ms,
                "reliability": item.motion.reliability,
            },
            "risk": {
                "score": item.risk.score,
                "level": item.risk.level,
                "reasons": list(item.risk.reasons),
            },
        }


class NdjsonRiskOutputPublisher(RiskOutputPublisher):
    """Appends one compact JSON object per frame."""

    def __init__(self, path: str | Path) -> None:
        self.path = Path(path)

    def publish(self, result: PipelineResult) -> str:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        serialized = json.dumps(
            JsonRiskOutputPublisher.to_dict(result),
            ensure_ascii=False,
            separators=(",", ":"),
        )
        with self.path.open("a", encoding="utf-8") as stream:
            stream.write(serialized + "\n")
        return serialized


class LatestJsonFilePublisher(RiskOutputPublisher):
    """Atomically replaces a file with the latest pretty JSON result."""

    def __init__(self, path: str | Path, *, indent: int = 2) -> None:
        self.path = Path(path)
        self._indent = indent

    def publish(self, result: PipelineResult) -> str:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        serialized = json.dumps(
            JsonRiskOutputPublisher.to_dict(result),
            ensure_ascii=False,
            indent=self._indent,
        )
        temporary_path = self.path.with_suffix(self.path.suffix + ".tmp")
        temporary_path.write_text(serialized + "\n", encoding="utf-8")
        temporary_path.replace(self.path)
        return serialized
