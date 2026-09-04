using System.Collections.Generic;
using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// Scatters prefab instances (trees, bushes -- anything with a Billboard
    /// component) along both sides of the track, offset laterally from the path
    /// centerline. Pooled and recycled: instances that fall behind the player are
    /// respawned ahead instead of being destroyed/instantiated repeatedly.
    /// </summary>
    public class TrackSceneryScatter : MonoBehaviour
    {
        [SerializeField] private BikeGameController controller;
        [SerializeField] private SineTrack track;
        [SerializeField] private GameObject[] scenaryPrefabs;

        [Header("Placement")]
        [SerializeField] private float spacing = 6f;
        [SerializeField] private Vector2 lateralOffsetRange = new Vector2(4f, 9f); // distance from path centerline, both sides
        [SerializeField] private float aheadDistance = 80f;
        [SerializeField] private float behindMargin = 15f;

        private readonly List<Transform> _pool = new List<Transform>();
        private float _nextSpawnZ;

        private void Start()
        {
            if (track == null) track = FindObjectOfType<SineTrack>();
            _nextSpawnZ = controller.DistanceX;

            // Pre-fill the pool for the initial window so scenery is visible immediately.
            int count = Mathf.CeilToInt(aheadDistance / spacing) * 2; // *2 for both sides
            for (int i = 0; i < count; i++)
                _pool.Add(SpawnOne());
        }

        private Transform SpawnOne()
        {
            if (scenaryPrefabs == null || scenaryPrefabs.Length == 0) return null;
            var prefab = scenaryPrefabs[Random.Range(0, scenaryPrefabs.Length)];
            var instance = Instantiate(prefab, transform);
            PlaceAt(instance.transform, _nextSpawnZ);
            _nextSpawnZ += spacing / 2f; // two placements (left+right) per spacing step, see PlaceAt side alternation
            return instance.transform;
        }

        private void PlaceAt(Transform t, float z)
        {
            float side = (Random.value < 0.5f) ? -1f : 1f;
            float lateralOffset = side * Random.Range(lateralOffsetRange.x, lateralOffsetRange.y);
            float centerX = track.TargetY(z);
            t.position = new Vector3(centerX + lateralOffset, 0f, z);
        }

        private void Update()
        {
            if (controller == null) return;
            float playerZ = controller.DistanceX;

            // Recycle anything that's fallen behind the player.
            foreach (var t in _pool)
            {
                if (t == null) continue;
                if (t.position.z < playerZ - behindMargin)
                {
                    PlaceAt(t, playerZ + aheadDistance);
                }
            }
        }
    }
}
