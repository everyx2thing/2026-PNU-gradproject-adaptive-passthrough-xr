using TeamVR.AdaptivePassthrough;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TeamVR.Experiment
{
    [DefaultExecutionOrder(690)]
    [DisallowMultipleComponent]
    public sealed class ExperimentGameRoot : MonoBehaviour
    {
        [SerializeField] private ExperimentRoundController roundController;
        [SerializeField] private ExperimentBallSpawner ballSpawner;
        [SerializeField] private ExperimentMenuController menuController;
        [SerializeField] private ExperimentGun gun;
        [SerializeField] private ExperimentScoreSystem scoreSystem;
        [SerializeField] private ExperimentScoreHud scoreHud;
        [SerializeField]
        private ExperimentThreatIndicatorController threatIndicator;

        private void Awake()
        {
            ResolveChildren();
            OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
            Camera eyeCamera = cameraRig != null
                && cameraRig.centerEyeAnchor != null
                ? cameraRig.centerEyeAnchor.GetComponent<Camera>()
                : null;
            if (eyeCamera == null)
            {
                return;
            }

            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].renderMode == RenderMode.WorldSpace)
                {
                    canvases[i].worldCamera = eyeCamera;
                }
            }
        }

        public bool ValidateConfiguration(out string error)
        {
            ResolveChildren();
            if (roundController == null || ballSpawner == null
                || menuController == null || gun == null
                || scoreSystem == null || scoreHud == null
                || threatIndicator == null)
            {
                error = "ExperimentGameRoot is missing a required game component.";
                return false;
            }

            if (!ballSpawner.ValidateConfiguration(out error)
                || !menuController.ValidateConfiguration(out error)
                || !gun.ValidateConfiguration(out error)
                || !scoreSystem.ValidateConfiguration(out error)
                || !scoreHud.ValidateConfiguration(out error)
                || !threatIndicator.ValidateConfiguration(out error))
            {
                return false;
            }

            if (!HasExactlyOne<ExperimentGameRoot>(out error)
                || !HasExactlyOne<OVRCameraRig>(out error)
                || !HasExactlyOne<OVRManager>(out error)
                || !HasExactlyOne<EventSystem>(out error)
                || !HasExactlyOne<SelectivePassthroughController>(out error)
                || !HasExactlyOne<StaticPassthroughPolicyController>(out error)
                || !HasExactlyOne<DynamicPassthroughPolicyController>(out error)
                || !HasExactlyOne<DynamicRiskSessionLogger>(out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void ResolveChildren()
        {
            roundController ??=
                GetComponentInChildren<ExperimentRoundController>(true);
            ballSpawner ??=
                GetComponentInChildren<ExperimentBallSpawner>(true);
            menuController ??=
                GetComponentInChildren<ExperimentMenuController>(true);
            gun ??= GetComponentInChildren<ExperimentGun>(true);
            scoreSystem ??=
                GetComponentInChildren<ExperimentScoreSystem>(true);
            scoreHud ??=
                GetComponentInChildren<ExperimentScoreHud>(true);
            threatIndicator ??=
                GetComponentInChildren<ExperimentThreatIndicatorController>(
                    true);
        }

        private static bool HasExactlyOne<T>(out string error)
            where T : Object
        {
            int count = FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length;
            if (count == 1)
            {
                error = string.Empty;
                return true;
            }

            error = typeof(T).Name + " count must be 1, but was " + count + ".";
            return false;
        }
    }
}
