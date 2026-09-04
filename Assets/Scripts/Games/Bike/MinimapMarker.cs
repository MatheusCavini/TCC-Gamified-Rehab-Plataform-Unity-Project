using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// Put this on the minimap player marker (a flat arrow/triangle sprite lying
    /// on the ground, seen from directly above by the minimap camera). Positions
    /// it at the bike's location and rotates it to point in the bike's current
    /// heading, so the arrow visibly swings left/right as you steer -- the
    /// minimap's answer to the camera yawing in the first-person view.
    ///
    /// Assumes the marker mesh/sprite is authored pointing "up" in its own local
    /// space before any rotation is applied (typical for an arrow sprite).
    /// </summary>
    public class MinimapMarker : MonoBehaviour
    {
        [SerializeField] private BikeGameController controller;
        [SerializeField] private float height = 0.05f;

        private void LateUpdate()
        {
            if (controller == null) return;
            transform.position = new Vector3(controller.LateralY, height, controller.DistanceX);
            // Lay flat (X=90) so it reads correctly from directly above, then yaw
            // by heading so it points the direction the bike is actually facing.
            transform.rotation = Quaternion.Euler(90f, controller.HeadingDeg, 0f);
        }
    }
}
