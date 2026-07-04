"""Download YOLOv8n weights and export a fixed-shape ONNX model."""

from __future__ import annotations

from pathlib import Path


def main() -> None:
    try:
        from ultralytics import YOLO
    except ImportError as error:
        raise RuntimeError(
            'Install export dependencies first: '
            '`python -m pip install -e ".[model-export]"`'
        ) from error

    project_root = Path(__file__).resolve().parents[1]
    models_dir = project_root / "models"
    models_dir.mkdir(parents=True, exist_ok=True)

    model = YOLO("yolov8n.pt")
    exported_path = Path(
        model.export(
            format="onnx",
            imgsz=640,
            opset=12,
            simplify=True,
            dynamic=False,
        )
    ).resolve()
    destination = models_dir / "yolov8n.onnx"
    if exported_path != destination:
        exported_path.replace(destination)

    downloaded_weights = Path("yolov8n.pt")
    if downloaded_weights.is_file():
        downloaded_weights.replace(models_dir / downloaded_weights.name)

    print(f"Person detector model ready: {destination}")


if __name__ == "__main__":
    main()
