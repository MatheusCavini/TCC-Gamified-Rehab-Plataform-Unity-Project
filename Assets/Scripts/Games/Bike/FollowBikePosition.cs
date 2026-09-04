using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// Keeps a transform's X/Z aligned with the bike's world position, at a
    /// configurable fixed height, with an optional forward bias.
    ///
    /// Two uses: the minimap camera (large height, fixed downward pitch set once
    /// in the Inspector as rotation (90,0,0), WITH a positive forwardOffset so the
    /// player sits toward the bottom of the minimap and more of the path ahead is
    /// visible -- the standard "radar" framing) and the on-map player marker
    /// (near-zero height, zero offset, sits exactly on the road under the camera's
    /// forward-biased view).
    /// </summary>
    public class FollowBikePosition : MonoBehaviour
    {
        [SerializeField] private BikeGameController controller;
        [SerializeField] private float height = 0.05f;
        [Tooltip("World units to bias the followed point ahead of the player along Z. Use a positive value (roughly half the minimap camera's orthographic size) on the MINIMAP CAMERA only, so the player appears near the bottom of the minimap instead of dead center. Leave at 0 on the player marker itself.")]
        [SerializeField] private float forwardOffset = 0f;

        private void LateUpdate()
        {
            if (controller == null) return;
            transform.position = new Vector3(controller.LateralY, height, controller.DistanceX + forwardOffset);
        }
    }
}
