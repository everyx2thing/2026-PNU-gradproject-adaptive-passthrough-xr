from types import SimpleNamespace

from graduate_risk_mvp.camera import OpenCVCameraSource


class FakeCapture:
    def __init__(self) -> None:
        self.released = False
        self.settings = []
        self.images = [
            SimpleNamespace(shape=(480, 640, 3)),
            SimpleNamespace(shape=(480, 640, 3)),
        ]

    def isOpened(self) -> bool:
        return True

    def set(self, property_id, value) -> None:
        self.settings.append((property_id, value))

    def read(self):
        if self.images:
            return True, self.images.pop(0)
        return False, None

    def release(self) -> None:
        self.released = True


def test_opencv_camera_source_yields_frames_and_releases_device() -> None:
    capture = FakeCapture()
    fake_cv2 = SimpleNamespace(
        CAP_PROP_FRAME_WIDTH=1,
        CAP_PROP_FRAME_HEIGHT=2,
        CAP_PROP_FPS=3,
        VideoCapture=lambda index: capture,
    )
    source = OpenCVCameraSource(
        device_index=0,
        width=640,
        height=480,
        fps=30,
        max_frames=2,
        cv2_module=fake_cv2,
    )

    frames = list(source.frames())

    assert [frame.frame_id for frame in frames] == [0, 1]
    assert frames[0].metadata["source"] == "usb_camera"
    assert frames[0].metadata["width"] == 640
    assert capture.released
