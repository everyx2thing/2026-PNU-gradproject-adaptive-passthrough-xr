"""Detector implementations."""

from __future__ import annotations

from collections.abc import Sequence
from pathlib import Path
from typing import Any

from .interfaces import Detector
from .models import BBoxNorm, Detection, Frame


class FakeDetector(Detector):
    """Reads scripted detections injected into a mock frame."""

    def detect(self, frame: Frame) -> Sequence[Detection]:
        detections = frame.metadata.get("detections", ())
        if not isinstance(detections, (list, tuple)):
            raise TypeError("Frame metadata 'detections' must be a sequence")
        if not all(isinstance(item, Detection) for item in detections):
            raise TypeError("FakeDetector accepts Detection objects only")
        return tuple(detections)


class OpenCVFaceDetector(Detector):
    """Uses OpenCV's bundled Haar cascade as a temporary person proxy."""

    def __init__(
        self,
        *,
        scale_factor: float = 1.1,
        min_neighbors: int = 5,
        min_face_size: int = 30,
        confidence: float = 0.8,
        cv2_module: Any = None,
        cascade: Any = None,
    ) -> None:
        self._cv2 = cv2_module if cv2_module is not None else _import_cv2()
        self._scale_factor = scale_factor
        self._min_neighbors = min_neighbors
        self._min_face_size = min_face_size
        self._confidence = confidence

        if cascade is None:
            cascade_path = (
                Path(self._cv2.data.haarcascades)
                / "haarcascade_frontalface_default.xml"
            )
            cascade = self._cv2.CascadeClassifier(str(cascade_path))
        if cascade.empty():
            raise RuntimeError("Could not load OpenCV frontal-face cascade")
        self._cascade = cascade

    def detect(self, frame: Frame) -> Sequence[Detection]:
        if frame.payload is None:
            raise ValueError("OpenCVFaceDetector requires an image frame payload")

        image = frame.payload
        height, width = image.shape[:2]
        if width <= 0 or height <= 0:
            raise ValueError("Frame dimensions must be positive")

        gray = self._cv2.cvtColor(image, self._cv2.COLOR_BGR2GRAY)
        gray = self._cv2.equalizeHist(gray)
        faces = self._cascade.detectMultiScale(
            gray,
            scaleFactor=self._scale_factor,
            minNeighbors=self._min_neighbors,
            minSize=(self._min_face_size, self._min_face_size),
        )

        detections: list[Detection] = []
        for x, y, box_width, box_height in faces:
            normalized_width = min(float(box_width) / width, 1.0)
            normalized_height = min(float(box_height) / height, 1.0)
            center_x = min(max((float(x) + box_width / 2.0) / width, 0.0), 1.0)
            center_y = min(max((float(y) + box_height / 2.0) / height, 0.0), 1.0)
            detections.append(
                Detection(
                    label="person",
                    confidence=self._confidence,
                    bbox=BBoxNorm(
                        cx=center_x,
                        cy=center_y,
                        w=normalized_width,
                        h=normalized_height,
                    ),
                )
            )
        return tuple(detections)


class OnnxPersonDetector(Detector):
    """Runs a YOLOv8-style ONNX model and returns full-person detections."""

    def __init__(
        self,
        model_path: str | Path,
        *,
        confidence_threshold: float = 0.4,
        iou_threshold: float = 0.45,
        input_size: int = 640,
        providers: Sequence[str] | None = None,
        session: Any = None,
        cv2_module: Any = None,
        numpy_module: Any = None,
    ) -> None:
        self._model_path = Path(model_path)
        if session is None and not self._model_path.is_file():
            raise FileNotFoundError(
                f"ONNX model not found: {self._model_path}. "
                "Run `python scripts/export_person_model.py` first."
            )
        if not 0.0 <= confidence_threshold <= 1.0:
            raise ValueError("confidence_threshold must be between 0 and 1")

        self._confidence_threshold = confidence_threshold
        self._iou_threshold = iou_threshold
        self._input_size = input_size
        self._cv2 = cv2_module if cv2_module is not None else _import_cv2()
        self._np = (
            numpy_module if numpy_module is not None else _import_numpy()
        )

        if session is None:
            onnxruntime = _import_onnxruntime()
            selected_providers = list(providers or ["CPUExecutionProvider"])
            session = onnxruntime.InferenceSession(
                str(self._model_path),
                providers=selected_providers,
            )
        self._session = session
        self._input_name = self._session.get_inputs()[0].name

    def detect(self, frame: Frame) -> Sequence[Detection]:
        if frame.payload is None:
            raise ValueError("OnnxPersonDetector requires an image frame payload")

        image = frame.payload
        image_height, image_width = image.shape[:2]
        blob, scale, pad_x, pad_y = self._preprocess(image)
        output = self._session.run(None, {self._input_name: blob})[0]
        predictions = self._np.squeeze(output)
        if predictions.ndim == 1:
            predictions = predictions.reshape(1, -1)
        if (
            predictions.ndim == 2
            and predictions.shape[0] < predictions.shape[1]
        ):
            predictions = predictions.T

        boxes: list[list[int]] = []
        confidences: list[float] = []
        for prediction in predictions:
            if len(prediction) < 5:
                continue
            confidence = float(prediction[4])
            if confidence < self._confidence_threshold:
                continue

            center_x, center_y, width, height = map(
                float, prediction[:4]
            )
            left = int((center_x - width / 2.0 - pad_x) / scale)
            top = int((center_y - height / 2.0 - pad_y) / scale)
            box_width = int(width / scale)
            box_height = int(height / scale)

            left = max(0, min(left, image_width - 1))
            top = max(0, min(top, image_height - 1))
            box_width = max(
                1, min(box_width, image_width - left)
            )
            box_height = max(
                1, min(box_height, image_height - top)
            )
            boxes.append([left, top, box_width, box_height])
            confidences.append(confidence)

        if not boxes:
            return ()

        selected = self._cv2.dnn.NMSBoxes(
            boxes,
            confidences,
            self._confidence_threshold,
            self._iou_threshold,
        )
        selected_indices = self._np.asarray(selected).reshape(-1)

        detections: list[Detection] = []
        for index in selected_indices:
            left, top, box_width, box_height = boxes[int(index)]
            detections.append(
                Detection(
                    label="person",
                    confidence=round(confidences[int(index)], 4),
                    bbox=BBoxNorm(
                        cx=(left + box_width / 2.0) / image_width,
                        cy=(top + box_height / 2.0) / image_height,
                        w=box_width / image_width,
                        h=box_height / image_height,
                    ),
                )
            )
        return tuple(detections)

    def _preprocess(self, image: Any) -> tuple[Any, float, int, int]:
        image_height, image_width = image.shape[:2]
        scale = min(
            self._input_size / image_width,
            self._input_size / image_height,
        )
        resized_width = max(1, round(image_width * scale))
        resized_height = max(1, round(image_height * scale))
        resized = self._cv2.resize(
            image, (resized_width, resized_height)
        )
        canvas = self._np.full(
            (self._input_size, self._input_size, 3),
            114,
            dtype=self._np.uint8,
        )
        pad_x = (self._input_size - resized_width) // 2
        pad_y = (self._input_size - resized_height) // 2
        canvas[
            pad_y : pad_y + resized_height,
            pad_x : pad_x + resized_width,
        ] = resized
        blob = self._cv2.dnn.blobFromImage(
            canvas,
            scalefactor=1.0 / 255.0,
            size=(self._input_size, self._input_size),
            swapRB=True,
            crop=False,
        )
        return blob, scale, pad_x, pad_y


def _import_cv2() -> Any:
    try:
        import cv2
    except ImportError as error:
        raise RuntimeError(
            'OpenCV is required for real-frame detection. Install it with '
            '`python -m pip install -e ".[camera]"`.'
        ) from error
    return cv2


def _import_numpy() -> Any:
    try:
        import numpy
    except ImportError as error:
        raise RuntimeError(
            'NumPy is required for ONNX person detection. Install it with '
            '`python -m pip install -e ".[camera]"`.'
        ) from error
    return numpy


def _import_onnxruntime() -> Any:
    try:
        import onnxruntime
    except ImportError as error:
        raise RuntimeError(
            'ONNX Runtime is required for person detection. Install it with '
            '`python -m pip install -e ".[camera]"`.'
        ) from error
    return onnxruntime
