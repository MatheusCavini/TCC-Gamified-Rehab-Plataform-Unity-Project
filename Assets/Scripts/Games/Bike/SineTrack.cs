using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// The track is defined purely as a function x = f(z) -- lateral offset as a
    /// function of forward distance. No colliders, no pre-baked path asset, so
    /// "how far off-path is the player" stays a cheap analytic lookup (TargetY)
    /// instead of a nearest-point-on-spline search.
    ///
    /// Pure math only -- visualization is handled by RoadMeshGenerator, which
    /// builds an actual 3D mesh from this function. That one mesh is what both the
    /// first-person camera and the top-down minimap camera see, so there's no
    /// separate "minimap art" to keep in sync with the real track shape.
    /// </summary>
    public class SineTrack : MonoBehaviour
    {
        [Header("Path Shape")]
        [SerializeField] private float amplitude = 3.0f;
        [SerializeField] private float frequency = 0.15f; // radians per world unit of distance
        [SerializeField] private float phase = 0f;

        /// <summary>Lateral (X) offset of the path centerline at a given forward distance (Z).</summary>
        public float TargetY(float distanceX) => amplitude * Mathf.Sin(distanceX * frequency + phase);

        /// <summary>Slope at distanceX -- exposed for anyone who wants to bank the camera/bike visually later. Not used by gameplay scoring.</summary>
        public float TargetSlope(float distanceX) => amplitude * frequency * Mathf.Cos(distanceX * frequency + phase);
    }
}
