using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// Builds a flat ribbon mesh tracing SineTrack's curve, extending a fixed
    /// window ahead of (and slightly behind) the player. Rebuilt every frame from
    /// FirstPersonRig -- cheap at ~40 segments, and far simpler than trying to
    /// scroll/tile a pre-baked mesh.
    ///
    /// World convention used throughout the bike game's 3D view: X = lateral
    /// position, Y = up, Z = forward distance traveled.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoadMeshGenerator : MonoBehaviour
    {
        [SerializeField] private SineTrack track;
        [SerializeField] private float roadWidth = 2f;
        [SerializeField] private float aheadDistance = 60f;
        [SerializeField] private int segments = 70;
        [Tooltip("World units of forward distance per full tile of the road texture's V coordinate.")]
        [SerializeField] private float textureTileLength = 2f;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector2[] _uvs;
        private int[] _triangles;

        private void Awake()
        {
            if (track == null) track = FindObjectOfType<SineTrack>();

            _mesh = new Mesh { name = "RoadRibbon" };
            GetComponent<MeshFilter>().mesh = _mesh;

            int vertCount = (segments + 1) * 2;
            _vertices = new Vector3[vertCount];
            _uvs = new Vector2[vertCount];
            _triangles = new int[segments * 6];
        }

        /// <summary>Regenerates the ribbon so it spans [startDistanceZ, startDistanceZ + aheadDistance].</summary>
        public void Rebuild(float startDistanceZ)
        {
            // U is fixed per edge (not dependent on curve position) so texel
            // density matches V's world-unit tiling -- this is what was causing
            // the stretching: U used to always span 0..1 regardless of the road's
            // actual physical width, which doesn't match V's real-world scale.
            float uLeft = -roadWidth * 0.5f / textureTileLength;
            float uRight = roadWidth * 0.5f / textureTileLength;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float z = startDistanceZ + t * aheadDistance;
                float centerX = track.TargetY(z);

                int vi = i * 2;
                _vertices[vi] = new Vector3(centerX - roadWidth * 0.5f, 0f, z);
                _vertices[vi + 1] = new Vector3(centerX + roadWidth * 0.5f, 0f, z);

                float v = z / textureTileLength;
                _uvs[vi] = new Vector2(uLeft, v);
                _uvs[vi + 1] = new Vector2(uRight, v);

                if (i < segments)
                {
                    int ti = i * 6;
                    _triangles[ti + 0] = vi;
                    _triangles[ti + 1] = vi + 2;
                    _triangles[ti + 2] = vi + 1;
                    _triangles[ti + 3] = vi + 1;
                    _triangles[ti + 4] = vi + 2;
                    _triangles[ti + 5] = vi + 3;
                }
            }

            _mesh.Clear();
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.triangles = _triangles;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }
}
