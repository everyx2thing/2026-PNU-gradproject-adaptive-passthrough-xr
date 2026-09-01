using System;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Pure gameplay data. Never references the AdaptivePassthrough risk
    // pipeline in any way - balls are only a physical stimulus to make the
    // participant move.
    public enum BallType
    {
        MustAvoid,
        OptionalHit
    }

    // Front-facing 180-degree arc only (no rear spawns). 9 points spaced
    // 22.5 degrees apart from Left (-90) to Right (+90). These match the 9
    // physical spawner objects placed in the scene (see
    // ExperimentBallSpawner's direction spawn point fields).
    public enum BallDirection
    {
        Left,
        LeftMid,
        FrontLeft,
        FrontLeftMid,
        Front,
        FrontRightMid,
        FrontRight,
        RightMid,
        Right
    }

    [Serializable]
    public struct BallSpawnEntry
    {
        [Min(0f)] public float spawnTimeSeconds;
        public BallDirection direction;
        [Min(0.5f)] public float spawnDistanceMeters;
        public float spawnHeightOffsetMeters;
        public float targetHeightOffsetMeters;
        [Min(0.05f)] public float speedMetersPerSecond;
        public BallType ballType;

        // Last-resort fallback only, used when no scene spawn point Transform
        // is assigned for this direction on ExperimentBallSpawner. Angles are
        // measured from forward, positive = toward the right.
        public static Vector3 DirectionVector(BallDirection direction)
        {
            float angleDegrees = DirectionAngleDegrees(direction);
            return Quaternion.Euler(0f, angleDegrees, 0f) * Vector3.forward;
        }

        private static float DirectionAngleDegrees(BallDirection direction)
        {
            switch (direction)
            {
                case BallDirection.Left: return -90f;
                case BallDirection.LeftMid: return -67.5f;
                case BallDirection.FrontLeft: return -45f;
                case BallDirection.FrontLeftMid: return -22.5f;
                case BallDirection.Front: return 0f;
                case BallDirection.FrontRightMid: return 22.5f;
                case BallDirection.FrontRight: return 45f;
                case BallDirection.RightMid: return 67.5f;
                case BallDirection.Right: return 90f;
                default: return 0f;
            }
        }
    }
}
