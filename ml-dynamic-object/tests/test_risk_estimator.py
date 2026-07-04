from graduate_risk_mvp.location import RelativeLocationEstimator
from graduate_risk_mvp.models import (
    BBoxNorm,
    Detection,
    MotionEstimate,
    TrackedObject,
)
from graduate_risk_mvp.risk import WeightedRiskEstimator


def _tracked(bbox: BBoxNorm, *, growth: float = 0.0) -> TrackedObject:
    return TrackedObject(
        track_id=1,
        detection=Detection(label="person", confidence=0.9, bbox=bbox),
        area_growth_rate=growth,
    )


def _motion(
    state: str = "unknown",
    *,
    rate: float = 0.0,
    ttc: float | None = None,
    reliability: float = 1.0,
) -> MotionEstimate:
    return MotionEstimate(
        state=state,
        scale_rate_per_sec=rate,
        ttc_sec_approx=ttc,
        center_approach_rate_per_sec=0.0,
        sample_count=8,
        observation_ms=700,
        reliability=reliability,
    )


def test_larger_bbox_increases_risk() -> None:
    location_estimator = RelativeLocationEstimator()
    risk_estimator = WeightedRiskEstimator()
    small = _tracked(BBoxNorm(cx=0.5, cy=0.5, w=0.10, h=0.10))
    large = _tracked(BBoxNorm(cx=0.5, cy=0.5, w=0.40, h=0.40))

    small_risk = risk_estimator.estimate(
        small, location_estimator.estimate(small), _motion()
    )
    large_risk = risk_estimator.estimate(
        large, location_estimator.estimate(large), _motion()
    )

    assert large_risk.score > small_risk.score


def test_confirmed_approach_increases_risk() -> None:
    location_estimator = RelativeLocationEstimator()
    risk_estimator = WeightedRiskEstimator()
    steady = _tracked(BBoxNorm(cx=0.5, cy=0.5, w=0.2, h=0.2))
    approaching = _tracked(BBoxNorm(cx=0.5, cy=0.5, w=0.2, h=0.2))

    steady_risk = risk_estimator.estimate(
        steady,
        location_estimator.estimate(steady),
        _motion("steady"),
    )
    approaching_risk = risk_estimator.estimate(
        approaching,
        location_estimator.estimate(approaching),
        _motion("approaching", rate=0.6, ttc=1.67),
    )

    assert approaching_risk.score > steady_risk.score
    assert approaching_risk.level == "danger"
    assert "approach_confirmed" in approaching_risk.reasons
    assert "ttc_under_2s" in approaching_risk.reasons


def test_receding_person_has_lower_risk_than_approaching_person() -> None:
    location_estimator = RelativeLocationEstimator()
    risk_estimator = WeightedRiskEstimator()
    tracked = _tracked(BBoxNorm(cx=0.5, cy=0.5, w=0.3, h=0.4))
    location = location_estimator.estimate(tracked)

    approaching = risk_estimator.estimate(
        tracked,
        location,
        _motion("approaching", rate=0.5, ttc=2.0),
    )
    receding = risk_estimator.estimate(
        tracked,
        location,
        _motion("receding", rate=-0.5),
    )

    assert approaching.score > receding.score
    assert receding.level != "danger"


def test_slow_near_approach_reaches_warning_level() -> None:
    location_estimator = RelativeLocationEstimator()
    risk_estimator = WeightedRiskEstimator()
    tracked = _tracked(BBoxNorm(cx=0.5, cy=0.5, w=0.35, h=0.8))
    location = location_estimator.estimate(tracked)

    risk = risk_estimator.estimate(
        tracked,
        location,
        _motion("approaching", rate=0.06, ttc=16.7),
    )

    assert risk.score >= 0.5
    assert risk.level == "warning"
    assert "approach_confirmed" in risk.reasons
