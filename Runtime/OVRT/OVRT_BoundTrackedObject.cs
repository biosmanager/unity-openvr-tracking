using UnityEngine;
using UnityEngine.Events;
using Valve.VR;

namespace OVRT
{
    public class OVRT_BoundTrackedObject : MonoBehaviour
    {
        public string binding;
        [Tooltip("If not set, relative to parent")]
        public Transform origin;

        public uint DeviceIndex { get; private set; }
        public bool IsValid { get; private set; }
        public bool IsConnected { get; private set; }

        private UnityAction<string, TrackedDevicePose_t, uint> onNewBoundPoseAction;

        private void OnNewBoundPose(string binding, TrackedDevicePose_t pose, uint deviceIndex)
        {
            if (this.binding != binding)
                return;

            IsValid = false;

            DeviceIndex = deviceIndex;
            IsConnected = pose.bDeviceIsConnected;

            if (!pose.bDeviceIsConnected)
                return;

            if (!pose.bPoseIsValid)
                return;

            IsValid = true;

            var rigidTransform = new OVRT_Utils.RigidTransform(pose.mDeviceToAbsoluteTracking);

            if (origin != null)
            {
                transform.position = origin.transform.TransformPoint(rigidTransform.pos);
                transform.rotation = origin.rotation * rigidTransform.rot;
            }
            else
            {
                transform.localPosition = rigidTransform.pos;
                transform.localRotation = rigidTransform.rot;
            }
        }

        private void Awake()
        {
            onNewBoundPoseAction += OnNewBoundPose;
        }

        void OnEnable()
        {
            OVRT_Events.NewBoundPose.AddListener(onNewBoundPoseAction);

        }

        void OnDisable()
        {
            OVRT_Events.NewBoundPose.RemoveListener(onNewBoundPoseAction);
            IsValid = false;
        }
    }
}