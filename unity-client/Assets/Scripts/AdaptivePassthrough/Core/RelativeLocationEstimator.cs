namespace TeamVR.AdaptivePassthrough
{
    public sealed class RelativeLocationEstimator
    {
        private readonly float leftThreshold;
        private readonly float rightThreshold;
        private readonly float farAreaThreshold;
        private readonly float nearAreaThreshold;
        private readonly float horizontalFieldOfViewDegrees;

        public RelativeLocationEstimator(
            float leftThreshold = 0.4f,
            float rightThreshold = 0.6f,
            float farAreaThreshold = 0.03f,
            float nearAreaThreshold = 0.06f,
            float horizontalFieldOfViewDegrees = 150f)
        {
            this.leftThreshold = leftThreshold;
            this.rightThreshold = rightThreshold;
            this.farAreaThreshold = farAreaThreshold;
            this.nearAreaThreshold = nearAreaThreshold;
            this.horizontalFieldOfViewDegrees = horizontalFieldOfViewDegrees;
        }

        public RelativeLocationEstimate Estimate(TrackedDynamicObject tracked)
        {
            return Estimate(
                tracked,
                PersonDistanceMeasurement.BoundingBoxFallback(
                    tracked.TrackId,
                    0.0,
                    tracked.Detection.boundingBox.Area,
                    "legacy_bbox_estimate"));
        }

        public RelativeLocationEstimate Estimate(
            TrackedDynamicObject tracked,
            PersonDistanceMeasurement distance)
        {
            NormalizedBoundingBox box = tracked.Detection.boundingBox;
            HorizontalZone zone = box.centerX < leftThreshold
                ? HorizontalZone.Left
                : box.centerX > rightThreshold
                    ? HorizontalZone.Right
                    : HorizontalZone.Center;

            bool hasMetricDistance =
                distance != null && distance.HasMetricDistance;
            DistanceBand distanceBand;
            if (hasMetricDistance)
            {
                distanceBand = distance.FilteredDistanceMeters <= 1.50f
                    ? DistanceBand.Near
                    : distance.FilteredDistanceMeters <= 3.00f
                        ? DistanceBand.Mid
                        : DistanceBand.Far;
            }
            else
            {
                distanceBand = box.Area < farAreaThreshold
                    ? DistanceBand.Far
                    : box.Area < nearAreaThreshold
                        ? DistanceBand.Mid
                        : DistanceBand.Near;
            }

            string relativeDirection;
            switch (zone)
            {
                case HorizontalZone.Left:
                    relativeDirection = "front-left";
                    break;
                case HorizontalZone.Right:
                    relativeDirection = "front-right";
                    break;
                default:
                    relativeDirection = "front-center";
                    break;
            }

            return new RelativeLocationEstimate(
                zone,
                relativeDirection,
                distanceBand,
                (box.centerX - 0.5f) * horizontalFieldOfViewDegrees,
                box.Area,
                distance == null
                    ? PersonDistanceSource.Unavailable
                    : distance.Source,
                hasMetricDistance,
                distance == null ? 0f : distance.RawDistanceMeters,
                distance == null ? 0f : distance.FilteredDistanceMeters,
                distance == null ? 0f : distance.Confidence);
        }
    }
}
