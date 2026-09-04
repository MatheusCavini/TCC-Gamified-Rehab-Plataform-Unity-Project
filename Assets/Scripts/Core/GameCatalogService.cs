using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalMenu.Core
{
    public struct GameAvailability
    {
        public GameManifest manifest;
        public bool isPlayable;

        /// <summary>
        /// Devices assigned to this game, grouped by the capability slot they fill.
        /// A capability with minCount 0 (e.g. optional pressure sensors) may map to
        /// an empty list -- that's a valid, playable assignment, not a missing one.
        /// </summary>
        public Dictionary<CapabilityType, List<string>> assignedDeviceIds;

        public string reason; // human readable, e.g. "Needs 1 more Intensity device"
    }

    /// <summary>
    /// Recomputes, every time the device list changes, which games in the catalog
    /// can currently be played given the connected/available devices.
    ///
    /// v1 matching rule: each device contributes exactly one unit of ONE capability
    /// type (encoder -> Position, emg -> Intensity). A game is playable if, for every
    /// required capability, enough distinct devices of that derived type are available.
    /// Devices are assigned to a game in registry order (first N encoders / first N
    /// EMGs). There is no manual "assign this specific device to slot 2" step in v1 --
    /// flag this to the team if a therapy protocol ever needs a fixed physical device
    /// in a fixed slot rather than "any device of this type".
    /// </summary>
    public class GameCatalogService : MonoBehaviour
    {
        public static GameCatalogService Instance { get; private set; }

        [SerializeField] private GameCatalog catalog;

        public event Action<List<GameAvailability>> OnCatalogUpdated;
        public List<GameAvailability> Current { get; private set; } = new List<GameAvailability>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (DeviceRegistryService.Instance != null)
                DeviceRegistryService.Instance.OnDeviceListUpdated += _ => Recompute();
            Recompute();
        }

        private static CapabilityType? DeriveCapability(string deviceType)
        {
            switch (deviceType)
            {
                case "encoder": return CapabilityType.Position;
                case "emg": return CapabilityType.Intensity;
                default: return null; // e.g. "imu" - not matched to any game slot yet
            }
        }

        public void Recompute()
        {
            var devices = DeviceRegistryService.Instance != null
                ? DeviceRegistryService.Instance.CurrentDevices
                : new List<DeviceInfo>();

            var byCapability = new Dictionary<CapabilityType, List<string>>();
            foreach (var d in devices)
            {
                var cap = DeriveCapability(d.type);
                if (cap == null) continue;
                if (!byCapability.TryGetValue(cap.Value, out var list))
                {
                    list = new List<string>();
                    byCapability[cap.Value] = list;
                }
                list.Add(d.device_id);
            }

            var result = new List<GameAvailability>();
            if (catalog != null)
            {
                foreach (var game in catalog.games)
                {
                    bool playable = true;
                    var assigned = new Dictionary<CapabilityType, List<string>>();
                    string reason = "Ready";

                    foreach (var req in game.requiredCapabilities)
                    {
                        byCapability.TryGetValue(req.type, out var available);
                        int have = available?.Count ?? 0;

                        if (have < req.minCount)
                        {
                            playable = false;
                            reason = $"Needs {req.minCount - have} more {req.type} device(s)";
                        }

                        // Record the assignment regardless of whether the minimum was met,
                        // so an optional slot (minCount 0) with zero devices correctly
                        // resolves to an empty list rather than being skipped/null.
                        assigned[req.type] = available == null
                            ? new List<string>()
                            : available.Take(Mathf.Min(req.maxCount, have)).ToList();
                    }

                    result.Add(new GameAvailability
                    {
                        manifest = game,
                        isPlayable = playable,
                        assignedDeviceIds = assigned,
                        reason = reason
                    });
                }
            }

            Current = result;
            OnCatalogUpdated?.Invoke(Current);
        }
    }
}
