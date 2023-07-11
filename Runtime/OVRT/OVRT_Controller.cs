using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using Valve.VR;
using static OVRT.OVRT_TrackedObject;

namespace OVRT
{
    public sealed class OVRT_Controller : OVRT_TrackedDevice
    {
        public enum Role
        {
            LeftHand,
            RightHand
        }

        public Role role;

        public UnityEvent<EVRButtonId, bool> onButtonPressed = new UnityEvent<EVRButtonId, bool>();

        private UnityAction<TrackedDevicePose_t[]> _onNewPosesAction;
        private UnityAction<int, EVRButtonId, bool> _onButtonPressedAction;
        private UnityAction<int> _onTrackedDeviceRoleChangedAction;

        public void TriggerHapticPulse(ushort durationMicroSec = 500, EVRButtonId buttonId = EVRButtonId.k_EButton_SteamVR_Touchpad)
        {
            if (DeviceIndex == -1)
                return;

            if (OpenVR.System == null) return;
            
            var axisId = (uint)buttonId - (uint)EVRButtonId.k_EButton_Axis0;
            OpenVR.System.TriggerHapticPulse((uint)DeviceIndex, axisId, (char)durationMicroSec);           
        }


        private void OnDeviceConnected(int index, bool connected)
        {
            if (OpenVR.System == null) return;

            var roleIndex = FindIndexForRole();
            
            if (roleIndex > -1)
            {
                IsConnected = connected;
                UpdateIndex();
            }
        }

        private void OnTrackedDeviceRoleChanged(int index)
        {
            UpdateIndex();
        }

        private int FindIndexForRole()
        {
            if (OpenVR.System == null) return -1;

            ETrackedControllerRole trackedRole = ETrackedControllerRole.Invalid;
            switch (role)
            {
                case Role.LeftHand:
                    trackedRole = ETrackedControllerRole.LeftHand; break;
                case Role.RightHand:
                    trackedRole = ETrackedControllerRole.RightHand; break;
            }

            if (trackedRole == ETrackedControllerRole.LeftHand || trackedRole == ETrackedControllerRole.RightHand)
            {
                return (int)OpenVR.System.GetTrackedDeviceIndexForControllerRole(trackedRole);
            }
            else
            {
                return -1;
            }
        }

        private void OnNewPoses(TrackedDevicePose_t[] poses)
        {
            if (DeviceIndex == -1)
                return;

            var i = DeviceIndex;

            IsValid = false;

            if (i < 0 || poses.Length <= i)
                return;

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

        private void OnButtonPressed(int index, EVRButtonId button, bool pressed) 
        {
            if (index == DeviceIndex)
            {
                onButtonPressed.Invoke(button, pressed);    
            }
        }

        private void Awake()
        {
            _onNewPosesAction += OnNewPoses;
            _onButtonPressedAction += OnButtonPressed;
            _onDeviceConnectedAction += OnDeviceConnected;
            _onTrackedDeviceRoleChangedAction += OnTrackedDeviceRoleChanged;    
        }

        private void Start()
        {
            UpdateIndex();
        }

        private void OnEnable()
        {
            UpdateIndex();

            OVRT_Events.NewPoses.AddListener(_onNewPosesAction);
            OVRT_Events.ButtonPressed.AddListener(_onButtonPressedAction);
            OVRT_Events.TrackedDeviceConnected.AddListener(_onDeviceConnectedAction);
            OVRT_Events.TrackedDeviceRoleChanged.AddListener(_onTrackedDeviceRoleChangedAction);
        }

        private void OnDisable()
        {
            OVRT_Events.NewPoses.RemoveListener(_onNewPosesAction);
            OVRT_Events.ButtonPressed.RemoveListener(_onButtonPressedAction);
            OVRT_Events.TrackedDeviceConnected.RemoveListener(_onDeviceConnectedAction);    
            IsValid = false;
            IsConnected = false;
        }

        private void UpdateIndex()
        {
            DeviceIndex = FindIndexForRole();
            onDeviceIndexChanged.Invoke(DeviceIndex);
        }
    }
}
