using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// Attach directly to the Main Camera. Translates with the bike's world
    /// position AND yaws with its heading (now that heading comes from a real
    /// kinematic bicycle model, rotating the camera to match is what makes the
    /// view actually look like riding down a curving path -- the road bends
    /// ahead, and the camera turns to face where the bike is actually pointed).
    /// A small fixed downward pitch is applied on top so you can see the road
    /// surface rather than just the horizon.
    /// </summary>
    public class FirstPersonRig : MonoBehaviour
    {
        [SerializeField] private BikeGameController controller;
        [SerializeField] private RoadMeshGenerator road;

        [Header("Rig")]
        [SerializeField] private float eyeHeight = 1.4f;
        [SerializeField] private float downwardTiltDegrees = 8f;
        [SerializeField] private float roadRebuildBehindMargin = 5f;

        [Header("Cosmetic handlebar tilt")]
        [Tooltip("RectTransform of the handlebar UI image. Set its pivot to roughly where the stem/steerer tube would be (e.g. x=0.5, y=0.25 if the bar image has the stem near the bottom) so it visually pivots like a real handlebar instead of spinning around its center.")]
        [SerializeField] private RectTransform handlebarVisual;
        [Tooltip("Degrees the handlebar image rotates per degree of actual front wheel angle. 1.0 = handlebar visually matches the wheel angle 1:1.")]
        [SerializeField] private float handlebarDegreesPerSteerDegree = 1.0f;

        private void LateUpdate()
        {
            if (controller == null) return;

            transform.position = new Vector3(controller.LateralY, eyeHeight, controller.DistanceX);
            transform.rotation = Quaternion.Euler(downwardTiltDegrees, controller.HeadingDeg, 0f);

            if (handlebarVisual != null)
            {
                float angle = controller.SteerAngleDeg * handlebarDegreesPerSteerDegree;
                handlebarVisual.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }

            road?.Rebuild(controller.DistanceX - roadRebuildBehindMargin);
        }
    }
}
