using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using RosMessageTypes.HalInterfaces;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

namespace HalMenu.Core
{
    public class GameSession
    {
        public string GameId;
        public string ControlMode;
        public string PatientId;
        public string DatabaseSessionId;
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
        public string SelectedControlMode { get; private set; } = "assisted";
        private const string PatientIdPrefsKey = "HalMenu.PrototypePatientId";
        private const string StartRecordingService = "/recording/start";
        private const string StopRecordingService = "/recording/stop";
        private bool _recording;
        private bool _recordingPending;
        private bool _stopPending;
        private bool _endRequested;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            var ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterRosService<SetStringRequest, SetStringResponse>(StartRecordingService);
            ros.RegisterRosService<TriggerRequest, TriggerResponse>(StopRecordingService);
        }

        public void SelectControlMode(string mode)
        {
            if (mode == "assisted" || mode == "resistive")
                SelectedControlMode = mode;
        }

        public bool BeginSession(GameAvailability availability)
        {
            if (availability.manifest == null ||
                DeviceRegistryService.Instance == null || !DeviceRegistryService.Instance.IsHalOnline)
                return false;
            if (_recording || _recordingPending || _stopPending) return false;

            var ros = ROSConnection.GetOrCreateInstance();
            if (ros == null || ros.HasConnectionError) return false;

            // Send the selected mode on the existing state topic before switching scenes.
            ros.RegisterPublisher<StringMsg>("/game/state");
            ros.Publish("/game/state", new StringMsg(JsonConvert.SerializeObject(new
            {
                game_id = availability.manifest.gameId,
                control_mode = SelectedControlMode
            })));

            ActiveSession = new GameSession
            {
                GameId = availability.manifest.gameId,
                DeviceIds = availability.assignedDeviceIds,
                ControlMode = SelectedControlMode,
                PatientId = GetOrCreatePrototypePatientId()
            };
            _endRequested = false;
            return true;
        }

        /// <summary>Starts persistence after calibration and when gameplay actually begins.</summary>
        public void StartRecording()
        {
            var session = ActiveSession;
            if (session == null || _recording || _recordingPending || _stopPending) return;

            _recordingPending = true;
            var payload = JsonConvert.SerializeObject(new
            {
                patient_id = session.PatientId,
                protocol = session.GameId
            });
            ROSConnection.GetOrCreateInstance().SendServiceMessage<SetStringResponse>(
                StartRecordingService, new SetStringRequest(payload), response =>
                {
                    _recordingPending = false;
                    if (response.success)
                    {
                        _recording = true;
                        session.DatabaseSessionId = response.message;
                        if (_endRequested) StopRecording();
                    }
                    else
                    {
                        _endRequested = false;
                        Debug.LogWarning($"[GameSessionService] Could not start recording: {response.message}");
                    }
                });
        }

        public void EndSession()
        {
            ActiveSession = null;
            if (_recordingPending)
            {
                _endRequested = true;
                return;
            }
            if (_recording) StopRecording();
        }

        private void StopRecording()
        {
            if (!_recording || _stopPending) return;
            _recording = false;
            _stopPending = true;
            ROSConnection.GetOrCreateInstance().SendServiceMessage<TriggerResponse>(
                StopRecordingService, new TriggerRequest(), response =>
                {
                    _stopPending = false;
                    _endRequested = false;
                    if (!response.success)
                        Debug.LogWarning($"[GameSessionService] Could not stop recording: {response.message}");
                });
        }

        private static string GetOrCreatePrototypePatientId()
        {
            var patientId = PlayerPrefs.GetString(PatientIdPrefsKey, "");
            if (!Guid.TryParse(patientId, out _))
            {
                patientId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(PatientIdPrefsKey, patientId);
                PlayerPrefs.Save();
            }
            return patientId;
        }
    }
}
