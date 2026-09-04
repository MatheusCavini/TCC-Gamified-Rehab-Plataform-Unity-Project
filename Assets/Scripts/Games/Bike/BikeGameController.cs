using System.Collections.Generic;
using System.Linq;
using HalMenu.Core;
using Newtonsoft.Json;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;

namespace HalMenu.Games.Bike
{
    /// <summary>
    /// Proof-of-concept game for the bike handlebar rig: 1 required rotation
    /// (steering) input, 0-2 optional pressure/intensity inputs (throttle).
    ///
    /// Design intent (see conversation / SETUP notes): presence of pressure sensors
    /// changes HOW MUCH control the patient has over speed, never WHETHER the game
    /// runs. With zero intensity devices assigned, speed is constant and only
    /// steering matters -- that path must always work, since it's the therapeutic
    /// core of the exercise and the only strictly required input. With one or two
    /// assigned, their average becomes throttle. Bilateral balance (only meaningful
    /// with two) is tracked and published as a metric but never gates gameplay.
    /// </summary>
    [RequireComponent(typeof(SineTrack))]
    public class BikeGameController : MonoBehaviour
    {
        [Header("Scene refs")]
        [SerializeField] private SineTrack track;

        [Header("Steering")]
        [Tooltip("Front wheel angle (degrees) at raw calibrated encoder value 0.0. Should be negative (steer left).")]
        [SerializeField] private float steerAngleAtZero = -80f;
        [Tooltip("Front wheel angle (degrees) at raw calibrated encoder value 1.0. Should be positive (steer right).")]
        [SerializeField] private float steerAngleAtOne = 80f;
        [Tooltip("Distance between front and rear wheel contact points. Larger = wider, lazier turning radius for the same wheel angle -- this is what makes the steering feel like a bike instead of a kart.")]
        [SerializeField] private float wheelbase = 1.0f;

        [Header("Speed")]
        [Tooltip("Speed used when zero intensity devices are assigned -- the always-available fallback.")]
        [SerializeField] private float cruiseSpeed = 3.0f;
        [Tooltip("Speed floor once at least one intensity device is assigned, even at zero grip.")]
        [SerializeField] private float minActiveSpeed = 1.0f;
        [SerializeField] private float maxActiveSpeed = 6.0f;

        [Header("Scoring")]
        [Tooltip("Distance from the path center beyond which accuracy for that frame is treated as zero.")]
        [SerializeField] private float trackHalfWidth = 2.5f;
        [SerializeField] private float metricsPublishInterval = 0.5f;

        [Header("ROS Topics (match architecture diagram)")]
        [SerializeField] private string gameStateTopic = "/game/state";
        [SerializeField] private string gameMetricsTopic = "/game/performance_metrics";

        // Session-derived device assignment
        private string _positionDeviceId;
        private List<string> _intensityDeviceIds = new List<string>();
        private readonly Dictionary<string, float> _latestValues = new Dictionary<string, float>();

        // Runtime state
        private float _distanceX;
        private float _lateralY;
        private float _headingRad; // 0 = facing straight down the Z axis
        private float _accuracyAccumulator;
        private float _accuracySamples;
        private float _metricsTimer;

        public float CurrentSpeed { get; private set; }
        public float CurrentAccuracy01 { get; private set; } = 1f;
        public float CurrentBalance01 { get; private set; } = 1f;
        public bool HasBilateralInputs => _intensityDeviceIds.Count >= 2;

        /// <summary>Forward progress along the track. Maps to world Z in the 3D view.</summary>
        public float DistanceX => _distanceX;
        /// <summary>Player's lateral (left/right) world position. Maps to world X in the 3D view.</summary>
        public float LateralY => _lateralY;
        /// <summary>Current front wheel angle in degrees, linearly mapped from the raw calibrated encoder value. -80..+80 by default.</summary>
        public float SteerAngleDeg { get; private set; }
        /// <summary>Current bike heading in degrees, 0 = straight down the track. Positive = turned right.</summary>
        public float HeadingDeg => _headingRad * Mathf.Rad2Deg;

        // ---- Control-relevant error terms (see conversation: published raw, not
        // consumed by any PID math here -- that math belongs on the ROS adaptive
        // controller node, not in Unity). ----

        /// <summary>Path's lateral position (world X) at the player's current forward distance.</summary>
        public float PathTargetY { get; private set; }
        /// <summary>Signed cross-track error: LateralY - PathTargetY. Positive = player is to the right of the path.</summary>
        public float LateralError { get; private set; }
        /// <summary>Heading the path itself is pointing at the player's current forward distance (its local tangent direction), in degrees.</summary>
        public float PathHeadingDeg { get; private set; }
        /// <summary>Signed heading error: how far the bike's current heading is from the path's local tangent direction, wrapped to -180..180.</summary>
        public float HeadingErrorDeg { get; private set; }

        private void Awake()
        {
            if (track == null) track = GetComponent<SineTrack>();
        }

        private void Start()
        {
            var session = GameSessionService.Instance?.ActiveSession;
            if (session == null)
            {
                Debug.LogWarning("[BikeGameController] No active GameSession -- was this scene loaded directly instead of via the menu's Play button? Falling back to cruise-only mode.");
            }
            else
            {
                var positionIds = session.Get(CapabilityType.Position);
                _positionDeviceId = positionIds.Count > 0 ? positionIds[0] : null;
                _intensityDeviceIds = session.Get(CapabilityType.Intensity);

                if (_positionDeviceId == null)
                    Debug.LogWarning("[BikeGameController] Session has no Position device assigned -- steering will not respond.");
            }

            if (DeviceRegistryService.Instance != null)
                DeviceRegistryService.Instance.OnDeviceStateUpdated += HandleDeviceState;

            var ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<StringMsg>(gameStateTopic);
            ros.RegisterPublisher<StringMsg>(gameMetricsTopic);
        }

        private void OnDestroy()
        {
            if (DeviceRegistryService.Instance != null)
                DeviceRegistryService.Instance.OnDeviceStateUpdated -= HandleDeviceState;
        }

        private void HandleDeviceState(string deviceId, float normalizedValue, string unit)
        {
            // Only track devices this session actually assigned to us.
            if (deviceId != _positionDeviceId && !_intensityDeviceIds.Contains(deviceId)) return;
            _latestValues[deviceId] = normalizedValue;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            UpdateSpeed();
            UpdateSteering(dt);
            UpdateErrorTerms();
            UpdateScoring();
            PublishOnInterval(dt);
        }

        private void UpdateSpeed()
        {
            if (_intensityDeviceIds.Count == 0)
            {
                CurrentSpeed = cruiseSpeed;
                CurrentBalance01 = 1f; // not applicable, default to "fine"
                return;
            }

            var values = _intensityDeviceIds
                .Select(id => _latestValues.TryGetValue(id, out var v) ? v : 0f)
                .ToList();

            float avgIntensity = values.Count > 0 ? values.Average() : 0f;
            CurrentSpeed = Mathf.Lerp(minActiveSpeed, maxActiveSpeed, Mathf.Clamp01(avgIntensity));

            if (values.Count >= 2)
                CurrentBalance01 = 1f - Mathf.Clamp01(Mathf.Abs(values[0] - values[1]));
            else
                CurrentBalance01 = 1f;
        }

        private void UpdateSteering(float dt)
        {
            float raw = 0.5f; // straight-ahead default if no position device is assigned yet
            if (_positionDeviceId != null && _latestValues.TryGetValue(_positionDeviceId, out var v))
                raw = v;

            SteerAngleDeg = Mathf.Lerp(steerAngleAtZero, steerAngleAtOne, Mathf.Clamp01(raw));
            float steerAngleRad = SteerAngleDeg * Mathf.Deg2Rad;

            // Kinematic bicycle model: heading changes based on speed and wheel angle,
            // scaled by wheelbase -- this is what gives a bike-like turning radius
            // instead of the wheel angle just directly steering a velocity vector.
            _headingRad += (CurrentSpeed / wheelbase) * Mathf.Tan(steerAngleRad) * dt;

            _distanceX += CurrentSpeed * Mathf.Cos(_headingRad) * dt;
            _lateralY += CurrentSpeed * Mathf.Sin(_headingRad) * dt;
        }

        /// <summary>
        /// Computes the raw error terms the adaptive control node needs. Deliberately
        /// does no control math here (no gains, no PID) -- see the design discussion:
        /// lateral error alone is a poor signal to feed a single PID that outputs
        /// torque, since it's two integrations removed from the actuator (torque ->
        /// steering angle -> heading -> lateral position). Publishing both lateral
        /// AND heading error lets the ROS-side controller use a proper cascaded or
        /// Stanley-style scheme instead.
        /// </summary>
        private void UpdateErrorTerms()
        {
            PathTargetY = track.TargetY(_distanceX);
            LateralError = _lateralY - PathTargetY;

            float pathSlope = track.TargetSlope(_distanceX);
            PathHeadingDeg = Mathf.Atan(pathSlope) * Mathf.Rad2Deg;
            HeadingErrorDeg = Mathf.DeltaAngle(PathHeadingDeg, HeadingDeg);
        }

        private void UpdateScoring()
        {
            float frameAccuracy = 1f - Mathf.Clamp01(Mathf.Abs(LateralError) / trackHalfWidth);

            _accuracyAccumulator += frameAccuracy;
            _accuracySamples += 1f;
            CurrentAccuracy01 = _accuracySamples > 0 ? _accuracyAccumulator / _accuracySamples : 1f;
        }

        private void PublishOnInterval(float dt)
        {
            _metricsTimer += dt;

            var statePayload = new
            {
                distance = _distanceX,
                lateral_y = _lateralY,
                path_target_y = PathTargetY,
                lateral_error = LateralError,
                heading_deg = HeadingDeg,
                path_heading_deg = PathHeadingDeg,
                heading_error_deg = HeadingErrorDeg,
                steer_angle_deg = SteerAngleDeg,
                speed = CurrentSpeed
            };
            ROSConnection.GetOrCreateInstance().Publish(gameStateTopic,
                new StringMsg(JsonConvert.SerializeObject(statePayload)));

            if (_metricsTimer < metricsPublishInterval) return;
            _metricsTimer = 0f;

            var metricsPayload = new
            {
                game_id = "bike_sine_wave",
                accuracy = CurrentAccuracy01,
                bilateral_balance = HasBilateralInputs ? (float?)CurrentBalance01 : null,
                intensity_device_count = _intensityDeviceIds.Count,
                distance = _distanceX
            };
            ROSConnection.GetOrCreateInstance().Publish(gameMetricsTopic,
                new StringMsg(JsonConvert.SerializeObject(metricsPayload)));
        }
    }
}
