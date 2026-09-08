using System;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Purely visual/physical stimulus. Never references the
    // AdaptivePassthrough risk pipeline - this component has no effect on
    // risk calculation. Two ways to resolve a ball: the gun's hitscan
    // raycast calls RegisterShotHit() directly, or the ball drifts within
    // bodyHitRadiusMeters of the participant's body and resolves itself as a
    // body hit. A Collider must remain on this GameObject (on the prefab)
    // so Physics.Raycast from the gun can find it - unlike the old
    // hand-proximity prototype, this script never removes it.
    [DefaultExecutionOrder(750)]
    [DisallowMultipleComponent]
    public sealed class ExperimentBall : MonoBehaviour
    {
        // Fired right before the GameObject is destroyed, in addition to
        // (never instead of) the existing ExperimentScoreSystem call below -
        // lets external systems (e.g. the tutorial controller) observe the
        // exact outcome of a specific ball without duplicating hit-detection
        // logic. No round/score code subscribes to these; purely additive.
        public event Action<ExperimentBall> ShotHit;
        public event Action<ExperimentBall> BodyHit;
        public event Action<ExperimentBall> Expired;

        [Header("Body Hit Cylinder (horizontal reach + vertical range around eye height)")]
        [SerializeField] private float bodyHitRadiusMeters = 0.4f;
        [SerializeField] private float bodyTopOffsetMeters = 0.15f;
        [SerializeField] private float bodyBottomOffsetMeters = 1.5f;
        [SerializeField] private float overshootMeters = 1.5f;

        [Header("Orientation (nudge if the model's front face isn't +Z)")]
        [SerializeField] private Vector3 faceRotationOffsetEulerAngles = Vector3.zero;

        [Header("SFX (spawn sound only - hit/penalty sounds live on ExperimentScoreSystem)")]
        [SerializeField] private AudioClip spawnProjectileClip;

        [Header("Bomb-only VFX (leave null on the target prefab)")]
        [SerializeField] private GameObject bombExplosionEffectPrefab;

        private Vector3 travelDirection;
        private float speedMetersPerSecond;
        private float maxTravelDistanceMeters;
        private float traveledDistanceMeters;
        private BallType ballType;
        private Transform bodyReferenceTransform;
        private ExperimentScoreSystem scoreSystem;
        private bool resolved;

        public BallType BallType => ballType;
        public Vector3 FaceRotationOffsetEulerAngles => faceRotationOffsetEulerAngles;

        private void Awake()
        {
            ResolveReferences();
        }

        public void Configure(
            Vector3 spawnWorldPosition,
            Vector3 targetWorldPosition,
            float speed,
            BallType type,
            Transform bodyReference,
            Material material)
        {
            transform.position = spawnWorldPosition;
            Vector3 toTarget = targetWorldPosition - spawnWorldPosition;
            travelDirection = toTarget.sqrMagnitude > 0.0001f
                ? toTarget.normalized
                : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(travelDirection, Vector3.up)
                * Quaternion.Euler(faceRotationOffsetEulerAngles);
            speedMetersPerSecond = Mathf.Max(0.05f, speed);
            maxTravelDistanceMeters =
                toTarget.magnitude + Mathf.Max(0f, overshootMeters);
            traveledDistanceMeters = 0f;
            ballType = type;
            bodyReferenceTransform = bodyReference;
            resolved = false;

            MeshRenderer meshRenderer = GetComponentInChildren<MeshRenderer>();
            if (meshRenderer != null && material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            if (spawnProjectileClip != null)
            {
                AudioSource.PlayClipAtPoint(spawnProjectileClip, spawnWorldPosition);
            }
        }

        // Called by ExperimentGun when its hitscan raycast hits this ball.
        public void RegisterShotHit()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            ResolveReferences();
            ShotHit?.Invoke(this);
            scoreSystem?.RegisterShotHit(ballType);

            if (ballType == BallType.MustAvoid && bombExplosionEffectPrefab != null)
            {
                GameObject explosion = Instantiate(
                    bombExplosionEffectPrefab,
                    transform.position,
                    Quaternion.identity);
                Destroy(explosion, 2f);
            }

            Destroy(gameObject);
        }

        private void Update()
        {
            if (resolved)
            {
                return;
            }

            float step = speedMetersPerSecond * Time.deltaTime;
            transform.position += travelDirection * step;
            traveledDistanceMeters += step;

            if (IsTouchingBody())
            {
                RegisterBodyHit();
                return;
            }

            if (traveledDistanceMeters >= maxTravelDistanceMeters)
            {
                resolved = true;
                Expired?.Invoke(this);
                Destroy(gameObject);
            }
        }

        private void RegisterBodyHit()
        {
            resolved = true;
            ResolveReferences();
            BodyHit?.Invoke(this);
            scoreSystem?.RegisterBodyHit(ballType);
            Destroy(gameObject);
        }

        private bool IsTouchingBody()
        {
            if (bodyReferenceTransform == null)
            {
                return false;
            }

            Vector3 headPosition = bodyReferenceTransform.position;
            Vector3 ballPosition = transform.position;

            float horizontalDistance = Vector2.Distance(
                new Vector2(ballPosition.x, ballPosition.z),
                new Vector2(headPosition.x, headPosition.z));
            if (horizontalDistance > bodyHitRadiusMeters)
            {
                return false;
            }

            float verticalOffset = headPosition.y - ballPosition.y; // + = below head
            return verticalOffset >= -bodyTopOffsetMeters
                && verticalOffset <= bodyBottomOffsetMeters;
        }

        private void ResolveReferences()
        {
            if (scoreSystem == null)
            {
                scoreSystem = FindAnyObjectByType<ExperimentScoreSystem>();
            }
        }
    }
}
