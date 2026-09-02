using UnityEngine;

namespace TeamVR.Experiment
{
    // Handheld raycast hitscan gun. Purely a physical stimulus/game element
    // - never references the AdaptivePassthrough risk pipeline or its
    // namespace. The gun is permanently attached to attachToRightHand's
    // controller (no grip/pick-up step) - trigger = fire, read directly via
    // OVRInput (the same API family already used in this project for
    // haptics) rather than the Meta XR Interaction SDK's Grab/Trigger
    // building blocks: this project's OVRCameraRig has no IController
    // data-source bridge wired up for that SDK yet, and that path could not
    // be verified working without a physical headset. NOTE: muzzleLocalPosition
    // and handHoldLocalPosition/handHoldLocalEulerAngles are best-guess
    // defaults based on the model's bounding box - nudge them in the
    // Inspector after a first in-headset look if the barrel direction or
    // in-hand pose looks off.
    [DefaultExecutionOrder(730)]
    [DisallowMultipleComponent]
    public sealed class ExperimentGun : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private ExperimentRoundController roundController;
        [SerializeField] private bool attachToRightHand = true;

        [Header("Muzzle / Hold Pose (tune after first in-headset check)")]
        [SerializeField] private Vector3 muzzleLocalPosition = new Vector3(0f, 0.02f, 0.30f);
        [SerializeField] private Vector3 handHoldLocalPosition = new Vector3(0f, -0.03f, 0.05f);
        [SerializeField] private Vector3 handHoldLocalEulerAngles = Vector3.zero;

        [Header("Behaviour")]
        [SerializeField, Min(0.5f)] private float maxRangeMeters = 8f;
        [SerializeField, Min(0.01f)] private float tracerDurationSeconds = 0.05f;

        [Header("Visuals")]
        [SerializeField] private Sprite reticleSprite;
        [SerializeField] private GameObject muzzleFlashPrefab;

        private Transform heldByHand;
        private OVRInput.Controller heldController = OVRInput.Controller.None;
        private Transform reticleTransform;
        private SpriteRenderer reticleRenderer;
        private LineRenderer tracer;
        private ParticleSystem muzzleFlashInstance;
        private Material tracerMaterial;
        private float tracerHideAtTime = -1f;

        public bool CanFire => roundController != null
            && roundController.RoundActive;

        public void Configure(
            OVRCameraRig rig,
            ExperimentRoundController controller)
        {
            cameraRig = rig;
            roundController = controller;
        }

        public bool ValidateConfiguration(out string error)
        {
            ResolveReferences();
            if (cameraRig == null || roundController == null)
            {
                error = "Experiment gun camera rig or round controller is missing.";
                return false;
            }

            if (reticleSprite == null || muzzleFlashPrefab == null)
            {
                error = "Experiment gun visual references are incomplete.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            ResolveReferences();
            BuildReticle();
            BuildTracer();
            BuildMuzzleFlash();
        }

        private void OnDestroy()
        {
            if (reticleTransform != null)
            {
                Destroy(reticleTransform.gameObject);
            }

            if (tracerMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(tracerMaterial);
                }
                else
                {
                    DestroyImmediate(tracerMaterial);
                }
            }
        }

        private void Update()
        {
            ResolveReferences();
            UpdateHold();
            bool isHeld = heldByHand != null && CanFire;

            Vector3 muzzleWorldPos = transform.TransformPoint(muzzleLocalPosition);
            Vector3 muzzleWorldDir = transform.forward;

            RaycastHit hit = default;
            bool hasHit = isHeld && Physics.Raycast(
                muzzleWorldPos, muzzleWorldDir, out hit, maxRangeMeters);

            UpdateReticle(isHeld, hasHit, hit, muzzleWorldPos, muzzleWorldDir);

            if (isHeld
                && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, heldController))
            {
                Fire(hasHit, hit, muzzleWorldPos, muzzleWorldDir);
            }

            if (tracerHideAtTime >= 0f && Time.time >= tracerHideAtTime)
            {
                tracer.enabled = false;
                tracerHideAtTime = -1f;
            }
        }

        private void UpdateHold()
        {
            if (cameraRig == null)
            {
                return;
            }

            Transform hand = attachToRightHand
                ? cameraRig.rightHandAnchor
                : cameraRig.leftHandAnchor;
            if (hand == null)
            {
                return;
            }

            heldByHand = hand;
            heldController = attachToRightHand
                ? OVRInput.Controller.RTouch
                : OVRInput.Controller.LTouch;

            transform.position = heldByHand.TransformPoint(handHoldLocalPosition);
            transform.rotation =
                heldByHand.rotation * Quaternion.Euler(handHoldLocalEulerAngles);
        }

        private void Fire(
            bool hasHit, RaycastHit hit, Vector3 muzzlePos, Vector3 dir)
        {
            Vector3 endPoint = hasHit ? hit.point : muzzlePos + dir * maxRangeMeters;

            if (hasHit)
            {
                ExperimentBall ball = hit.collider.GetComponentInParent<ExperimentBall>();
                if (ball != null)
                {
                    ball.RegisterShotHit();
                }
            }

            if (muzzleFlashInstance != null)
            {
                muzzleFlashInstance.transform.position = muzzlePos;
                muzzleFlashInstance.transform.rotation = Quaternion.LookRotation(dir);
                muzzleFlashInstance.Play();
            }

            if (tracer != null)
            {
                tracer.enabled = true;
                tracer.SetPosition(0, muzzlePos);
                tracer.SetPosition(1, endPoint);
                tracerHideAtTime = Time.time + tracerDurationSeconds;
            }
        }

        private void UpdateReticle(
            bool isHeld,
            bool hasHit,
            RaycastHit hit,
            Vector3 muzzlePos,
            Vector3 dir)
        {
            if (reticleTransform == null)
            {
                return;
            }

            if (!isHeld)
            {
                reticleTransform.gameObject.SetActive(false);
                return;
            }

            reticleTransform.gameObject.SetActive(true);
            Vector3 point = hasHit ? hit.point : muzzlePos + dir * maxRangeMeters;
            reticleTransform.position = point;

            if (cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                Vector3 toCamera = cameraRig.centerEyeAnchor.position - point;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    reticleTransform.rotation = Quaternion.LookRotation(
                        -toCamera.normalized, Vector3.up);
                }
            }
        }

        private void BuildReticle()
        {
            GameObject reticleObject = new GameObject("GeneratedGunReticle");
            reticleRenderer = reticleObject.AddComponent<SpriteRenderer>();
            reticleRenderer.sprite = reticleSprite;
            reticleRenderer.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            reticleObject.transform.localScale = Vector3.one * 0.12f;
            reticleTransform = reticleObject.transform;
            reticleObject.SetActive(false);
        }

        private void BuildTracer()
        {
            GameObject tracerObject = new GameObject("GeneratedGunTracer");
            tracerObject.transform.SetParent(transform, false);
            tracer = tracerObject.AddComponent<LineRenderer>();
            tracer.positionCount = 2;
            tracer.startWidth = 0.01f;
            tracer.endWidth = 0.004f;
            tracer.useWorldSpace = true;
            tracer.enabled = false;

            Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (lineShader == null)
            {
                lineShader = Shader.Find("Sprites/Default");
            }

            tracerMaterial = new Material(lineShader);
            tracer.material = tracerMaterial;
            tracer.startColor = new Color(1f, 0.95f, 0.6f, 1f);
            tracer.endColor = new Color(1f, 0.8f, 0.3f, 0f);
        }

        private void BuildMuzzleFlash()
        {
            if (muzzleFlashPrefab == null)
            {
                return;
            }

            GameObject instance = Instantiate(muzzleFlashPrefab, transform);
            instance.transform.localPosition = muzzleLocalPosition;
            muzzleFlashInstance = instance.GetComponent<ParticleSystem>();
        }

        private void ResolveReferences()
        {
            if (cameraRig == null)
            {
                cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (roundController == null)
            {
                roundController =
                    FindAnyObjectByType<ExperimentRoundController>();
            }
        }
    }
}
