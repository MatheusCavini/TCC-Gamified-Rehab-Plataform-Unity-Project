using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalMenu.Core
{
    /// <summary>
    /// The capability "alphabet" games are matched against. Extend this enum as new
    /// device types are added to the HAL (e.g. IMU -> Orientation once a game needs it).
    /// </summary>
    public enum CapabilityType
    {
        Position,    // e.g. rotary encoder angle -> 1 DoF position channel
        Intensity,   // e.g. EMG envelope -> 1 DoF intensity/effort channel
        Orientation  // reserved for IMU-driven games; not derived/matched yet
    }

    [Serializable]
    public class CapabilityRequirement
    {
        public CapabilityType type;
        [Min(0)] public int minCount = 1;
        [Min(0)] public int maxCount = 1;
    }

    [CreateAssetMenu(fileName = "NewGameManifest", menuName = "HalMenu/Game Manifest")]
    public class GameManifest : ScriptableObject
    {
        public string gameId;
        public string displayName;
        [TextArea] public string description;
        public Sprite icon;

        [Tooltip("Scene to load when the therapist presses Play. Must be added to Build Settings.")]
        public string sceneName;

        public List<CapabilityRequirement> requiredCapabilities = new List<CapabilityRequirement>();
    }
}
