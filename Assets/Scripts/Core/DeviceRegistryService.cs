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
        public event Action<DeviceChannelState> OnChannelStateUpdated;

        private List<DeviceInfo> _devices = new List<DeviceInfo>();
        private Dictionary<string, bool> _calibrationFlags = new Dictionary<string, bool>();
        private float _lastAvailableMsgTime = -999f;
        private bool _hasReceivedAtLeastOnce;
        private readonly Dictionary<string, DeviceChannelState> _channelStates = new Dictionary<string, DeviceChannelState>();

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

                var channelState = new DeviceChannelState
                {
                    DeviceId = deviceId,
                    ChannelIds = ReadStrings(state, "channel_ids"),
                    ChannelNames = ReadStrings(state, "channel_names"),
                    Values = ReadValues(state)
                };
                _channelStates[deviceId] = channelState;
                OnChannelStateUpdated?.Invoke(channelState);

                // Preserve the scalar contract used by game scripts: use the first
                // normalized value when ROS supplies a vector.
                float value = channelState.Values.Length > 0 ? channelState.Values[0] : 0f;
                OnDeviceStateUpdated?.Invoke(deviceId, value, unit);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeviceRegistryService] Failed to parse /hal/device_state payload: {e.Message}");
            }
        }

        public bool TryGetLatestChannelState(string deviceId, out DeviceChannelState state) => _channelStates.TryGetValue(deviceId, out state);

        private static List<string> ReadStrings(Dictionary<string, object> state, string key)
        {
            if (!state.TryGetValue(key, out var raw) || raw == null) return new List<string>();
            var token = raw as Newtonsoft.Json.Linq.JToken;
            return token is Newtonsoft.Json.Linq.JArray array
                ? array.ToObject<List<string>>() ?? new List<string>()
                : new List<string>();
        }

        private static float[] ReadValues(Dictionary<string, object> state)
        {
            if (!state.TryGetValue("normalized_value", out var raw) || raw == null) return Array.Empty<float>();
            var token = raw as Newtonsoft.Json.Linq.JToken;
            if (token is Newtonsoft.Json.Linq.JArray array) return array.ToObject<float[]>() ?? Array.Empty<float>();
            return new[] { Convert.ToSingle(raw) };
        }

        public bool IsCalibrating(string deviceId) => _calibrationFlags.TryGetValue(deviceId, out var v) && v;
    }

    public sealed class DeviceChannelState
    {
        public string DeviceId;
        public List<string> ChannelIds = new List<string>();
        public List<string> ChannelNames = new List<string>();
        public float[] Values = Array.Empty<float>();
    }
}
