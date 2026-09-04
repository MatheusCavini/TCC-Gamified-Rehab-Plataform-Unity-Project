using System;
using System.Collections.Generic;

namespace HalMenu.Core
{
    /// <summary>
    /// Mirrors the JSON payload published by each device node on /device/info
    /// and relayed (as a JSON array) by device_manager_node on /devices/available.
    /// Field names match the JSON keys exactly ("device_id", "type", "capabilities",
    /// "units") so Newtonsoft.Json can bind to it directly with no manual mapping.
    /// </summary>
    [Serializable]
    public class DeviceInfo
    {
        public string device_id;
        public string type;
        public List<string> capabilities = new List<string>();
        public List<string> units = new List<string>();

        public override bool Equals(object obj) => obj is DeviceInfo other && other.device_id == device_id;
        public override int GetHashCode() => device_id?.GetHashCode() ?? 0;
    }
}
