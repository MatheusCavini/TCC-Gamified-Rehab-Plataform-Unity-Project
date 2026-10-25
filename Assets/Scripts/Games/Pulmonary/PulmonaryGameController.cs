using HalMenu.Core;
using Newtonsoft.Json;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace HalMenu.Games.Pulmonary
{
    public enum PulmonaryState { Setup, Countdown, Running, Finished }
    public enum BreathPhase { Inflate, Deflate }

    /// <summary>
    /// Balloon breathing exercise for pulmonary rehab: one required Respiratory
    /// input (a single normalised 0-1 effort value). The balloon inflates with the
    /// patient's effort; a guide balloon shows the effort the timed pattern asks for
    /// at every moment -- INFLATE (blow) for N seconds, then DEFLATE (relax) for M
    /// seconds, repeated for a number of cycles.
    ///
    /// Both durations are set by the therapist in the setup panel (PulmonaryHud).
    /// The game never gates anything on the score; accuracy is only reported
    /// (HUD + /game/performance_metrics), like the Bike game.
    /// </summary>
    public class PulmonaryGameController : MonoBehaviour
    {
        public const string GameId = "pulmonary_balloon";

        [Header("Defaults (the therapist's last setup is remembered and overrides these)")]
        [SerializeField] private PulmonaryConfig defaultConfig = new PulmonaryConfig();

        [Header("Guide / scoring")]
        [Tooltip("Fraction of each phase the guide takes to travel to its target level. 1 = ramps across the whole phase, small = reaches the target quickly and holds it.")]
        [Range(0.1f, 1f)] [SerializeField] private float guideRampFraction = 0.6f;
        [Tooltip("Effort level the guide returns to during DEFLATE (0 = fully relaxed).")]
        [Range(0f, 0.4f)] [SerializeField] private float deflateTarget = 0.1f;
        [Tooltip("Allowed distance between the patient's effort and the guide for the frame to count as 'on target'.")]
        [Range(0.05f, 0.5f)] [SerializeField] private float tolerance = 0.2f;
        [SerializeField] private float countdownSeconds = 3f;

        [Header("Input")]
        [Tooltip("Low-pass time constant applied to the device value so the balloon moves smoothly.")]
        [SerializeField] private float inputSmoothingSeconds = 0.15f;
        [Tooltip("If no sample arrives for this long the signal is considered lost.")]
        [SerializeField] private float signalTimeoutSeconds = 1.0f;
        [Tooltip("Freeze the exercise clock while the signal is lost, instead of scoring the patient for a device fault.")]
        [SerializeField] private bool pauseOnSignalLoss = true;
        [Tooltip("Editor / development builds only, and only when no device is assigned: hold SPACE to simulate blowing.")]
        [SerializeField] private bool enableKeyboardDebug = true;

        [Header("ROS Topics (match architecture diagram)")]
        [SerializeField] private string gameStateTopic = "/game/state";
        [SerializeField] private string gameMetricsTopic = "/game/performance_metrics";
        [SerializeField] private float statePublishInterval = 0.1f;
        [SerializeField] private float metricsPublishInterval = 0.5f;

        // ---- Read by the HUD ----
        public PulmonaryConfig Config { get; private set; }
        public PulmonaryState State { get; private set; } = PulmonaryState.Setup;
        public BreathPhase Phase { get; private set; } = BreathPhase.Inflate;
        public string DeviceId { get; private set; }
        public bool HasDevice => !string.IsNullOrEmpty(DeviceId);
        public bool HasSignal { get; private set; }

        /// <summary>Smoothed patient effort, 0-1. Drives the balloon size.</summary>
        public float Level01 { get; private set; }
        /// <summary>Effort the pattern asks for right now, 0-1. Drives the guide balloon.</summary>
        public float Target01 { get; private set; }
        public float Tolerance => tolerance;
        public bool InBand { get; private set; }

        public int CycleNumber => Mathf.Min(_cycleIndex + 1, Config.cycles); // 1-based, for display
        public float PhaseElapsed { get; private set; }
        public float PhaseDuration => Mathf.Max(0.1f, Phase == BreathPhase.Inflate ? Config.inflateSeconds : Config.deflateSeconds);
        public float PhaseRemaining => Mathf.Max(0f, PhaseDuration - PhaseElapsed);
        public float CountdownRemaining { get; private set; }

        /// <summary>Time-weighted closeness to the guide over the whole run, 0-1.</summary>
        public float Accuracy01 { get; private set; } = 1f;
        /// <summary>Fraction of the run time spent inside the tolerance band, 0-1.</summary>
        public float InBandFraction { get; private set; } = 1f;
        public float ElapsedRunSeconds => _timeSum;

        public bool UsingKeyboardDebug
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return enableKeyboardDebug && !HasDevice;
#else
                return false;
#endif
            }
        }

        public string StatusMessage
        {
            get
            {
                if (UsingKeyboardDebug) return "DEBUG: hold SPACE to blow";
                if (!HasDevice) return "No pulmonary device assigned";
                if (!HasSignal) return "No signal from device";
                return "Device connected";
            }
        }

        // ---- Internals ----
        private int _cycleIndex;
        private float _rawInput;
        private float _lastSampleTime = -999f;
        private float _accSum;
        private float _timeSum;
        private float _inBandTime;
        private float _stateTimer;
        private float _metricsTimer;
        private bool _forceMetrics;

        private void Awake()
        {
            // Awake (not Start) so the HUD can read Config when it binds its sliders.
            Config = PulmonaryConfig.LoadOrDefault(defaultConfig);
            Target01 = deflateTarget;
        }

        private void Start()
        {
            ResolveDevice();

            if (DeviceRegistryService.Instance != null)
                DeviceRegistryService.Instance.OnChannelStateUpdated += HandleChannelState;

            var ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<StringMsg>(gameStateTopic);
            ros.RegisterPublisher<StringMsg>(gameMetricsTopic);
        }

        private void OnDestroy()
        {
            if (DeviceRegistryService.Instance != null)
                DeviceRegistryService.Instance.OnChannelStateUpdated -= HandleChannelState;
        }

        private void ResolveDevice()
        {
            var session = GameSessionService.Instance?.ActiveSession;
            if (session != null)
            {
                var ids = session.Get(CapabilityType.Respiratory);
                if (ids.Count > 0)
                {
                    DeviceId = ids[0];
                    return;
                }
                Debug.LogWarning("[PulmonaryGameController] Session has no Respiratory device assigned.");
            }
            else
            {
                Debug.LogWarning("[PulmonaryGameController] No active GameSession -- was this scene loaded directly instead of via the menu's Play button? Looking for a 'pulmonary' device in the registry.");
            }

            // Fallback for a scene opened directly in the editor.
            var registry = DeviceRegistryService.Instance;
            if (registry == null) return;
            foreach (var device in registry.CurrentDevices)
            {
                if (device.type == "pulmonary")
                {
                    DeviceId = device.device_id;
                    return;
                }
            }
        }

        // Called on the main thread by ROSConnection, like every other subscriber here.
        private void HandleChannelState(DeviceChannelState state)
        {
            if (state == null || !HasDevice || state.DeviceId != DeviceId || state.Values.Length == 0) return;
            _rawInput = Mathf.Clamp01(state.Values[0]);
            _lastSampleTime = Time.realtimeSinceStartup;
        }

        // ---------------------------------------------------------------
        // Commands from the HUD
        // ---------------------------------------------------------------
        public void SaveConfig()
        {
            Config.Sanitized().Save();
        }

        public void StartSession()
        {
            SaveConfig();
            _cycleIndex = 0;
            Phase = BreathPhase.Inflate;
            PhaseElapsed = 0f;
            _accSum = 0f;
            _timeSum = 0f;
            _inBandTime = 0f;
            Accuracy01 = 1f;
            InBandFraction = 1f;
            InBand = false;
            Target01 = deflateTarget;
            CountdownRemaining = Mathf.Max(0f, countdownSeconds);
            State = CountdownRemaining > 0f ? PulmonaryState.Countdown : PulmonaryState.Running;
            GameSessionService.Instance?.StartRecording();
        }

        public void ReturnToSetup()
        {
            State = PulmonaryState.Setup;
            Target01 = deflateTarget;
            InBand = false;
        }

        // ---------------------------------------------------------------
        // Frame loop
        // ---------------------------------------------------------------
        private void Update()
        {
            float dt = Time.deltaTime;

            UpdateInput(dt);

            if (State == PulmonaryState.Countdown) TickCountdown(dt);
            else if (State == PulmonaryState.Running) TickRun(dt);

            PublishOnInterval(dt);
        }

        private void UpdateInput(float dt)
        {
            float raw;
            if (UsingKeyboardDebug)
            {
                raw = ReadKeyboardDebug();
                HasSignal = true;
            }
            else
            {
                HasSignal = HasDevice && Time.realtimeSinceStartup - _lastSampleTime < signalTimeoutSeconds;
                raw = HasSignal ? _rawInput : 0f; // no signal -> balloon deflates, never stays stuck inflated
            }

            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, inputSmoothingSeconds));
            Level01 += (raw - Level01) * k;
        }

        private static float ReadKeyboardDebug()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.isPressed) return 0.9f;
#endif
            return 0f;
        }

        private void TickCountdown(float dt)
        {
            CountdownRemaining -= dt;
            if (CountdownRemaining <= 0f)
            {
                CountdownRemaining = 0f;
                State = PulmonaryState.Running;
            }
        }

        private void TickRun(float dt)
        {
            if (pauseOnSignalLoss && !HasSignal) return;

            PhaseElapsed += dt;
            float t01 = Mathf.Clamp01(PhaseElapsed / PhaseDuration);
            Target01 = ComputeTarget(Phase, t01);

            float error = Mathf.Abs(Level01 - Target01);
            InBand = error <= tolerance;

            _accSum += (1f - Mathf.Clamp01(error / tolerance)) * dt;
            _timeSum += dt;
            if (InBand) _inBandTime += dt;
            Accuracy01 = _timeSum > 0f ? _accSum / _timeSum : 1f;
            InBandFraction = _timeSum > 0f ? _inBandTime / _timeSum : 1f;

            if (PhaseElapsed >= PhaseDuration) AdvancePhase();
        }

        /// <summary>
        /// Guide level at a given point of a phase. It moves from where the previous
        /// phase ended to this phase's goal along a smooth curve that takes
        /// guideRampFraction of the phase, then holds the goal.
        /// </summary>
        private float ComputeTarget(BreathPhase phase, float phaseProgress01)
        {
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseProgress01 / guideRampFraction));
            return phase == BreathPhase.Inflate
                ? Mathf.Lerp(deflateTarget, Config.inflateTarget, ramp)
                : Mathf.Lerp(Config.inflateTarget, deflateTarget, ramp);
        }

        private void AdvancePhase()
        {
            float overflow = Mathf.Max(0f, PhaseElapsed - PhaseDuration);

            if (Phase == BreathPhase.Inflate)
            {
                Phase = BreathPhase.Deflate;
            }
            else
            {
                _cycleIndex++;
                if (_cycleIndex >= Config.cycles)
                {
                    Finish();
                    return;
                }
                Phase = BreathPhase.Inflate;
            }

            PhaseElapsed = overflow;
        }

        private void Finish()
        {
            State = PulmonaryState.Finished;
            PhaseElapsed = 0f;
            Target01 = deflateTarget;
            InBand = false;
            _forceMetrics = true; // make sure the final result reaches the ROS side
        }

        // ---------------------------------------------------------------
        // ROS publishing (same topics / JSON style as the Bike game)
        // ---------------------------------------------------------------
        private void PublishOnInterval(float dt)
        {
            _stateTimer += dt;
            _metricsTimer += dt;
            var ros = ROSConnection.GetOrCreateInstance();

            if (_stateTimer >= statePublishInterval)
            {
                _stateTimer = 0f;
                var statePayload = new
                {
                    game_id = GameId,
                    state = State.ToString().ToLowerInvariant(),
                    phase = Phase.ToString().ToLowerInvariant(),
                    cycle = CycleNumber,
                    total_cycles = Config.cycles,
                    phase_elapsed = PhaseElapsed,
                    phase_duration = PhaseDuration,
                    level = Level01,
                    target = Target01,
                    in_band = InBand,
                    control_mode = GameSessionService.Instance?.ActiveSession?.ControlMode ?? "assisted"
                };
                ros.Publish(gameStateTopic, new StringMsg(JsonConvert.SerializeObject(statePayload)));
            }

            if (State == PulmonaryState.Setup) return;
            if (_metricsTimer < metricsPublishInterval && !_forceMetrics) return;
            _metricsTimer = 0f;
            _forceMetrics = false;

            var metricsPayload = new
            {
                game_id = GameId,
                accuracy = Accuracy01,
                in_band_fraction = InBandFraction,
                cycle = CycleNumber,
                total_cycles = Config.cycles,
                inflate_seconds = Config.inflateSeconds,
                deflate_seconds = Config.deflateSeconds,
                inflate_target = Config.inflateTarget,
                finished = State == PulmonaryState.Finished,
                run_seconds = _timeSum
            };
            ros.Publish(gameMetricsTopic, new StringMsg(JsonConvert.SerializeObject(metricsPayload)));
        }
    }
}
