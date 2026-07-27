# Person detector model

`QuestPersonDetectionRunner` expects a Unity Inference Engine model with three
outputs in this order:

1. bounding boxes in center-X, center-Y, width, height pixel coordinates;
2. integer class IDs;
3. confidence scores.

The intended model is Meta's `yolov9sentis.sentis` sample model. COCO class `0`
is `person`. Assign the imported model asset to `QuestPersonDetectionRunner`
and leave **Boxes Are Center Format** enabled.

Source:
<https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples/tree/main/Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model>

The model and class-list files in that sample directory are provided under the
MIT license described by the sample repository.
