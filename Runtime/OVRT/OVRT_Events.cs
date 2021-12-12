using System;
using UnityEngine;
using UnityEngine.Events;
using Valve.VR;

namespace OVRT
{
    public static class OVRT_Events
    {
        public static UnityEvent<uint> TrackedDeviceActivated = new UnityEvent<uint>();
        public static UnityEvent TrackedDeviceDeactivated = new UnityEvent();

        public static UnityEvent<TrackedDevicePose_t[]> NewPoses = new UnityEvent<TrackedDevicePose_t[]>();
        public static UnityEvent<string, TrackedDevicePose_t, uint> NewBoundPose = new UnityEvent<string, TrackedDevicePose_t, uint>();
    }
}