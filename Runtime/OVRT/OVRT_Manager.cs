using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Valve.VR;

namespace OVRT
{
    /// <summary>
    /// Manages connection to OpenVR and dispatches new poses and events.
    /// </summary>
    public class OVRT_Manager : MonoBehaviour
    {
        public enum UpdateMode
        {
            FixedUpdate,
            Update,
            LateUpdate,
            OnPreCull
        }

        public enum PosePredictionAlgorithm
        {
            OpenVR,
            LinearNoAcc
        }

        public ETrackingUniverseOrigin trackingUniverse = ETrackingUniverseOrigin.TrackingUniverseStanding;
        public bool useSteamVrTrackerRoles = true;

        [Header("Pose prediction")]
        public bool usePosePrediction = true;
        public PosePredictionAlgorithm posePredictionAlgorithm;
        public bool doUpdatePosesBeforeRendering = true;
        public float displayFrequency = 90;
        public float vsyncToPhotonsSeconds = 0.03f;
        public float photonsToVblankSeconds = 0.0f;
        public uint predictNumFramesAheadUpdate = 0;
        public uint predictNumFramesAheadBeforeRender = 1;
        public float additionalPredictionOffsetSeconds = 0.0f;

        [Header("Debug")]
        /*[SerializeField]*/ private bool debug_doComparePoses = false;
        /*[SerializeField]*/ private int debug_numPosesToCompare = 5000;
        /*[SerializeField]*/ private int debug_deviceIndexToCompare = 1;

        public bool[] ConnectedDeviceIndices { get; private set; } = new bool[OpenVR.k_unMaxTrackedDeviceCount];
        public Dictionary<string, string> Bindings { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> SteamVrTrackerBindings { get; private set; } = new Dictionary<string, string>();

        private bool _isInitialized = false;
        private CVRSystem _vrSystem;
        private TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private TrackedDevicePose_t[] _predictedPosesOpenVR = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private TrackedDevicePose_t[] _predictedPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private UnityAction<int, bool> _onDeviceConnected;
        private string _steamVrConfigPath = null;

        private List<Tuple<Vector3, Quaternion, bool>> _debug_poseComparisons = new List<Tuple<Vector3, Quaternion, bool>>();
        private bool _debug_poseComparisonsWereWritten = false;


        public Dictionary<string, string> GetSteamVrTrackerBindings()
        {
            Dictionary<string, string> trackerBindings = new Dictionary<string, string>();

            string steamVrSettingsPath;
            if (_steamVrConfigPath is null)
            {
                steamVrSettingsPath = Path.Combine(OpenVR.RuntimePath(), "../../../config/steamvr.vrsettings");
            }
            else
            {
                steamVrSettingsPath = Path.Combine(_steamVrConfigPath, "steamvr.vrsettings");
            }

            if (!File.Exists(steamVrSettingsPath))
            {
                Debug.LogWarning("[OVRT] Could not find SteamVR configuration file!");
                return trackerBindings;
            }

            var json = File.ReadAllText(steamVrSettingsPath);
            var steamVrSettings = JObject.Parse(json);

            if (steamVrSettings.ContainsKey("trackers"))
            {
                var trackers = steamVrSettings["trackers"].ToObject<Dictionary<string, string>>();
                foreach (var pair in trackers)
                {
                    trackerBindings.Add(pair.Key.Replace("/devices/htc/vive_tracker", ""), pair.Value);
                }
            }

            return trackerBindings;
        }

        public bool ReadBindingsFromJson(string json)
        {
            try
            {
                var newBindings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                Bindings = newBindings;
                return true;
            }
            catch (JsonException)
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
            catch (JsonException)
            {
                json = "";
                return false;
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


        private void Awake()
        {
            _onDeviceConnected += OnDeviceConnected;
            Init();
        }

        private void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
            OVRT_Events.TrackedDeviceConnected.AddListener(_onDeviceConnected);
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            OVRT_Events.TrackedDeviceConnected.RemoveListener(_onDeviceConnected);
            Array.Clear(ConnectedDeviceIndices, 0, ConnectedDeviceIndices.Length);
        }

        private void Init()
        {
            if (!OpenVR.IsRuntimeInstalled())
            {
                Debug.LogError("[OVRT] SteamVR runtime not installed!");
                return;
            }

            // Ensure SteamVR is running
            if (!OpenVR.IsHmdPresent())
            {
                var dummyError = EVRInitError.None;
                OpenVR.Init(ref dummyError, EVRApplicationType.VRApplication_Scene);
                System.Threading.SpinWait.SpinUntil(() => OpenVR.IsHmdPresent(), TimeSpan.FromSeconds(10));
                OpenVR.Shutdown();
            }

            var initError = EVRInitError.None;
            _vrSystem = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Other);

            if (initError != EVRInitError.None)
            {
                var initErrorString = OpenVR.GetStringForHmdError(initError);
                Debug.LogError($"[OVRT] Could not initialize OpenVR tracking: {initErrorString}");
                return;
            }

            _isInitialized = true;

            var openVrPathsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
            if (File.Exists(openVrPathsConfigPath))
            {
                var json = File.ReadAllText(openVrPathsConfigPath);
                var openVrPathsConfig = JObject.Parse(json);

                if (openVrPathsConfig.ContainsKey("config"))
                {
                    var paths = openVrPathsConfig["config"].ToObject<List<string>>();
                    if (paths.Count > 0)
                    {
                        _steamVrConfigPath = paths[0];
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[OVRT] Could not find {openVrPathsConfigPath}!");
            }

            Debug.Log($"[OVRT] Initialized OpenVR tracking.");

            UpdateSteamVrTrackerBindings();
        }

        private void Start()
        {
            if (doUpdatePosesBeforeRendering && QualitySettings.vSyncCount == 0)
            {
                Debug.LogWarning("Pose prediction requires vertical synchronization for sensible prediction results. Set QualitySettings.vSyncCount to 1 or higher.");
            }
        }

        private void OnDestroy()
        {
            OpenVR.Shutdown();
        }

        private void Update()
        {
            Tick();
        }

        private void OnBeforeRender()
        {
            if (doUpdatePosesBeforeRendering)
            {
                if (displayFrequency <= 0)
                {
                    displayFrequency = (float)Screen.currentResolution.refreshRateRatio.value;
                }

                var predictionSeconds = CalculatePredictionSeconds(predictNumFramesAheadBeforeRender, additionalPredictionOffsetSeconds);
                UpdatePoses(predictionSeconds);
                //Debug_ComparePredictedPoses(secondsFromNow);
            }
        }

        [DllImport("OVRT_Native")]
        private static extern bool GetTimeSinceLastVsync(out double secondsSinceLastVsync, out ulong frameCounter);

        [DllImport("OVRT_Native")]
        private static extern double GetLastVsyncTimestamp();

        public float GetFrameDuration()
        {
            return 1f / Math.Max(displayFrequency, 1);
        }

        public float CalculatePredictionSeconds(uint numFramesAhead = 1, float additionalPredictionOffset = 0f)
        {
            if (!GetTimeSinceLastVsync(out double secondsSinceLastVsync, out ulong frameCounter))
            {
                secondsSinceLastVsync = 0f;
                frameCounter = 0;
            }

            float frameDuration = GetFrameDuration();
            float secondsFromNow = 
                Mathf.Max(0, frameDuration - (float)secondsSinceLastVsync) 
                + numFramesAhead * frameDuration
                + vsyncToPhotonsSeconds
                + photonsToVblankSeconds
                + additionalPredictionOffset;

            return secondsFromNow;
        }

        private void OnDeviceConnected(int index, bool connected)
        {
            ConnectedDeviceIndices[index] = connected;
        }

        private void Tick()
        {
            if (!_isInitialized) return;

            // Process OpenVR event queue
            var vrEvent = new VREvent_t();
            while (_vrSystem.PollNextEvent(ref vrEvent, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VREvent_t))))
            {
                switch ((EVREventType)vrEvent.eventType)
                {
                    case EVREventType.VREvent_TrackedDeviceActivated:
                        OVRT_Events.TrackedDeviceConnected.Invoke((int)vrEvent.trackedDeviceIndex, true);
                        break;
                    case EVREventType.VREvent_TrackedDeviceDeactivated:
                        OVRT_Events.TrackedDeviceConnected.Invoke((int)vrEvent.trackedDeviceIndex, false);
                        break;
                    case EVREventType.VREvent_TrackedDeviceRoleChanged:
                        OVRT_Events.TrackedDeviceRoleChanged.Invoke((int)vrEvent.trackedDeviceIndex);
                        break;
                    case EVREventType.VREvent_ButtonPress:
                        OVRT_Events.ButtonPressed.Invoke((int)vrEvent.trackedDeviceIndex, (EVRButtonId)vrEvent.data.controller.button, true);
                        break;
                    case EVREventType.VREvent_ButtonUnpress:
                        OVRT_Events.ButtonPressed.Invoke((int)vrEvent.trackedDeviceIndex, (EVRButtonId)vrEvent.data.controller.button, false);
                        break;
                    case EVREventType.VREvent_TrackersSectionSettingChanged:
                        // Allow some time until SteamVR configuration file has been updated on disk
                        Invoke("UpdateSteamVrTrackerBindings", 1.0f);
                        break;
                    case EVREventType.VREvent_ShowRenderModels:
                        OVRT_Events.HideRenderModelsChanged.Invoke(false);
                        break;
                    case EVREventType.VREvent_HideRenderModels:
                        OVRT_Events.HideRenderModelsChanged.Invoke(true);
                        break;
                    case EVREventType.VREvent_ModelSkinSettingsHaveChanged:
                        OVRT_Events.ModelSkinSettingsHaveChanged.Invoke();
                        break;
                    case EVREventType.VREvent_Quit:
                    case EVREventType.VREvent_ProcessQuit:
                    case EVREventType.VREvent_DriverRequestedQuit:
                        return;
                    default:
                        break;
                }
            }

            var predictionSeconds = predictNumFramesAheadUpdate * GetFrameDuration();
            UpdatePoses(predictionSeconds);
        }

        private void Debug_ComparePredictedPoses(float predictedSecondsToPhotonsFromNow)
        {
            if (!debug_doComparePoses || _debug_poseComparisonsWereWritten) return;

            var poseDifferences = ComparePoses(_predictedPosesOpenVR, _predictedPoses);

            _debug_poseComparisons.Add(poseDifferences[debug_deviceIndexToCompare]);

            var sb = new StringBuilder();
            sb.AppendLine($"{predictedSecondsToPhotonsFromNow} delta");

            for (int i = 0; i < poseDifferences.Count; i++)
            {
                var (posDiff, rotDiff, valid) = poseDifferences[i];

                if (!valid) continue;

                sb.AppendLine($"{_vrSystem.GetTrackedDeviceClass((uint)i)} [{i}]: {posDiff.magnitude} {rotDiff.eulerAngles}");
            }

            Debug.Log(sb.ToString());

            if (_debug_poseComparisons.Count >= debug_numPosesToCompare)
            {

                var output = _debug_poseComparisons.Select(t =>
                {
                    var diffRot = t.Item2.eulerAngles;
                    var row = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                        t.Item1.x,
                        t.Item1.y,
                        t.Item1.z,
                        t.Item2.w,
                        t.Item2.x,
                        t.Item2.y,
                        t.Item2.z,
                        diffRot.x,
                        diffRot.y,
                        diffRot.z
                    );
                    return row;
                }).ToList();
                output.Insert(0, "diffPosX,diffPosY,diffPosZ,diffQuatW,diffQuatX,diffQuatY,diffQuatZ,diffRotX,diffRotY,diffRotZ");
                File.WriteAllLines("diffs.csv", output);
                _debug_poseComparisonsWereWritten = true;
                Debug.Log("Wrote diffs");
            }
        }

        private void UpdatePoses(float predictedSecondsToPhotonsFromNow)
        {
            if (!_isInitialized) return;

            if (debug_doComparePoses || posePredictionAlgorithm == PosePredictionAlgorithm.OpenVR)
            {
                _vrSystem.GetDeviceToAbsoluteTrackingPose(trackingUniverse, usePosePrediction ? predictedSecondsToPhotonsFromNow : 0f, _predictedPosesOpenVR);
                _poses = _predictedPosesOpenVR;
            }
            if (debug_doComparePoses || posePredictionAlgorithm == PosePredictionAlgorithm.LinearNoAcc)
            {
                var currentPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
                _vrSystem.GetDeviceToAbsoluteTrackingPose(trackingUniverse, 0f, currentPoses);

                if (usePosePrediction)
                {
                    PredictPoses(currentPoses, predictedSecondsToPhotonsFromNow, out _predictedPoses);
                    _poses = _predictedPoses;
                }
                else
                {
                    _poses = currentPoses;
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
                        OVRT_Events.NewBoundPose.Invoke(binding, pose, (int)i);
                    }

                    if (useSteamVrTrackerRoles)
                    {
                        string trackerBinding;
                        if (SteamVrTrackerBindings.TryGetValue(serialNumber, out trackerBinding))
                        {
                            OVRT_Events.NewBoundPose.Invoke(trackerBinding, pose, (int)i);
                        }
                    }
                }
            }
        }

        private static List<Tuple<Vector3, Quaternion, bool>> ComparePoses(TrackedDevicePose_t[] posesA, TrackedDevicePose_t[] posesB)
        {
            if (posesA.Length != posesB.Length)
            {
                throw new IndexOutOfRangeException("Poses A and B must have the same size!");
            }

            var differences = new List<Tuple<Vector3, Quaternion, bool>>();

            for (int i = 0; i < posesA.Length; i++) {
                var transformA = new OVRT_Utils.RigidTransform(posesA[i].mDeviceToAbsoluteTracking);
                var transformB = new OVRT_Utils.RigidTransform(posesB[i].mDeviceToAbsoluteTracking);

                var posDiff = transformA.pos - transformB.pos;
                var rotDiff = transformB.rot * Quaternion.Inverse(transformA.rot);

                differences.Add(new Tuple<Vector3, Quaternion, bool>(posDiff, rotDiff, posesA[i].bPoseIsValid));
            }

            return differences;
        }

        private static Quaternion QuaternionExp(Vector3 v)
        {
            float x = v.x / 2f;
            float y = v.y / 2f;
            float z = v.z / 2f;

            float th2 = x * x + y * y + z * z;
            float th = Mathf.Sqrt(th2);

            float c = Mathf.Cos(th);
            float s = th2 < Mathf.Sqrt(120 * Mathf.Epsilon) ? 1 - th2 / 6f : Mathf.Sin(th) / th;

            return new Quaternion(s * x, s * y, s * z, c);
        }

        private void PredictPoses(TrackedDevicePose_t[] poses, float deltaSec, out TrackedDevicePose_t[] predicedPoses) 
        {
            predicedPoses = new TrackedDevicePose_t[poses.Length];

            if (!_isInitialized) return;
            
            for (uint i = 0; i < _poses.Length; i++) {
                var pose = poses[i];
                var newPose = pose;

                var rigidTransform = new OVRT_Utils.RigidTransform(poses[i].mDeviceToAbsoluteTracking);
                var newRigidTransform = rigidTransform;

                newRigidTransform.pos.x = rigidTransform.pos.x + deltaSec * pose.vVelocity.v0;
                newRigidTransform.pos.y = rigidTransform.pos.y + deltaSec * pose.vVelocity.v1;
                newRigidTransform.pos.z = rigidTransform.pos.z - deltaSec * pose.vVelocity.v2;

                Vector3 W;
                W.x = deltaSec * -pose.vAngularVelocity.v0;
                W.y = deltaSec * -pose.vAngularVelocity.v1;
                W.z = deltaSec * pose.vAngularVelocity.v2;
                newRigidTransform.rot = QuaternionExp(W) * rigidTransform.rot;

                newPose.mDeviceToAbsoluteTracking = newRigidTransform.ToHmdMatrix34();
                predicedPoses[i] = newPose;
            }
        }

        private void UpdateSteamVrTrackerBindings()
        {
            var trackerBindings = GetSteamVrTrackerBindings();
            SteamVrTrackerBindings = trackerBindings;
            OVRT_Events.TrackerRolesChanged.Invoke();
        }

        private static bool runningTemporarySession = false;
        public static bool InitializeTemporarySession()
        {
            if (Application.isEditor)
            {
                //bool needsInit = (!active && !usingNativeSupport && !runningTemporarySession);

                EVRInitError initError = EVRInitError.None;
                OpenVR.GetGenericInterface(OpenVR.IVRCompositor_Version, ref initError);
                bool needsInit = initError != EVRInitError.None;

                if (needsInit)
                {
                    EVRInitError error = EVRInitError.None;
                    OpenVR.Init(ref error, EVRApplicationType.VRApplication_Other);

                    if (error != EVRInitError.None)
                    {
                        Debug.LogError("[OVRT] Could not initialize OpenVR tracking: " + error.ToString());
                        return false;
                    }

                    runningTemporarySession = true;
                }


                return needsInit;
            }

            return false;
        }

        public static void ExitTemporarySession()
        {
            if (runningTemporarySession)
            {
                OpenVR.Shutdown();
                runningTemporarySession = false;
            }
        }
    }
}