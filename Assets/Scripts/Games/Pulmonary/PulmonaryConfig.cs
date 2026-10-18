using System;
using UnityEngine;

namespace HalMenu.Games.Pulmonary
{
    /// <summary>
    /// Therapist-adjustable settings of the balloon breathing exercise. The setup
    /// panel edits these, and the last values used are remembered between
    /// sessions (PlayerPrefs) so a therapist does not retype a protocol every time.
    /// </summary>
    [Serializable]
    public class PulmonaryConfig
    {
        private const string PrefsKey = "HalMenu.Pulmonary.Config";

        [Tooltip("Duration of the INFLATE phase: the patient blows into the device.")]
        [Min(1f)] public float inflateSeconds = 4f;
        [Tooltip("Duration of the DEFLATE phase: the patient relaxes.")]
        [Min(1f)] public float deflateSeconds = 4f;
        [Tooltip("Number of inflate + deflate repetitions.")]
        [Min(1)] public int cycles = 5;
        [Tooltip("Effort the patient should reach while inflating, as a fraction of the calibrated range (0-1).")]
        [Range(0.2f, 1f)] public float inflateTarget = 0.8f;

        public PulmonaryConfig Clone() => (PulmonaryConfig)MemberwiseClone();

        public PulmonaryConfig Sanitized()
        {
            inflateSeconds = Mathf.Clamp(inflateSeconds, 1f, 60f);
            deflateSeconds = Mathf.Clamp(deflateSeconds, 1f, 60f);
            cycles = Mathf.Clamp(cycles, 1, 50);
            inflateTarget = Mathf.Clamp(inflateTarget, 0.2f, 1f);
            return this;
        }

        public static PulmonaryConfig LoadOrDefault(PulmonaryConfig fallback)
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var loaded = JsonUtility.FromJson<PulmonaryConfig>(json);
                    if (loaded != null) return loaded.Sanitized();
                }
                catch (ArgumentException)
                {
                    // Corrupt/old payload: fall through to the defaults.
                }
            }
            return fallback.Clone().Sanitized();
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
