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
        private readonly Dictionary<string, List<ChannelPreview>> _channelPreviews = new Dictionary<string, List<ChannelPreview>>();

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
                DeviceRegistryService.Instance.OnChannelStateUpdated += RefreshChannelBars;

                // Paint whatever state already exists in case the menu scene loads
                // after the registry already received its first messages.
                RebuildDeviceList(DeviceRegistryService.Instance.CurrentDevices);
                RefreshCalibrationLabels(DeviceRegistryService.Instance.CalibrationFlags);
                foreach (var device in DeviceRegistryService.Instance.CurrentDevices)
                    if (DeviceRegistryService.Instance.TryGetLatestChannelState(device.device_id, out var cached)) RefreshChannelBars(cached);
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
                DeviceRegistryService.Instance.OnChannelStateUpdated -= RefreshChannelBars;
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
            _channelPreviews.Clear();

            foreach (var device in devices)
            {
                var row = deviceRowTemplate.CloneTree();
                row.Q<Label>("device-name").text = device.device_id;
                row.Q<Label>("device-type").text = device.type;

                var channelList = row.Q<VisualElement>("channel-list");
                var ids = device.channel_ids ?? new List<string>();
                var names = device.channel_names ?? new List<string>();
                DeviceChannelState cachedState = null;
                DeviceRegistryService.Instance?.TryGetLatestChannelState(device.device_id, out cachedState);
                if (ids.Count == 0 && cachedState != null) ids = cachedState.ChannelIds;
                bool isMultiChannelEmg = string.Equals(device.type, "emg", System.StringComparison.OrdinalIgnoreCase) && ids != null && ids.Count > 0;
                if (isMultiChannelEmg)
                {
                    row.Q<VisualElement>("device-live-bar-bg").style.display = DisplayStyle.None;
                    row.Q<Label>("device-status").style.display = DisplayStyle.None;
                    row.Q<Button>("calibrate-btn").style.display = DisplayStyle.None;
                    var previews = new List<ChannelPreview>();
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var channelId = ids[i];
                        if (string.IsNullOrEmpty(channelId)) continue;
                        string label = i < names.Count && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : channelId;
                        var preview = CreateChannelPreview(channelId, label, device.device_id, i);
                        channelList.Add(preview.Root);
                        previews.Add(preview);
                    }
                    _channelPreviews[device.device_id] = previews;
                }
                else channelList.style.display = DisplayStyle.None;

                var calibrateBtn = row.Q<Button>("calibrate-btn");
                string deviceId = device.device_id; // capture for closure
                calibrateBtn.clicked += () => HandleToggleCalibration(deviceId, calibrateBtn);

                _deviceList.Add(row);
                _deviceRows[device.device_id] = row;
            }

            if (DeviceRegistryService.Instance != null)
                RefreshCalibrationLabels(DeviceRegistryService.Instance.CalibrationFlags);
            GameCatalogService.Instance?.Recompute();
        }

        private void RefreshCalibrationLabels(IReadOnlyDictionary<string, bool> flags)
        {
            foreach (var kvp in _deviceRows)
            {
                bool calibrating = flags.TryGetValue(kvp.Key, out var v) && v;
                var statusLabel = kvp.Value.Q<Label>("device-status");
                var calibrateBtn = kvp.Value.Q<Button>("calibrate-btn");
                SetCalibrationVisual(statusLabel, calibrateBtn, calibrating);
            }
            foreach (var pair in _channelPreviews)
            foreach (var preview in pair.Value)
                SetCalibrationVisual(preview.Status, preview.Button,
                    flags.TryGetValue(pair.Key + "::" + preview.ChannelId, out var active) && active);
        }

        private static void SetCalibrationVisual(Label statusLabel, Button calibrateBtn, bool calibrating)
        {
            statusLabel.text = calibrating ? "Calibrating..." : "Idle";
            calibrateBtn.text = calibrating ? "Stop (Lock ROM)" : "Calibrate";
            statusLabel.RemoveFromClassList(calibrating ? "status-idle" : "status-calibrating");
            statusLabel.AddToClassList(calibrating ? "status-calibrating" : "status-idle");
        }

        private static ChannelPreview CreateChannelPreview(string channelId, string label, string deviceId, int channelIndex)
        {
            var root = new VisualElement();
            root.AddToClassList("channel-preview");
            var name = new Label(label);
            name.AddToClassList("channel-name");
            root.Add(name);
            var background = new VisualElement();
            background.AddToClassList("live-bar-bg");
            var fill = new VisualElement();
            fill.AddToClassList("live-bar-fill");
            background.Add(fill);
            root.Add(background);
            var status = new Label("Idle");
            status.AddToClassList("device-status");
            status.AddToClassList("status-idle");
            root.Add(status);
            var button = new Button { text = "Calibrate" };
            button.AddToClassList("secondary-btn");
            button.AddToClassList("small-btn");
            button.clicked += () => MenuController.ToggleChannelCalibration(deviceId, channelId, button);
            root.Add(button);
            return new ChannelPreview(channelId, channelIndex, root, fill, status, button);
        }

        private static void ToggleChannelCalibration(string deviceId, string channelId, Button button)
        {
            button.SetEnabled(false);
            CalibrationService.Instance.ToggleCalibration(deviceId, channelId, (success, message) =>
            {
                button.SetEnabled(true);
                if (!success) Debug.LogWarning($"[MenuController] Calibration toggle failed: {message}");
            });
        }

        private void RefreshChannelBars(DeviceChannelState state)
        {
            if (state == null) return;
            if (!_channelPreviews.TryGetValue(state.DeviceId, out var previews) || previews.Count == 0)
            {
                if (state.ChannelIds != null && state.ChannelIds.Count > 0 && DeviceRegistryService.Instance != null)
                    RebuildDeviceList(DeviceRegistryService.Instance.CurrentDevices);
                if (!_channelPreviews.TryGetValue(state.DeviceId, out previews)) return;
            }
            for (int i = 0; i < previews.Count; i++)
            {
                int valueIndex = previews[i].ChannelIndex;
                int idIndex = state.ChannelIds != null ? state.ChannelIds.IndexOf(previews[i].ChannelId) : -1;
                if (idIndex >= 0) valueIndex = idIndex;
                if (state.Values == null || valueIndex < 0 || valueIndex >= state.Values.Length) continue;
                previews[i].Fill.style.width = new StyleLength(new Length(Mathf.Clamp01(state.Values[valueIndex]) * 100f, LengthUnit.Percent));
            }
        }

        private sealed class ChannelPreview
        {
            public readonly string ChannelId;
            public readonly int ChannelIndex;
            public readonly VisualElement Root, Fill;
            public readonly Label Status;
            public readonly Button Button;
            public ChannelPreview(string channelId, int channelIndex, VisualElement root, VisualElement fill, Label status, Button button)
            { ChannelId = channelId; ChannelIndex = channelIndex; Root = root; Fill = fill; Status = status; Button = button; }
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
