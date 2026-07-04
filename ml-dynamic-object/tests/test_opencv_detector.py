from types import SimpleNamespace

from graduate_risk_mvp.detector import OpenCVFaceDetector
from graduate_risk_mvp.models import Frame


class FakeCascade:
    def empty(self) -> bool:
        return False

    def detectMultiScale(self, image, **kwargs):
        return [(320, 180, 128, 144)]


def test_opencv_face_detector_normalizes_detected_box() -> None:
    fake_cv2 = SimpleNamespace(
        COLOR_BGR2GRAY=6,
        cvtColor=lambda image, code: image,
        equalizeHist=lambda image: image,
    )
    detector = OpenCVFaceDetector(
        cv2_module=fake_cv2,
        cascade=FakeCascade(),
    )
    image = SimpleNamespace(shape=(720, 1280, 3))

    detections = detector.detect(
        Frame(frame_id=0, timestamp_ms=1, payload=image)
    )

    assert len(detections) == 1
    assert detections[0].label == "person"
    assert detections[0].bbox.cx == 0.3
    assert detections[0].bbox.w == 0.1
