using System.Collections.Generic;
using UnityEngine;

namespace HalMenu.Core
{
    public class GameSession
    {
        public string GameId;
        public Dictionary<CapabilityType, List<string>> DeviceIds;

        public List<string> Get(CapabilityType type) =>
            DeviceIds != null && DeviceIds.TryGetValue(type, out var list) ? list : new List<string>();
    }

    /// <summary>
    /// Lives on the persistent Bootstrap scene. The menu calls BeginSession() at the
    /// moment "Play" is pressed, snapshotting which specific device_ids fill each
    /// capability slot for that game. Game scenes read ActiveSession instead of
    /// talking to GameCatalogService directly -- this matters because the catalog
    /// keeps recomputing live as devices connect/disconnect, and a game that's
    /// already running shouldn't have its device assignment silently change under it
    /// mid-session just because, e.g., a second EMG sensor was plugged in.
    /// </summary>
    public class GameSessionService : MonoBehaviour
    {
        public static GameSessionService Instance { get; private set; }

        public GameSession ActiveSession { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void BeginSession(GameAvailability availability)
        {
            ActiveSession = new GameSession
            {
                GameId = availability.manifest.gameId,
                DeviceIds = availability.assignedDeviceIds
            };
        }

        public void EndSession() => ActiveSession = null;
    }
}
