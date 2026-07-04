from graduate_risk_mvp.location import RelativeLocationEstimator
from graduate_risk_mvp.models import BBoxNorm, Detection, TrackedObject


def test_right_side_object_is_front_right() -> None:
    tracked = TrackedObject(
        track_id=1,
        detection=Detection(
            label="person",
            confidence=0.9,
            bbox=BBoxNorm(cx=0.8, cy=0.5, w=0.1, h=0.2),
        ),
    )

    location = RelativeLocationEstimator().estimate(tracked)

    assert location.user_zone == "front-right"
    assert location.screen_zone == "center-right"
    assert location.bearing_deg_approx > 0

