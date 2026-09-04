using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;

namespace HalMenu.Core
{
    /// <summary>
    /// Lives on the persistent Bootstrap scene. Subscribes to the topics that describe
    /// *which* devices exist and their live/calibration state, and re-broadcasts that
    /// information to the rest of the app (menu UI, game catalog matching) through
    /// plain C# events, so no other script needs to know about ROS message types.
    /// </summary>
    public class DeviceRegistryService : MonoBehaviour
    {
        public static DeviceRegistryService Instance { get; private set; }

        [Header("ROS Topics (must match the Python nodes)")]
        [SerializeField] private string devicesAvailableTopic = "/devices/available";
        [SerializeField] private string calibrationStateTopic = "/calibration/state";
        [SerializeField] private string deviceStateTopic = "/hal/device_state";

        [Header("Liveness")]
        [Tooltip("If no /devices/available message arrives within this many seconds, the HAL is treated as offline in the UI.")]
        [SerializeField] private float staleAfterSeconds = 2.0f;

        public IReadOnlyList<DeviceInfo> CurrentDevices => _devices;
        public IReadOnlyDictionary<string, bool> CalibrationFlags => _calibrationFlags;
        public float TimeSinceLastHeartbeat => Time.realtimeSinceStartup - _lastAvailableMsgTime;
        public bool IsHalOnline => _hasReceivedAtLeastOnce && TimeSinceLastHeartbeat < staleAfterSeconds;

        public event Action<IReadOnlyList<DeviceInfo>> OnDeviceListUpdated;
        public event Action<IReadOnlyDictionary<string, bool>> OnCalibrationStateUpdated;
        public event Action<string, float, string> OnDeviceStateUpdated; // deviceId, normalizedValue, unit

        private List<DeviceInfo> _devices = new List<DeviceInfo>();
        private Dictionary<string, bool> _calibrationFlags = new Dictionary<string, bool>();
        private float _lastAvailableMsgTime = -999f;
        private bool _hasReceivedAtLeastOnce;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            var ros = ROSConnection.GetOrCreateInstance();
            ros.Subscribe<StringMsg>(devicesAvailableTopic, OnDevicesAvailable);
            ros.Subscribe<StringMsg>(calibrationStateTopic, OnCalibrationState);
            ros.Subscribe<StringMsg>(deviceStateTopic, OnDeviceState);
        }

        private void OnDevicesAvailable(StringMsg msg)
        {
            _lastAvailableMsgTime = Time.realtimeSinceStartup;
            _hasReceivedAtLeastOnce = true;

            try
            {
                _devices = JsonConvert.DeserializeObject<List<DeviceInfo>>(msg.data) ?? new List<DeviceInfo>();
                OnDeviceListUpdated?.Invoke(_devices);
            }
            catch (JsonException e)
            {
                Debug.LogWarning($"[DeviceRegistryService] Failed to parse /devices/available payload: {e.Message}");
            }
        }

        private void OnCalibrationState(StringMsg msg)
        {
            try
            {
                _calibrationFlags = JsonConvert.DeserializeObject<Dictionary<string, bool>>(msg.data) ?? new Dictionary<string, bool>();
                OnCalibrationStateUpdated?.Invoke(_calibrationFlags);
            }
            catch (JsonException e)
            {
                Debug.LogWarning($"[DeviceRegistryService] Failed to parse /calibration/state payload: {e.Message}");
            }
        }

        private void OnDeviceState(StringMsg msg)
        {
            try
            {
                var state = JsonConvert.DeserializeObject<Dictionary<string, object>>(msg.data);
                if (state == null || !state.ContainsKey("device_id")) return;

                string deviceId = state["device_id"].ToString();
                string unit = state.ContainsKey("unit") ? state["unit"].ToString() : "";

                // normalized_value is usually a scalar, but SignalProcessor.normalize()
                // returns a list when a device has more than one channel. We only need
                // a representative scalar for the menu's live preview bar.
                float value;
                var rawVal = state["normalized_value"];
                if (rawVal is Newtonsoft.Json.Linq.JArray arr && arr.Count > 0)
                    value = arr[0].ToObject<float>();
                else
                    value = Convert.ToSingle(rawVal);

                OnDeviceStateUpdated?.Invoke(deviceId, value, unit);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeviceRegistryService] Failed to parse /hal/device_state payload: {e.Message}");
            }
        }

        public bool IsCalibrating(string deviceId) => _calibrationFlags.TryGetValue(deviceId, out var v) && v;
    }
}
