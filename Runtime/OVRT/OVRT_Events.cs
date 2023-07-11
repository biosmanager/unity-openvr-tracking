using System;
using UnityEngine;
using UnityEngine.Events;
using Valve.VR;

namespace OVRT
{
    public static class OVRT_Events
    {
        public static UnityEvent<int, bool> TrackedDeviceConnected = new UnityEvent<int, bool>();

        public static UnityEvent<int> TrackedDeviceRoleChanged = new UnityEvent<int>();

        public static UnityEvent<TrackedDevicePose_t[]> NewPoses = new UnityEvent<TrackedDevicePose_t[]>();
        public static UnityEvent<string, TrackedDevicePose_t, int> NewBoundPose = new UnityEvent<string, TrackedDevicePose_t, int>();

        public static UnityEvent<int, EVRButtonId, bool> ButtonPressed = new UnityEvent<int, EVRButtonId, bool>();

        public static UnityEvent TrackerRolesChanged = new UnityEvent();

        public static UnityEvent<bool> HideRenderModelsChanged = new UnityEvent<bool>();
        public static UnityEvent ModelSkinSettingsHaveChanged = new UnityEvent();

        public static UnityEvent<OVRT_RenderModel, bool> RenderModelLoaded = new UnityEvent<OVRT_RenderModel, bool>();
    }
}