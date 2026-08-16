"""Lower a binary sklearn TreeEnsembleClassifier to Unity-friendly ONNX ops.

The source model uses ai.onnx.ml TreeEnsembleClassifier and ZipMap, which the
Unity InferenceEngine importer cannot consume.  This converter preserves the
forest decisions with Gather/LessOrEqual/Where/Add/Sub operations and exports
one float output named ``risk_probability``.

For the current Neutral/Positive model, Neutral is treated as Negative risk:

    risk_probability = P(Neutral) = 1 - P(Positive)
"""

from __future__ import annotations

import argparse
from collections import defaultdict
from pathlib import Path

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper


INPUT_NAME = "float_input"
OUTPUT_NAME = "risk_probability"
FEATURE_COUNT = 7


def _attributes(node: onnx.NodeProto) -> dict[str, object]:
    return {
        attribute.name: helper.get_attribute_value(attribute)
        for attribute in node.attribute
    }


def _decode_labels(values: list[bytes]) -> list[str]:
    return [
        value.decode("utf-8") if isinstance(value, bytes) else str(value)
        for value in values
    ]


def convert(source_path: Path, target_path: Path) -> None:
    source = onnx.load(source_path)
    classifiers = [
        node
        for node in source.graph.node
        if node.domain == "ai.onnx.ml"
        and node.op_type == "TreeEnsembleClassifier"
    ]
    if len(classifiers) != 1:
        raise ValueError(
            "Expected exactly one ai.onnx.ml TreeEnsembleClassifier."
        )

    attributes = _attributes(classifiers[0])
    labels = _decode_labels(attributes["classlabels_strings"])
    if labels != ["Neutral", "Positive"]:
        raise ValueError(
            "Expected class order ['Neutral', 'Positive'], got "
            f"{labels}."
        )
    if attributes.get("post_transform", b"NONE") != b"NONE":
        raise ValueError("Only post_transform=NONE is supported.")
    if any(attributes["nodes_missing_value_tracks_true"]):
        raise ValueError("Missing-value branches are not supported.")

    class_ids = list(attributes["class_ids"])
    if any(class_id != 0 for class_id in class_ids):
        raise ValueError(
            "Expected sklearn's compressed binary probability encoding."
        )

    nodes_by_tree: dict[int, dict[int, dict[str, object]]] = defaultdict(dict)
    for values in zip(
        attributes["nodes_treeids"],
        attributes["nodes_nodeids"],
        attributes["nodes_featureids"],
        attributes["nodes_modes"],
        attributes["nodes_values"],
        attributes["nodes_truenodeids"],
        attributes["nodes_falsenodeids"],
    ):
        tree_id, node_id, feature_id, mode, value, true_id, false_id = values
        nodes_by_tree[int(tree_id)][int(node_id)] = {
            "feature_id": int(feature_id),
            "mode": mode.decode("ascii"),
            "value": float(value),
            "true_id": int(true_id),
            "false_id": int(false_id),
        }

    positive_leaf_weights: dict[tuple[int, int], float] = {}
    for tree_id, node_id, weight in zip(
        attributes["class_treeids"],
        attributes["class_nodeids"],
        attributes["class_weights"],
    ):
        positive_leaf_weights[(int(tree_id), int(node_id))] = float(weight)

    graph_nodes: list[onnx.NodeProto] = []
    initializers: list[onnx.TensorProto] = []
    generated_index = 0

    def constant(name: str, value: np.ndarray) -> str:
        initializers.append(numpy_helper.from_array(value, name=name))
        return name

    def unique(prefix: str) -> str:
        nonlocal generated_index
        generated_index += 1
        return f"{prefix}_{generated_index}"

    def emit_tree_node(tree_id: int, node_id: int) -> str:
        tree_node = nodes_by_tree[tree_id][node_id]
        mode = tree_node["mode"]
        if mode == "LEAF":
            return constant(
                unique(f"tree_{tree_id}_leaf_{node_id}"),
                np.asarray(
                    [positive_leaf_weights.get((tree_id, node_id), 0.0)],
                    dtype=np.float32,
                ),
            )
        if mode != "BRANCH_LEQ":
            raise ValueError(f"Unsupported tree node mode: {mode}")

        feature_id = int(tree_node["feature_id"])
        if feature_id < 0 or feature_id >= FEATURE_COUNT:
            raise ValueError(f"Invalid feature index: {feature_id}")

        feature_index = constant(
            unique("feature_index"),
            np.asarray(feature_id, dtype=np.int64),
        )
        gathered = unique("feature")
        graph_nodes.append(
            helper.make_node(
                "Gather",
                [INPUT_NAME, feature_index],
                [gathered],
                axis=1,
            )
        )
        threshold = constant(
            unique("threshold"),
            np.asarray([tree_node["value"]], dtype=np.float32),
        )
        condition = unique("leq")
        graph_nodes.append(
            helper.make_node(
                "LessOrEqual",
                [gathered, threshold],
                [condition],
            )
        )

        true_value = emit_tree_node(tree_id, int(tree_node["true_id"]))
        false_value = emit_tree_node(tree_id, int(tree_node["false_id"]))
        selected = unique(f"tree_{tree_id}_node_{node_id}")
        graph_nodes.append(
            helper.make_node(
                "Where",
                [condition, true_value, false_value],
                [selected],
            )
        )
        return selected

    tree_outputs: list[str] = []
    for tree_id in sorted(nodes_by_tree):
        child_ids = {
            int(node[child_name])
            for node in nodes_by_tree[tree_id].values()
            if node["mode"] != "LEAF"
            for child_name in ("true_id", "false_id")
        }
        root_ids = set(nodes_by_tree[tree_id]) - child_ids
        if len(root_ids) != 1:
            raise ValueError(
                f"Tree {tree_id} has invalid roots: {sorted(root_ids)}"
            )
        tree_outputs.append(emit_tree_node(tree_id, root_ids.pop()))

    positive_probability = tree_outputs[0]
    for tree_output in tree_outputs[1:]:
        summed = unique("positive_sum")
        graph_nodes.append(
            helper.make_node(
                "Add",
                [positive_probability, tree_output],
                [summed],
            )
        )
        positive_probability = summed

    one = constant("one", np.asarray([1.0], dtype=np.float32))
    graph_nodes.append(
        helper.make_node(
            "Sub",
            [one, positive_probability],
            [OUTPUT_NAME],
        )
    )

    graph = helper.make_graph(
        graph_nodes,
        "PersonalizationNeutralRisk",
        [helper.make_tensor_value_info(
            INPUT_NAME,
            TensorProto.FLOAT,
            [1, FEATURE_COUNT],
        )],
        [helper.make_tensor_value_info(
            OUTPUT_NAME,
            TensorProto.FLOAT,
            [1],
        )],
        initializer=initializers,
    )
    converted = helper.make_model(
        graph,
        producer_name="TeamVR.convert_tree_onnx_for_unity",
        opset_imports=[helper.make_opsetid("", 17)],
    )
    converted.ir_version = 8
    converted.metadata_props.add(
        key="teamvr.risk_mapping",
        value="risk_probability=P(Neutral)=1-P(Positive)",
    )
    converted.metadata_props.add(
        key="teamvr.source_model",
        value=source_path.name,
    )
    onnx.checker.check_model(converted)
    target_path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(converted, target_path)


def verify(source_path: Path, target_path: Path) -> float:
    import onnxruntime as ort

    source_session = ort.InferenceSession(
        str(source_path),
        providers=["CPUExecutionProvider"],
    )
    target_session = ort.InferenceSession(
        str(target_path),
        providers=["CPUExecutionProvider"],
    )
    random = np.random.default_rng(20260811)
    maxima = np.asarray([1, 1, 10, 3, 5, 1, 1], dtype=np.float32)
    samples = [
        np.zeros(FEATURE_COUNT, dtype=np.float32),
        maxima.copy(),
    ]
    samples.extend(
        random.random((128, FEATURE_COUNT), dtype=np.float32) * maxima
    )

    maximum_error = 0.0
    for sample in samples:
        batch = sample.reshape(1, FEATURE_COUNT)
        source_probability = source_session.run(
            ["output_probability"],
            {INPUT_NAME: batch},
        )[0][0]["Neutral"]
        converted_probability = float(
            target_session.run(
                [OUTPUT_NAME],
                {INPUT_NAME: batch},
            )[0][0]
        )
        maximum_error = max(
            maximum_error,
            abs(source_probability - converted_probability),
        )

    if maximum_error > 1e-5:
        raise RuntimeError(
            "Converted model does not match the source model: "
            f"max error={maximum_error:.8f}"
        )
    return maximum_error


def parse_args() -> argparse.Namespace:
    repository_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--source",
        type=Path,
        default=repository_root
        / "unity-client/Assets/Models/rf_personalization_real.onnx.source",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=repository_root
        / "unity-client/Assets/Models/personalization_runtime.onnx",
    )
    parser.add_argument("--skip-verify", action="store_true")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    convert(args.source, args.output)
    print(f"Unity-compatible ONNX written to {args.output}")
    if not args.skip_verify:
        error = verify(args.source, args.output)
        print(f"Neutral-risk parity verified; max error={error:.8f}")


if __name__ == "__main__":
    main()
