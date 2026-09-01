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

        private readonly List<GameObject> activeBalls = new List<GameObject>();
        private Material mustAvoidMaterial;
        private Material optionalHitMaterial;
        private Coroutine roundCoroutine;
        private Action onRoundComplete;

        public bool IsRoundActive => roundCoroutine != null;

        // Read by ExperimentThreatIndicatorController to draw edge-of-screen
        // arrows for balls currently outside the player's field of view.
        public IReadOnlyList<GameObject> ActiveBalls => activeBalls;

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

                SpawnBall(entry, originPosition, originRotation);
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
            Quaternion originRotation)
        {
            Vector3 spawnPosition = ResolveSpawnPosition(
                entry,
                originPosition,
                originRotation);
            Vector3 targetPosition = originPosition
                + Vector3.up * entry.targetHeightOffsetMeters;

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
            Quaternion originRotation)
        {
            Transform spawnPoint = GetSpawnPointTransform(entry.direction);
            if (spawnPoint == null)
            {
                spawnPoint = frontSpawnPoint;
            }

            if (spawnPoint != null)
            {
                return spawnPoint.position
                    + Vector3.up * entry.spawnHeightOffsetMeters;
            }

            Vector3 fallbackDirectionWorld =
                originRotation * BallSpawnEntry.DirectionVector(entry.direction);
            return originPosition
                + fallbackDirectionWorld * entry.spawnDistanceMeters
                + Vector3.up * entry.spawnHeightOffsetMeters;
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
