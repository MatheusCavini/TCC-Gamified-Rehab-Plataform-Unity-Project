using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// A finite ground plane that reads as infinite: every frame it's recentered
    /// under the followed point (X/Z only, never Y), and the material's texture
    /// offset is shifted by that same world-space delta. Recentering alone would
    /// make the ground look static (the texture pattern would always sit in the
    /// same place relative to the camera, killing the sense of motion);
    /// compensating the UV offset keeps the texture anchored to world space, so it
    /// scrolls correctly under you while the mesh itself never runs out of extent.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class InfiniteGround : MonoBehaviour
    {
        [SerializeField] private BikeGameController controller;
        [Tooltip("World units one texture tile covers -- should match the ground texture's actual tiling scale.")]
        [SerializeField] private float tileWorldSize = 4f;

        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private static readonly int MainTexST = Shader.PropertyToID("_BaseMap_ST"); // URP; use "_MainTex_ST" for Built-in RP

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (controller == null) return;

            transform.position = new Vector3(controller.LateralY, transform.position.y, controller.DistanceX);

            float offsetX = controller.LateralY / tileWorldSize;
            float offsetZ = controller.DistanceX / tileWorldSize;

            _renderer.GetPropertyBlock(_block);
            _block.SetVector(MainTexST, new Vector4(1f, 1f, offsetX, offsetZ)); // (tileX, tileY, offsetX, offsetY)
            _renderer.SetPropertyBlock(_block);
        }
    }
}
