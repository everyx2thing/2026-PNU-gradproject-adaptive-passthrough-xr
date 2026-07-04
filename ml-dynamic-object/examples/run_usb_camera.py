"""Run the risk pipeline with a real USB camera."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
PROJECT_SRC = PROJECT_ROOT / "src"
sys.path.insert(0, str(PROJECT_SRC))

from graduate_risk_mvp.camera import OpenCVCameraSource
from graduate_risk_mvp.factory import create_person_pipeline
from graduate_risk_mvp.output import (
    JsonRiskOutputPublisher,
    LatestJsonFilePublisher,
    NdjsonRiskOutputPublisher,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Read a USB camera and output the latest risk result as JSON."
    )
    parser.add_argument("--camera", type=int, default=0, help="Camera device index")
    parser.add_argument(
        "--frames",
        type=int,
        default=90,
        help="Frames to process; use 0 for unlimited",
    )
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=float, default=30.0)
    parser.add_argument(
        "--model",
        type=Path,
        default=PROJECT_ROOT / "models" / "yolov8n.onnx",
        help="Path to the YOLOv8-style ONNX model",
    )
    parser.add_argument(
        "--confidence",
        type=float,
        default=0.4,
        help="Minimum person confidence",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=PROJECT_ROOT / "outputs" / "risk.ndjson",
        help="Append every frame result as NDJSON",
    )
    parser.add_argument(
        "--latest-output",
        type=Path,
        default=PROJECT_ROOT / "outputs" / "latest.json",
        help="Overwrite with the latest JSON result",
    )
    parser.add_argument(
        "--no-file-output",
        action="store_true",
        help="Print only the final JSON and do not write output files",
    )
    parser.add_argument(
        "--preview",
        action="store_true",
        help="Show a preview window; press Q or Esc to finish",
    )
    return parser.parse_args()


def draw_result(image, result) -> None:
    import cv2

    frame_height, frame_width = image.shape[:2]
    for item in result.objects:
        bbox = item.tracked.detection.bbox
        left = int((bbox.cx - bbox.w / 2.0) * frame_width)
        top = int((bbox.cy - bbox.h / 2.0) * frame_height)
        right = int((bbox.cx + bbox.w / 2.0) * frame_width)
        bottom = int((bbox.cy + bbox.h / 2.0) * frame_height)
        color = (0, 0, 255) if item.risk.score >= 0.5 else (0, 255, 255)
        cv2.rectangle(image, (left, top), (right, bottom), color, 2)
        cv2.putText(
            image,
            (
                f"person {item.motion.state} "
                f"{item.risk.level} {item.risk.score:.2f}"
            ),
            (left, max(20, top - 8)),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            color,
            2,
        )


def main() -> None:
    args = parse_args()
    source = OpenCVCameraSource(
        device_index=args.camera,
        width=args.width,
        height=args.height,
        fps=args.fps,
        max_frames=None if args.frames == 0 else args.frames,
    )
    pipeline = create_person_pipeline(
        str(args.model),
        confidence_threshold=args.confidence,
    )
    ndjson_publisher = (
        None
        if args.no_file_output
        else NdjsonRiskOutputPublisher(args.output)
    )
    latest_publisher = (
        None
        if args.no_file_output
        else LatestJsonFilePublisher(args.latest_output)
    )
    latest_result = None
    frame_count = 0

    frame_iterator = iter(source.frames())
    try:
        for frame in frame_iterator:
            latest_result = pipeline.process_frame(frame)
            frame_count += 1
            if ndjson_publisher is not None:
                ndjson_publisher.publish(latest_result)
            if latest_publisher is not None:
                latest_publisher.publish(latest_result)

            if args.preview:
                import cv2

                preview = frame.payload.copy()
                draw_result(preview, latest_result)
                cv2.imshow("Graduate Risk MVP - press Q or Esc to stop", preview)
                if cv2.waitKey(1) & 0xFF in (ord("q"), 27):
                    break
    finally:
        close = getattr(frame_iterator, "close", None)
        if close is not None:
            close()
        if args.preview:
            import cv2

            cv2.destroyAllWindows()

    if latest_result is None:
        raise RuntimeError("The camera produced no frames")

    print(
        f"Processed {frame_count} frame(s) from camera {args.camera}.",
        file=sys.stderr,
    )
    if not args.no_file_output:
        print(f"Frame history: {args.output.resolve()}", file=sys.stderr)
        print(
            f"Latest result: {args.latest_output.resolve()}",
            file=sys.stderr,
        )
    JsonRiskOutputPublisher().publish(latest_result)


if __name__ == "__main__":
    main()
