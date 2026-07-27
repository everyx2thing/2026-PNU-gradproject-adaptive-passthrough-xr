using System;
using UnityEngine;
using UnityEngine.Android;

namespace TeamVR.AdaptivePassthrough
{
    [DisallowMultipleComponent]
    public sealed class QuestCameraPermissionCoordinator : MonoBehaviour
    {
        public const string HeadsetCameraPermission =
            "horizonos.permission.HEADSET_CAMERA";

        [SerializeField] private bool requestOnStart = true;

        public event Action<bool> PermissionResolved;

        public bool IsAuthorized
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(HeadsetCameraPermission);
#else
                return false;
#endif
            }
        }

        private void Start()
        {
            if (requestOnStart)
            {
                RequestPermission();
            }
        }

        public void RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                PermissionResolved?.Invoke(true);
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => PermissionResolved?.Invoke(true);
            callbacks.PermissionDenied += _ => PermissionResolved?.Invoke(false);
            Permission.RequestUserPermission(HeadsetCameraPermission, callbacks);
#else
            Debug.Log("[DynamicRisk] HEADSET_CAMERA permission is only available on Quest.");
            PermissionResolved?.Invoke(false);
#endif
        }
    }
}
