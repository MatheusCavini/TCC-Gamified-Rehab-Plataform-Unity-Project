using System.Collections.Generic;
using HalMenu.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HalMenu.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MenuController : MonoBehaviour
    {
        [Header("Row / Card Templates")]
        [SerializeField] private VisualTreeAsset deviceRowTemplate;
        [SerializeField] private VisualTreeAsset gameCardTemplate;

        private UIDocument _doc;
        private VisualElement _root;
        private VisualElement _deviceList;
        private VisualElement _gameGrid;
        private Label _statusLabel;
        private Label _profileFeedback;

        private readonly Dictionary<string, VisualElement> _deviceRows = new Dictionary<string, VisualElement>();

        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = _doc.rootVisualElement;

            _deviceList = _root.Q<VisualElement>("device-list");
            _gameGrid = _root.Q<VisualElement>("game-grid");
            _statusLabel = _root.Q<Label>("status-label");
            _profileFeedback = _root.Q<Label>("profile-feedback");

            _root.Q<Button>("save-profile-btn").clicked += HandleSaveProfile;
            _root.Q<Button>("load-profile-btn").clicked += HandleLoadProfile;

            if (DeviceRegistryService.Instance != null)
            {
                DeviceRegistryService.Instance.OnDeviceListUpdated += RebuildDeviceList;
                DeviceRegistryService.Instance.OnCalibrationStateUpdated += RefreshCalibrationLabels;
                DeviceRegistryService.Instance.OnDeviceStateUpdated += RefreshLiveBar;

                // Paint whatever state already exists in case the menu scene loads
                // after the registry already received its first messages.
                RebuildDeviceList(DeviceRegistryService.Instance.CurrentDevices);
                RefreshCalibrationLabels(DeviceRegistryService.Instance.CalibrationFlags);
            }

            if (GameCatalogService.Instance != null)
            {
                GameCatalogService.Instance.OnCatalogUpdated += RebuildGameGrid;
                RebuildGameGrid(GameCatalogService.Instance.Current);
            }
        }

        private void OnDisable()
        {
            if (DeviceRegistryService.Instance != null)
            {
                DeviceRegistryService.Instance.OnDeviceListUpdated -= RebuildDeviceList;
                DeviceRegistryService.Instance.OnCalibrationStateUpdated -= RefreshCalibrationLabels;
                DeviceRegistryService.Instance.OnDeviceStateUpdated -= RefreshLiveBar;
            }
            if (GameCatalogService.Instance != null)
                GameCatalogService.Instance.OnCatalogUpdated -= RebuildGameGrid;
        }

        private void Update()
        {
            if (_statusLabel == null || DeviceRegistryService.Instance == null) return;

            bool online = DeviceRegistryService.Instance.IsHalOnline;
            _statusLabel.text = online ? "HAL Online" : "HAL Offline";
            _statusLabel.RemoveFromClassList(online ? "status-offline" : "status-online");
            _statusLabel.AddToClassList(online ? "status-online" : "status-offline");
        }

        // ---------------------------------------------------------------
        // Devices
        // ---------------------------------------------------------------
        private void RebuildDeviceList(IReadOnlyList<DeviceInfo> devices)
        {
            _deviceList.Clear();
            _deviceRows.Clear();

            foreach (var device in devices)
            {
                var row = deviceRowTemplate.CloneTree();
                row.Q<Label>("device-name").text = device.device_id;
                row.Q<Label>("device-type").text = device.type;

                var calibrateBtn = row.Q<Button>("calibrate-btn");
                string deviceId = device.device_id; // capture for closure
                calibrateBtn.clicked += () => HandleToggleCalibration(deviceId, calibrateBtn);

                _deviceList.Add(row);
                _deviceRows[device.device_id] = row;
            }

            GameCatalogService.Instance?.Recompute();
        }

        private void RefreshCalibrationLabels(IReadOnlyDictionary<string, bool> flags)
        {
            foreach (var kvp in _deviceRows)
            {
                bool calibrating = flags.TryGetValue(kvp.Key, out var v) && v;
                var statusLabel = kvp.Value.Q<Label>("device-status");
                var calibrateBtn = kvp.Value.Q<Button>("calibrate-btn");

                statusLabel.text = calibrating ? "Calibrating..." : "Idle";
                calibrateBtn.text = calibrating ? "Stop (Lock ROM)" : "Calibrate";

                statusLabel.RemoveFromClassList(calibrating ? "status-idle" : "status-calibrating");
                statusLabel.AddToClassList(calibrating ? "status-calibrating" : "status-idle");
            }
        }

        private void RefreshLiveBar(string deviceId, float normalizedValue, string unit)
        {
            if (!_deviceRows.TryGetValue(deviceId, out var row)) return;
            var fill = row.Q<VisualElement>("device-live-bar-fill");
            fill.style.width = new StyleLength(new Length(Mathf.Clamp01(normalizedValue) * 100f, LengthUnit.Percent));
        }

        private void HandleToggleCalibration(string deviceId, Button sourceButton)
        {
            sourceButton.SetEnabled(false);
            CalibrationService.Instance.ToggleCalibration(deviceId, (success, message) =>
            {
                sourceButton.SetEnabled(true);
                if (!success) Debug.LogWarning($"[MenuController] Calibration toggle failed: {message}");
            });
        }

        // ---------------------------------------------------------------
        // Profile
        // ---------------------------------------------------------------
        private void HandleSaveProfile()
        {
            _profileFeedback.text = "Saving...";
            CalibrationService.Instance.SaveProfile((success, message) => _profileFeedback.text = message);
        }

        private void HandleLoadProfile()
        {
            _profileFeedback.text = "Loading...";
            CalibrationService.Instance.LoadProfile((success, message) =>
            {
                _profileFeedback.text = message;
                GameCatalogService.Instance?.Recompute();
            });
        }

        // ---------------------------------------------------------------
        // Games
        // ---------------------------------------------------------------
        private void RebuildGameGrid(List<GameAvailability> availability)
        {
            _gameGrid.Clear();
            foreach (var entry in availability)
            {
                var card = gameCardTemplate.CloneTree();
                card.Q<Label>("game-title").text = entry.manifest.displayName;
                card.Q<Label>("game-description").text = entry.manifest.description;
                card.Q<Label>("game-reason").text = entry.reason;

                var icon = card.Q<VisualElement>("game-icon");
                if (entry.manifest.icon != null)
                    icon.style.backgroundImage = new StyleBackground(entry.manifest.icon);

                var playBtn = card.Q<Button>("play-btn");
                playBtn.SetEnabled(entry.isPlayable);
                var gameEntry = entry; // capture for closure
                playBtn.clicked += () =>
                {
                    GameSessionService.Instance.BeginSession(gameEntry);
                    SceneLoader.Instance.LoadGame(gameEntry.manifest.sceneName);
                };

                card.EnableInClassList("game-card-disabled", !entry.isPlayable);
                _gameGrid.Add(card);
            }
        }
    }
}
