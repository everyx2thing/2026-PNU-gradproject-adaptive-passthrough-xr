using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Spawns and moves balls along the fixed schedule described by a
    // ProjectileScheduleSet. Never references the AdaptivePassthrough risk
    // pipeline - balls are purely a visual/physical stimulus that must not
    // feed into risk calculation in any way.
    [DefaultExecutionOrder(720)]
    [DisallowMultipleComponent]
    public sealed class ExperimentBallSpawner : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig cameraRig;

        [Header("Ball Prefabs (real Bomb/Target models - leave a slot empty to fall back to a plain colored sphere)")]
        [SerializeField] private GameObject bombBallPrefab;
        [SerializeField] private GameObject targetBallPrefab;

        [Header("Fallback Primitive (used only when a prefab above is not assigned)")]
        [SerializeField, Min(0.02f)] private float ballRadiusMeters = 0.09f;
        [SerializeField] private Color mustAvoidColor =
            new Color(0.85f, 0.15f, 0.15f);
        [SerializeField] private Color optionalHitColor =
            new Color(0.15f, 0.55f, 0.85f);

        [Header("Direction Spawn Points (front-facing 180 degree arc, 9 points, 22.5 degree spacing)")]
        [SerializeField] private Transform leftSpawnPoint;
        [SerializeField] private Transform leftMidSpawnPoint;
        [SerializeField] private Transform frontLeftSpawnPoint;
        [SerializeField] private Transform frontLeftMidSpawnPoint;
        [SerializeField] private Transform frontSpawnPoint;
        [SerializeField] private Transform frontRightMidSpawnPoint;
        [SerializeField] private Transform frontRightSpawnPoint;
        [SerializeField] private Transform rightMidSpawnPoint;
        [SerializeField] private Transform rightSpawnPoint;

        [Tooltip(
            "Optional origin for the authored spawn layout. When omitted, "
            + "the common parent of the nine spawn points is used. The "
            + "layout is re-anchored to the HMD position and horizontal yaw "
            + "at the beginning of every round.")]
        [SerializeField] private Transform spawnLayoutOrigin;

        private readonly List<GameObject> activeBalls = new List<GameObject>();
        private Material mustAvoidMaterial;
        private Material optionalHitMaterial;
        private Coroutine roundCoroutine;
        private Action onRoundComplete;

        public bool IsRoundActive => roundCoroutine != null;

        // Read by ExperimentThreatIndicatorController to draw edge-of-screen
        // arrows for balls currently outside the player's field of view.
        public IReadOnlyList<GameObject> ActiveBalls => activeBalls;

        public void Configure(
            OVRCameraRig rig,
            GameObject bombPrefab,
            GameObject targetPrefab,
            Transform layoutOrigin,
            Transform[] spawnPoints)
        {
            cameraRig = rig;
            bombBallPrefab = bombPrefab;
            targetBallPrefab = targetPrefab;
            spawnLayoutOrigin = layoutOrigin;

            if (spawnPoints == null || spawnPoints.Length != 9)
            {
                return;
            }

            leftSpawnPoint = spawnPoints[0];
            leftMidSpawnPoint = spawnPoints[1];
            frontLeftSpawnPoint = spawnPoints[2];
            frontLeftMidSpawnPoint = spawnPoints[3];
            frontSpawnPoint = spawnPoints[4];
            frontRightMidSpawnPoint = spawnPoints[5];
            frontRightSpawnPoint = spawnPoints[6];
            rightMidSpawnPoint = spawnPoints[7];
            rightSpawnPoint = spawnPoints[8];
        }

        public bool ValidateConfiguration(out string error)
        {
            ResolveReferences();
            if (cameraRig == null || cameraRig.centerEyeAnchor == null)
            {
                error = "OVRCameraRig center eye anchor is missing.";
                return false;
            }

            if (bombBallPrefab == null || targetBallPrefab == null)
            {
                error = "Bomb and target ball prefabs must both be assigned.";
                return false;
            }

            for (int i = 0; i < 9; i++)
            {
                if (GetSpawnPointTransform((BallDirection)i) == null)
                {
                    error = "All nine experiment spawn points must be assigned.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureMaterials();
        }

        public void StartRound(
            ProjectileScheduleSet scheduleSet,
            Action roundComplete)
        {
            StopRound();
            ResolveReferences();
            EnsureMaterials();
            onRoundComplete = roundComplete;
            roundCoroutine = StartCoroutine(RunRound(scheduleSet));
        }

        // Practice uses the current head pose without starting a timed round.
        public GameObject SpawnSingleBall(BallSpawnEntry entry)
        {
            ResolveReferences();
            EnsureMaterials();
            if (cameraRig == null || cameraRig.centerEyeAnchor == null)
            {
                return null;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(
                cameraRig.centerEyeAnchor.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
            {
                flatForward = Vector3.forward;
            }

            int previousCount = activeBalls.Count;
            SpawnBall(entry, cameraRig.centerEyeAnchor.position,
                Quaternion.LookRotation(flatForward.normalized, Vector3.up),
                ResolveLayoutOrigin());
            return activeBalls.Count > previousCount
                ? activeBalls[activeBalls.Count - 1] : null;
        }

        public void StopRound()
        {
            if (roundCoroutine != null)
            {
                StopCoroutine(roundCoroutine);
                roundCoroutine = null;
            }

            for (int i = 0; i < activeBalls.Count; i++)
            {
                if (activeBalls[i] != null)
                {
                    Destroy(activeBalls[i]);
                }
            }

            activeBalls.Clear();
            onRoundComplete = null;
        }

        private IEnumerator RunRound(ProjectileScheduleSet scheduleSet)
        {
            if (scheduleSet == null
                || cameraRig == null
                || cameraRig.centerEyeAnchor == null)
            {
                roundCoroutine = null;
                Action missingSetupCallback = onRoundComplete;
                onRoundComplete = null;
                missingSetupCallback?.Invoke();
                yield break;
            }

            Vector3 originPosition = cameraRig.centerEyeAnchor.position;
            Vector3 flatForward = Vector3.ProjectOnPlane(
                cameraRig.centerEyeAnchor.forward,
                Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
            {
                flatForward = Vector3.forward;
            }

            Quaternion originRotation = Quaternion.LookRotation(
                flatForward.normalized,
                Vector3.up);

            Transform layoutOrigin = ResolveLayoutOrigin();

            // Play the authored schedule verbatim - every entry, at its own
            // authored spawnTimeSeconds - so a round runs for the full
            // duration the schedule asset was designed for.
            IReadOnlyList<BallSpawnEntry> entries = scheduleSet.Entries;
            float elapsedSeconds = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                BallSpawnEntry entry = entries[i];
                float waitSeconds = Mathf.Max(
                    0f,
                    entry.spawnTimeSeconds - elapsedSeconds);
                if (waitSeconds > 0f)
                {
                    yield return new WaitForSeconds(waitSeconds);
                    elapsedSeconds += waitSeconds;
                }

                SpawnBall(
                    entry,
                    originPosition,
                    originRotation,
                    layoutOrigin);
            }

            while (activeBalls.Count > 0)
            {
                activeBalls.RemoveAll(ball => ball == null);
                if (activeBalls.Count > 0)
                {
                    yield return null;
                }
            }

            roundCoroutine = null;
            Action callback = onRoundComplete;
            onRoundComplete = null;
            callback?.Invoke();
        }

        private void SpawnBall(
            BallSpawnEntry entry,
            Vector3 originPosition,
            Quaternion originRotation,
            Transform layoutOrigin)
        {
            Vector3 spawnPosition = ResolveSpawnPosition(
                entry,
                originPosition,
                originRotation,
                layoutOrigin);
            // Spawn locations remain anchored to the round-start layout, but
            // every projectile aims at the participant's latest head position.
            // ExperimentBall stores this direction once, so the projectile is
            // still dodgeable and never homes after launch.
            Vector3 targetPosition = ResolveTargetPosition(
                cameraRig.centerEyeAnchor.position,
                entry.targetHeightOffsetMeters);

            GameObject prefab = entry.ballType == BallType.MustAvoid
                ? bombBallPrefab
                : targetBallPrefab;

            GameObject ball;
            Material fallbackMaterial = null;

            if (prefab != null)
            {
                // Real Bomb/Target model prefab - it already carries its own
                // ExperimentBall component, collider and material, so we
                // only need to place it and configure it below.
                ball = Instantiate(prefab, transform);
            }
            else
            {
                // No prefab assigned for this ball type - fall back to a
                // plain colored sphere so the round never breaks.
                ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.transform.SetParent(transform, false);
                ball.transform.localScale =
                    Vector3.one * (ballRadiusMeters * 2f);
                fallbackMaterial = entry.ballType == BallType.MustAvoid
                    ? mustAvoidMaterial
                    : optionalHitMaterial;
            }

            ball.name = "ExperimentBall_" + entry.ballType;

            ExperimentBall behaviour = ball.GetComponent<ExperimentBall>();
            if (behaviour == null)
            {
                behaviour = ball.AddComponent<ExperimentBall>();
            }

            behaviour.Configure(
                spawnPosition,
                targetPosition,
                entry.speedMetersPerSecond,
                entry.ballType,
                cameraRig.centerEyeAnchor,
                fallbackMaterial);

            activeBalls.Add(ball);
        }

        public static Vector3 ResolveTargetPosition(
            Vector3 currentHeadPosition,
            float targetHeightOffsetMeters)
        {
            return currentHeadPosition
                + Vector3.up * targetHeightOffsetMeters;
        }

        // Spawn origin resolution order:
        // 1. The scene spawn point Transform assigned for this entry's
        //    direction (real placed spawner object - preferred).
        // 2. If that direction has no spawn point assigned, fall back to the
        //    Front spawn point (there are no rear spawners at all).
        // 3. If even the Front spawn point is missing, fall back to the old
        //    camera-relative computed position so the round never breaks.
        private Vector3 ResolveSpawnPosition(
            BallSpawnEntry entry,
            Vector3 originPosition,
            Quaternion originRotation,
            Transform layoutOrigin)
        {
            Transform spawnPoint = GetSpawnPointTransform(entry.direction);
            if (spawnPoint == null)
            {
                spawnPoint = frontSpawnPoint;
            }

            if (spawnPoint != null)
            {
                // Spawn exactly where the marker is physically placed in
                // the room, independent of the participant's current
                // position or gaze direction.
                return spawnPoint.position
                    + Vector3.up * entry.spawnHeightOffsetMeters;
            }

            Vector3 fallbackDirectionWorld =
                originRotation * BallSpawnEntry.DirectionVector(entry.direction);
            return originPosition
                + fallbackDirectionWorld * entry.spawnDistanceMeters
                + Vector3.up * entry.spawnHeightOffsetMeters;
        }

        private Transform ResolveLayoutOrigin()
        {
            if (spawnLayoutOrigin != null)
            {
                return spawnLayoutOrigin;
            }

            Transform candidate = leftSpawnPoint == null
                ? null
                : leftSpawnPoint.parent;
            if (candidate == null)
            {
                return null;
            }

            for (int i = 1; i < 9; i++)
            {
                Transform point = GetSpawnPointTransform((BallDirection)i);
                if (point == null || point.parent != candidate)
                {
                    return null;
                }
            }

            return candidate;
        }

        private Transform GetSpawnPointTransform(BallDirection direction)
        {
            switch (direction)
            {
                case BallDirection.Left: return leftSpawnPoint;
                case BallDirection.LeftMid: return leftMidSpawnPoint;
                case BallDirection.FrontLeft: return frontLeftSpawnPoint;
                case BallDirection.FrontLeftMid: return frontLeftMidSpawnPoint;
                case BallDirection.Front: return frontSpawnPoint;
                case BallDirection.FrontRightMid: return frontRightMidSpawnPoint;
                case BallDirection.FrontRight: return frontRightSpawnPoint;
                case BallDirection.RightMid: return rightMidSpawnPoint;
                case BallDirection.Right: return rightSpawnPoint;
                default: return null;
            }
        }

        private void EnsureMaterials()
        {
            if (mustAvoidMaterial == null)
            {
                mustAvoidMaterial = CreateMaterial(mustAvoidColor);
            }

            if (optionalHitMaterial == null)
            {
                optionalHitMaterial = CreateMaterial(optionalHitColor);
            }
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                Debug.LogError(
                    "[ExperimentBallSpawner] No suitable shader found "
                    + "for ball material.");
                return null;
            }

            return new Material(shader)
            {
                hideFlags = HideFlags.DontSave,
                color = color
            };
        }

        private void ResolveReferences()
        {
            if (cameraRig == null)
            {
                cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }
        }

        private void OnDestroy()
        {
            StopRound();
            DestroyMaterial(mustAvoidMaterial);
            DestroyMaterial(optionalHitMaterial);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
    }
}
