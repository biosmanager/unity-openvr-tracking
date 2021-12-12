using UnityEngine;
using UnityEngine.Events;
using Valve.VR;

namespace OVRT
{
    public class OVRT_TrackedObject : MonoBehaviour
    {
        public enum EIndex
        {
            None = -1,
            Hmd = (int)OpenVR.k_unTrackedDeviceIndex_Hmd,
            Device1,
            Device2,
            Device3,
            Device4,
            Device5,
            Device6,
            Device7,
            Device8,
            Device9,
            Device10,
            Device11,
            Device12,
            Device13,
            Device14,
            Device15,
            Device16
        }

        public EIndex index;
        [Tooltip("If not set, relative to parent")]
        public Transform origin;

        public bool IsValid { get; private set; }
        public bool IsConnected { get; private set; }  

        private UnityAction<TrackedDevicePose_t[]> onNewPosesAction;

        private void OnNewPoses(TrackedDevicePose_t[] poses)
        {
            if (index == EIndex.None)
                return;

            var i = (int)index;

            IsValid = false;
            IsConnected = false;

            if (poses.Length <= i)
                return;

            IsConnected = poses[i].bDeviceIsConnected;

            if (!poses[i].bDeviceIsConnected)
                return;

            if (!poses[i].bPoseIsValid)
                return;

            IsValid = true;

            var pose = new OVRT_Utils.RigidTransform(poses[i].mDeviceToAbsoluteTracking);

            if (origin != null)
            {
                transform.position = origin.transform.TransformPoint(pose.pos);
                transform.rotation = origin.rotation * pose.rot;
            }
            else
            {
                transform.localPosition = pose.pos;
                transform.localRotation = pose.rot;
            }
        }

        private void Awake()
        {
            onNewPosesAction += OnNewPoses;
        }

        void OnEnable()
        {
            OVRT_Events.NewPoses.AddListener(onNewPosesAction);

        }

        void OnDisable()
        {
            OVRT_Events.NewPoses.RemoveListener(onNewPosesAction);
            IsValid = false;
        }
    }
}