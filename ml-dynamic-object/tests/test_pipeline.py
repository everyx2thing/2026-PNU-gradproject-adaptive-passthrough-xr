import io
import json

from graduate_risk_mvp.camera import MockCameraSource
from graduate_risk_mvp.factory import create_default_pipeline
from graduate_risk_mvp.models import BBoxNorm, Detection
from graduate_risk_mvp.output import JsonRiskOutputPublisher


def test_most_dangerous_object_is_used_in_summary() -> None:
    source = MockCameraSource(
        [
            [
                Detection(
                    "dog", 0.8, BBoxNorm(cx=0.2, cy=0.5, w=0.08, h=0.10)
                ),
                Detection(
                    "car", 0.9, BBoxNorm(cx=0.8, cy=0.5, w=0.45, h=0.40)
                ),
            ]
        ],
        start_timestamp_ms=123456,
    )
    result = next(iter(create_default_pipeline().run(source)))

    output = JsonRiskOutputPublisher.to_dict(result)

    scores = [item["risk"]["score"] for item in output["objects"]]
    assert output["summary"]["maxRiskScore"] == max(scores)
    assert output["summary"]["mainRiskZone"] == "front-right"


def test_mock_pipeline_publishes_valid_json() -> None:
    source = MockCameraSource(
        [
            [
                Detection(
                    "person",
                    0.87,
                    BBoxNorm(cx=0.62, cy=0.48, w=0.18, h=0.35),
                )
            ]
        ],
        start_timestamp_ms=123456,
    )
    result = next(iter(create_default_pipeline().run(source)))
    stream = io.StringIO()

    serialized = JsonRiskOutputPublisher(stream).publish(result)
    output = json.loads(serialized)

    assert json.loads(stream.getvalue()) == output
    assert output["timestampMs"] == 123456
    assert output["objects"][0]["trackId"] == 1
    assert output["objects"][0]["relativeLocation"]["userZone"] == "front-right"
    assert output["objects"][0]["motion"]["state"] == "unknown"
    assert output["summary"]["maxRiskLevel"] in {
        "safe",
        "caution",
        "warning",
        "danger",
    }
