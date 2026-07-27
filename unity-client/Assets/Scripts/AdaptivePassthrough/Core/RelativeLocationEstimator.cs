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
            NormalizedBoundingBox box = tracked.Detection.boundingBox;
            HorizontalZone zone = box.centerX < leftThreshold
                ? HorizontalZone.Left
                : box.centerX > rightThreshold
                    ? HorizontalZone.Right
                    : HorizontalZone.Center;

            DistanceBand distanceBand = box.Area < farAreaThreshold
                ? DistanceBand.Far
                : box.Area < nearAreaThreshold
                    ? DistanceBand.Mid
                    : DistanceBand.Near;

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
                box.Area);
        }
    }
}
