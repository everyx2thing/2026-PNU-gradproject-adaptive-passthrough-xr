"""Camera source implementations."""

from __future__ import annotations

import time
from collections.abc import Iterable, Sequence
from typing import Any

from .interfaces import CameraSource
from .models import Detection, Frame


class CameraOpenError(RuntimeError):
    """Raised when a camera device cannot be opened."""


class CameraReadError(RuntimeError):
    """Raised when frames cannot be read from an opened camera."""


class MockCameraSource(CameraSource):
    """Produces frames containing scripted detections instead of image pixels."""

    def __init__(
        self,
        script: Sequence[Sequence[Detection]],
        *,
        start_timestamp_ms: int | None = None,
        frame_interval_ms: int = 100,
    ) -> None:
        self._script = script
        self._start_timestamp_ms = (
            start_timestamp_ms
            if start_timestamp_ms is not None
            else int(time.time() * 1000)
        )
        self._frame_interval_ms = frame_interval_ms

    def frames(self) -> Iterable[Frame]:
        for frame_id, detections in enumerate(self._script):
            yield Frame(
                frame_id=frame_id,
                timestamp_ms=self._start_timestamp_ms
                + frame_id * self._frame_interval_ms,
                metadata={"detections": tuple(detections)},
            )


class OpenCVCameraSource(CameraSource):
    """Reads BGR frames from a local USB camera through OpenCV."""

    def __init__(
        self,
        device_index: int = 0,
        *,
        width: int | None = 1280,
        height: int | None = 720,
        fps: float | None = 30.0,
        max_frames: int | None = None,
        max_consecutive_read_failures: int = 5,
        cv2_module: Any = None,
    ) -> None:
        if device_index < 0:
            raise ValueError("device_index must be zero or greater")
        if max_frames is not None and max_frames <= 0:
            raise ValueError("max_frames must be positive or None")

        self._device_index = device_index
        self._width = width
        self._height = height
        self._fps = fps
        self._max_frames = max_frames
        self._max_consecutive_read_failures = max_consecutive_read_failures
        self._cv2 = cv2_module

    def frames(self) -> Iterable[Frame]:
        cv2 = self._cv2 if self._cv2 is not None else _import_cv2()
        capture = cv2.VideoCapture(self._device_index)

        if not capture.isOpened():
            capture.release()
            raise CameraOpenError(
                f"Could not open camera index {self._device_index}. "
                "Check the index, USB connection, and whether another app is using it."
            )

        self._configure_capture(capture, cv2)
        frame_id = 0
        consecutive_failures = 0

        try:
            while self._max_frames is None or frame_id < self._max_frames:
                success, image = capture.read()
                if not success or image is None:
                    consecutive_failures += 1
                    if (
                        consecutive_failures
                        >= self._max_consecutive_read_failures
                    ):
                        raise CameraReadError(
                            f"Camera index {self._device_index} stopped returning frames"
                        )
                    continue

                consecutive_failures = 0
                frame_height, frame_width = image.shape[:2]
                yield Frame(
                    frame_id=frame_id,
                    timestamp_ms=int(time.time() * 1000),
                    payload=image,
                    metadata={
                        "source": "usb_camera",
                        "deviceIndex": self._device_index,
                        "width": int(frame_width),
                        "height": int(frame_height),
                    },
                )
                frame_id += 1
        finally:
            capture.release()

    def _configure_capture(self, capture: Any, cv2: Any) -> None:
        settings = (
            (cv2.CAP_PROP_FRAME_WIDTH, self._width),
            (cv2.CAP_PROP_FRAME_HEIGHT, self._height),
            (cv2.CAP_PROP_FPS, self._fps),
        )
        for property_id, value in settings:
            if value is not None:
                capture.set(property_id, value)


def _import_cv2() -> Any:
    try:
        import cv2
    except ImportError as error:
        raise RuntimeError(
            'OpenCV is required for USB camera input. Install it with '
            '`python -m pip install -e ".[camera]"`.'
        ) from error
    return cv2
