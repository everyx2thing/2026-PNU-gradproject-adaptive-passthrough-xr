from graduate_risk_mvp.models import BBoxNorm, Detection
from graduate_risk_mvp.tracker import SimpleTracker


def test_tracker_keeps_id_and_computes_area_growth() -> None:
    tracker = SimpleTracker()
    first = tracker.update(
        [Detection("person", 0.9, BBoxNorm(0.5, 0.5, 0.1, 0.2))]
    )[0]
    second = tracker.update(
        [Detection("person", 0.9, BBoxNorm(0.52, 0.5, 0.2, 0.3))]
    )[0]

    assert second.track_id == first.track_id
    assert second.area_growth_rate > 0

