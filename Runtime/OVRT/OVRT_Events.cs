using System;
using UnityEngine;
using UnityEngine.Events;
using Valve.VR;

namespace OVRT
{
    public static class OVRT_Events
    {
        public static UnityEvent<int, bool> TrackedDeviceConnected = new UnityEvent<int, bool>();

        public static UnityEvent<TrackedDevicePose_t[]> NewPoses = new UnityEvent<TrackedDevicePose_t[]>();
        public static UnityEvent<string, TrackedDevicePose_t, int> NewBoundPose = new UnityEvent<string, TrackedDevicePose_t, int>();
    }
}