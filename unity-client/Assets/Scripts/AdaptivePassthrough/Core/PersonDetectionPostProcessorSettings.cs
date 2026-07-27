using System;

namespace TeamVR.AdaptivePassthrough
{
    public enum PersonBoxFormat
    {
        CenterXYWH,
        CornersXYXY
    }

    public enum PersonBoxCoordinateSpace
    {
        ModelPixels,
        Normalized
    }

    [Serializable]
    public sealed class PersonDetectionPostProcessorSettings
    {
        public PersonBoxFormat boxFormat = PersonBoxFormat.CenterXYWH;
        public PersonBoxCoordinateSpace coordinateSpace =
            PersonBoxCoordinateSpace.ModelPixels;
        public int personClassId;
        public float confidenceThreshold = 0.55f;
        public float iouThreshold = 0.45f;
        public int maximumCandidates = 50;
        public int maximumDetections = 10;
        public float minimumVisibleFraction = 0.15f;
        public float maximumNormalizedDimension = 2f;
        public bool flipVertical;
    }
}
