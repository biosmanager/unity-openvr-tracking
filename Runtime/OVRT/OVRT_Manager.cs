using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;
using Valve.VR;
using System.Threading.Tasks;
using System.Threading;

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

        public ETrackingUniverseOrigin trackingUniverse = ETrackingUniverseOrigin.TrackingUniverseStanding;
        public UpdateMode updateMode = UpdateMode.Update;
        public bool useSteamVrTrackerRoles = true;
        public float predictedSecondsToPhotonsFromNow = 0.0f;

        public bool[] ConnectedDeviceIndices { get; private set; } = new bool[OpenVR.k_unMaxTrackedDeviceCount];
        public Dictionary<string, string> Bindings { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> SteamVrTrackerBindings { get; private set; } = new Dictionary<string, string>();

        private bool _isInitialized = false;

        private TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private UnityAction<int, bool> _onDeviceConnected;
        private string _steamVrConfigPath = null;

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private Task _task;
        private ConcurrentQueue<VREvent_t> _eventQueue = new ConcurrentQueue<VREvent_t>();
        private CVRSystem _vrSystem;
        private double _currentSamplesPerSecond;


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
            var capacity = OpenVR.System.GetStringTrackedDeviceProperty(deviceId, prop, null, 0, ref error);
            if (capacity > 1)
            {
                var result = new System.Text.StringBuilder((int)capacity);
                OpenVR.System.GetStringTrackedDeviceProperty(deviceId, prop, result, capacity, ref error);
                return result.ToString();
            }
            return (error != ETrackedPropertyError.TrackedProp_Success) ? error.ToString() : "<unknown>";
        }


        private void Awake()
        {
            _onDeviceConnected += OnDeviceConnected;

            var token = cancellationTokenSource.Token;

            _task = Task.Run(async () =>
            {
                Init();

                var sw = System.Diagnostics.Stopwatch.StartNew();

                while (!token.IsCancellationRequested)
                {
                    PollPosesAndEvents();

                    _currentSamplesPerSecond = 1000.0 / sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                }
            }, token);
        }

        private void OnEnable()
        {
            OVRT_Events.TrackedDeviceConnected.AddListener(_onDeviceConnected);
        }

        private void OnDisable()
        {
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
                SpinWait.SpinUntil(() => OpenVR.IsHmdPresent(), TimeSpan.FromSeconds(10));
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

        private void OnDeviceConnected(int index, bool connected)
        {
            ConnectedDeviceIndices[index] = connected;
        }

        // Runs in task
        private void PollPosesAndEvents()
        {
            _vrSystem.GetDeviceToAbsoluteTrackingPose(trackingUniverse, predictedSecondsToPhotonsFromNow, _poses);

            var vrEvent = new VREvent_t();
            while (_vrSystem.PollNextEvent(ref vrEvent, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VREvent_t))))
            {
                _eventQueue.Enqueue(vrEvent);   
            }
        }

        private void UpdatePoses()
        {
            if (!_isInitialized) return;

            // Process OpenVR event queue
            VREvent_t vrEvent;
            while (_eventQueue.TryDequeue(out vrEvent))
            {
                switch ((EVREventType)vrEvent.eventType)
                {
                    case EVREventType.VREvent_TrackedDeviceActivated:
                        OVRT_Events.TrackedDeviceConnected.Invoke((int)vrEvent.trackedDeviceIndex, true);
                        break;
                    case EVREventType.VREvent_TrackedDeviceDeactivated:
                        OVRT_Events.TrackedDeviceConnected.Invoke((int)vrEvent.trackedDeviceIndex, false);
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