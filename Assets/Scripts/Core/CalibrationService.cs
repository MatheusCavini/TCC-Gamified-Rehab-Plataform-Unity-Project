using System;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std; // TriggerRequest / TriggerResponse (std_srvs/Trigger)

// TODO -- FIX AFTER MESSAGE GENERATION:
// Run "Robotics > Generate ROS Messages" pointed at your hal_interfaces package
// (which must contain srv/SetString.srv), then open the generated file under
// Assets/RosMessages/HalInterfaces/srv/SetString.cs and copy whatever namespace
// is declared at the top of that file into the using statement below. It will
// likely be RosMessageTypes.HalInterfaces, but the generator derives it from your
// package name, so verify rather than assume.
using RosMessageTypes.HalInterfaces;

using UnityEngine;

namespace HalMenu.Core
{
    /// <summary>
    /// Wraps the three calibration-related ROS services exposed by
    /// signal_processing_node.py so UI code never touches ROS types directly.
    /// </summary>
    public class CalibrationService : MonoBehaviour
    {
        public static CalibrationService Instance { get; private set; }

        [SerializeField] private string toggleDeviceService = "/calibration/toggle_device";
        [SerializeField] private string saveProfileService = "/calibration/save_profile";
        [SerializeField] private string loadProfileService = "/calibration/load_profile";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            var ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterRosService<SetStringRequest, SetStringResponse>(toggleDeviceService);
            ros.RegisterRosService<TriggerRequest, TriggerResponse>(saveProfileService);
            ros.RegisterRosService<TriggerRequest, TriggerResponse>(loadProfileService);
        }

        /// <summary>Toggles ROM calibration recording for one specific device.</summary>
        public void ToggleCalibration(string deviceId, Action<bool, string> onResult)
        {
            // NOTE: verify the generated SetStringRequest constructor / field name
            // ("data") matches your actual hal_interfaces/srv/SetString.srv definition.
            var req = new SetStringRequest(deviceId);
            ROSConnection.GetOrCreateInstance().SendServiceMessage<SetStringResponse>(
                toggleDeviceService, req,
                resp => onResult?.Invoke(resp.success, resp.message));
        }

        public void SaveProfile(Action<bool, string> onResult)
        {
            var req = new TriggerRequest();
            ROSConnection.GetOrCreateInstance().SendServiceMessage<TriggerResponse>(
                saveProfileService, req,
                resp => onResult?.Invoke(resp.success, resp.message));
        }

        public void LoadProfile(Action<bool, string> onResult)
        {
            var req = new TriggerRequest();
            ROSConnection.GetOrCreateInstance().SendServiceMessage<TriggerResponse>(
                loadProfileService, req,
                resp => onResult?.Invoke(resp.success, resp.message));
        }
    }
}
