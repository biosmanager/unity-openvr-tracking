using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using Valve.VR;

namespace OVRT
{
    public class OVRT_Manager : MonoBehaviour
    {
        public enum UpdateMode
        {
            FixedUpdate,
            Update,
            LateUpdate,
            OnPreCull
        }

        public UpdateMode updateMode = UpdateMode.Update;
        public bool useSteamVrTrackerRoles = false;

        public Dictionary<string, string> Bindings { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> SteamVrTrackerBindings { get; private set; } = new Dictionary<string, string>();

        private CVRSystem _vrSystem;
        private TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private void Start()
        {
            var initError = EVRInitError.None;
            _vrSystem = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Other);

            if (initError != EVRInitError.None)
            {
                var initErrorString = OpenVR.GetStringForHmdError(initError);
                Debug.LogError($"[OVRT] Could not initialize OpenVR tracking: {initErrorString}");
                return;
            }

            Debug.Log($"[OVRT] Initialized OpenVR tracking.");

            //for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            //{
            //    if (_vrSystem.IsTrackedDeviceConnected(i))
            //    {
            //        var serialNumber = GetStringProperty(ETrackedDeviceProperty.Prop_SerialNumber_String, i);

            //        Debug.Log($"{i}: {_vrSystem.GetTrackedDeviceClass(i).ToString()} {serialNumber}");
            //    }
            //}

            UpdateSteamVrTrackerBindings();
        }

        private void OnDestroy()
        {
            OpenVR.Shutdown();
        }

        private void FixedUpdate()
        {
            if (updateMode == UpdateMode.FixedUpdate)
            {
                UpdatePoses();
            }
        }

        private void Update()
        {
            if (updateMode == UpdateMode.Update)
            {
                UpdatePoses();
            }
        }

        private void LateUpdate()
        {
            if (updateMode == UpdateMode.LateUpdate)
            {
                UpdatePoses();
            }
        }

        private void OnPreCull()
        {
            if (updateMode == UpdateMode.OnPreCull)
            {
                UpdatePoses();
            }
        }

        private void UpdatePoses()
        {
            _vrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0.0f, _poses);


            // Process OpenVR event queue
            var vrEvent = new VREvent_t();
            while (_vrSystem.PollNextEvent(ref vrEvent, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VREvent_t))))
            {
                switch ((EVREventType)vrEvent.eventType)
                {
                    case EVREventType.VREvent_TrackedDeviceActivated:
                        OVRT_Events.TrackedDeviceActivated.Invoke(vrEvent.trackedDeviceIndex);
                        break;
                    case EVREventType.VREvent_TrackedDeviceDeactivated:
                        OVRT_Events.TrackedDeviceDeactivated.Invoke();
                        break;
                    case EVREventType.VREvent_TrackersSectionSettingChanged:
                        UpdateSteamVrTrackerBindings();
                        break;
                    default:
                        break;
                }

            }

            OVRT_Events.NewPoses.Invoke(_poses);

            for (uint i = 0; i < _poses.Length; i++)
            {
                var pose = _poses[i];
                if (pose.bDeviceIsConnected && pose.bPoseIsValid)
                {
                    var serialNumber = GetStringProperty(ETrackedDeviceProperty.Prop_SerialNumber_String, i);

                    string binding;
                    if (Bindings.TryGetValue(serialNumber, out binding))
                    {
                        OVRT_Events.NewBoundPose.Invoke(binding, pose, i);
                    }

                    if (useSteamVrTrackerRoles)
                    {
                        string trackerBinding;
                        if (SteamVrTrackerBindings.TryGetValue(serialNumber, out trackerBinding))
                        {
                            OVRT_Events.NewBoundPose.Invoke(trackerBinding, pose, i);
                        }
                    }
                }
            }
        }

        public string GetStringProperty(ETrackedDeviceProperty prop, uint deviceId)
        {
            var error = ETrackedPropertyError.TrackedProp_Success;
            var capacity = _vrSystem.GetStringTrackedDeviceProperty(deviceId, prop, null, 0, ref error);
            if (capacity > 1)
            {
                var result = new System.Text.StringBuilder((int)capacity);
                _vrSystem.GetStringTrackedDeviceProperty(deviceId, prop, result, capacity, ref error);
                return result.ToString();
            }
            return (error != ETrackedPropertyError.TrackedProp_Success) ? error.ToString() : "<unknown>";
        }

        public bool ReadBindingsFromJson(string json)
        {
            try
            {
                var newBindings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                Bindings = newBindings;
                return true;
            }
            catch (JsonException e)
            {
                return false;
            }
        }

        public bool ConvertBindingsToJson(out string json)
        {
            try
            {
                json = JsonConvert.SerializeObject(Bindings);
                return true;
            }
            catch (JsonException e)
            {
                json = "";
                return false;
            }
        }

        public Dictionary<string, string> GetSteamVrConnectedTrackerBindings()
        {
            uint[] trackerIndices = new uint[OpenVR.k_unMaxTrackedDeviceCount];
            var numTrackers = _vrSystem.GetSortedTrackedDeviceIndicesOfClass(ETrackedDeviceClass.GenericTracker, trackerIndices, 0);

            Dictionary<string, string> trackerBindings = new Dictionary<string, string>();

            foreach (var trackerIndex in trackerIndices)
            {
                var serialNumber = GetStringProperty(ETrackedDeviceProperty.Prop_SerialNumber_String, trackerIndex);

                EVRSettingsError error = EVRSettingsError.None;
                var value = new StringBuilder(4096);
                OpenVR.Settings.GetString(OpenVR.k_pch_Trackers_Section, $"/devices/htc/vive_tracker{serialNumber}", value, 4096, ref error);
                if (error == EVRSettingsError.None)
                {
                    trackerBindings.Add(serialNumber, value.ToString());
                }
            }

            return trackerBindings;
        }

        private void UpdateSteamVrTrackerBindings()
        {
            var trackerBindings = GetSteamVrConnectedTrackerBindings();
            SteamVrTrackerBindings = trackerBindings;
        }
    }
}