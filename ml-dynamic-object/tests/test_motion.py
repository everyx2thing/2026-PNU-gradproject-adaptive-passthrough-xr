from graduate_risk_mvp.models import BBoxNorm, Detection, TrackedObject
from graduate_risk_mvp.motion import HistoryMotionEstimator


def _tracked(height: float, *, cx: float = 0.5) -> TrackedObject:
    return TrackedObject(
        track_id=1,
        detection=Detection(
            "person",
            0.9,
            BBoxNorm(cx=cx, cy=0.5, w=height * 0.45, h=height),
        ),
    )


def test_persistent_scale_growth_is_classified_as_approaching() -> None:
    estimator = HistoryMotionEstimator(smoothing_alpha=1.0)
    estimate = None
    for timestamp_ms, height in zip(
        (0, 100, 200, 300, 400),
        (0.20, 0.22, 0.25, 0.29, 0.34),
    ):
        estimate = estimator.update(
            [_tracked(height)], timestamp_ms
        )[1]

    assert estimate is not None
    assert estimate.state == "approaching"
    assert estimate.scale_rate_per_sec > 0
    assert estimate.ttc_sec_approx is not None


def test_slow_persistent_growth_is_classified_as_approaching() -> None:
    estimator = HistoryMotionEstimator(smoothing_alpha=1.0)
    estimate = None
    for timestamp_ms, height in zip(
        (0, 250, 500, 750, 1000, 1250),
        (0.200, 0.204, 0.208, 0.212, 0.216, 0.220),
    ):
        estimate = estimator.update(
            [_tracked(height)], timestamp_ms
        )[1]

    assert estimate is not None
    assert estimate.state == "approaching"
    assert 0.04 <= estimate.scale_rate_per_sec < 0.15


def test_short_history_does_not_claim_approach() -> None:
    estimator = HistoryMotionEstimator()
    estimator.update([_tracked(0.2)], 0)
    estimate = estimator.update([_tracked(0.3)], 100)[1]

    assert estimate.state == "unknown"
    assert estimate.ttc_sec_approx is None


def test_single_scale_spike_is_rejected_as_noise() -> None:
    estimator = HistoryMotionEstimator(smoothing_alpha=1.0)
    estimate = None
    for timestamp_ms, height in zip(
        (0, 100, 200, 300, 400),
        (0.20, 0.20, 0.35, 0.20, 0.20),
    ):
        estimate = estimator.update(
            [_tracked(height)], timestamp_ms
        )[1]

    assert estimate is not None
    assert estimate.state == "steady"
    assert estimate.ttc_sec_approx is None


def test_persistent_scale_reduction_is_classified_as_receding() -> None:
    estimator = HistoryMotionEstimator(smoothing_alpha=1.0)
    estimate = None
    for timestamp_ms, height in zip(
        (0, 100, 200, 300, 400),
        (0.40, 0.36, 0.32, 0.28, 0.25),
    ):
        estimate = estimator.update(
            [_tracked(height)], timestamp_ms
        )[1]

    assert estimate is not None
    assert estimate.state == "receding"
    assert estimate.scale_rate_per_sec < 0
