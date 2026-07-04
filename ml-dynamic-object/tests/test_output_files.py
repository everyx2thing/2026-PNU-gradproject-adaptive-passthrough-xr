import json

from graduate_risk_mvp.camera import MockCameraSource
from graduate_risk_mvp.factory import create_default_pipeline
from graduate_risk_mvp.models import BBoxNorm, Detection
from graduate_risk_mvp.output import (
    LatestJsonFilePublisher,
    NdjsonRiskOutputPublisher,
)


def test_file_publishers_write_latest_json_and_ndjson(tmp_path) -> None:
    source = MockCameraSource(
        [
            [Detection("person", 0.9, BBoxNorm(0.5, 0.5, 0.1, 0.2))],
            [Detection("person", 0.9, BBoxNorm(0.5, 0.5, 0.2, 0.4))],
        ],
        start_timestamp_ms=1000,
    )
    results = list(create_default_pipeline().run(source))
    latest_path = tmp_path / "latest.json"
    history_path = tmp_path / "risk.ndjson"
    latest = LatestJsonFilePublisher(latest_path)
    history = NdjsonRiskOutputPublisher(history_path)

    for result in results:
        latest.publish(result)
        history.publish(result)

    latest_data = json.loads(latest_path.read_text(encoding="utf-8"))
    history_lines = history_path.read_text(encoding="utf-8").splitlines()

    assert latest_data["timestampMs"] == 1100
    assert len(history_lines) == 2
    assert json.loads(history_lines[-1])["timestampMs"] == 1100
