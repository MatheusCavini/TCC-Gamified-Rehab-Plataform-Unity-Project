using HalMenu.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HalMenu.Games.Pulmonary
{
    /// <summary>
    /// UI Toolkit front-end of the balloon breathing exercise: therapist setup panel,
    /// countdown, the balloon itself (player + guide outline), phase timer, effort
    /// meter and results. All game logic lives in PulmonaryGameController.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PulmonaryHud : MonoBehaviour
    {
        [SerializeField] private PulmonaryGameController controller;

        [Header("Optional art (leave empty to use the built-in vector balloon)")]
        [Tooltip("Balloon image. Use a white/greyscale sprite: it is tinted green / orange / blue by the game.")]
        [SerializeField] private Sprite balloonSprite;
        [Tooltip("Full-screen background image drawn behind everything.")]
        [SerializeField] private Sprite backgroundSprite;

        [Header("Balloon")]
        [Tooltip("Balloon scale at zero effort.")]
        [Range(0.1f, 1f)] [SerializeField] private float minScale = 0.3f;
        [Tooltip("Balloon scale at full effort.")]
        [Range(0.5f, 1.5f)] [SerializeField] private float maxScale = 1.0f;
        [SerializeField] private Color idleColor = new Color(0.36f, 0.60f, 0.95f);
        [SerializeField] private Color onTargetColor = new Color(0.30f, 0.78f, 0.45f);
        [SerializeField] private Color offTargetColor = new Color(0.95f, 0.55f, 0.28f);

        private bool _bound;
        private PulmonaryState? _shownState;
        private Color _lastColor = Color.clear;
        private bool _startEnabled = true;

        private VisualElement _root, _balloon, _knot, _ghost, _phaseBlock, _effortBand, _effortFill, _progressFill;
        private VisualElement _setupOverlay, _countdownOverlay, _resultsOverlay;
        private Label _signalLabel, _phaseLabel, _phaseHintLabel, _phaseTimerLabel, _cycleLabel, _accuracyLabel;
        private Label _countdownLabel, _setupSummary, _setupStatus;
        private Label _resultCycles, _resultAccuracy, _resultInBand;
        private Button _startBtn, _stopBtn;

        // Bound in Start (not OnEnable) so the UIDocument tree and the controller's
        // Awake-initialised Config are guaranteed to exist.
        private void Start()
        {
            if (controller == null)
            {
                Debug.LogError("[PulmonaryHud] No PulmonaryGameController assigned.");
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;

            _root = root.Q<VisualElement>("pulm-root");
            _balloon = root.Q<VisualElement>("balloon");
            _knot = root.Q<VisualElement>("balloon-knot");
            _ghost = root.Q<VisualElement>("ghost-balloon");
            _phaseBlock = root.Q<VisualElement>("phase-block");
            _effortBand = root.Q<VisualElement>("effort-band");
            _effortFill = root.Q<VisualElement>("effort-fill");
            _progressFill = root.Q<VisualElement>("phase-progress-fill");
            _setupOverlay = root.Q<VisualElement>("setup-overlay");
            _countdownOverlay = root.Q<VisualElement>("countdown-overlay");
            _resultsOverlay = root.Q<VisualElement>("results-overlay");

            _signalLabel = root.Q<Label>("signal-label");
            _phaseLabel = root.Q<Label>("phase-label");
            _phaseHintLabel = root.Q<Label>("phase-hint-label");
            _phaseTimerLabel = root.Q<Label>("phase-timer-label");
            _cycleLabel = root.Q<Label>("cycle-label");
            _accuracyLabel = root.Q<Label>("accuracy-label");
            _countdownLabel = root.Q<Label>("countdown-label");
            _setupSummary = root.Q<Label>("setup-summary-label");
            _setupStatus = root.Q<Label>("setup-status-label");
            _resultCycles = root.Q<Label>("result-cycles-label");
            _resultAccuracy = root.Q<Label>("result-accuracy-label");
            _resultInBand = root.Q<Label>("result-inband-label");

            _startBtn = root.Q<Button>("start-btn");
            _stopBtn = root.Q<Button>("stop-btn");

            _startBtn.clicked += controller.StartSession;
            _stopBtn.clicked += controller.ReturnToSetup;
            root.Q<Button>("retry-btn").clicked += controller.StartSession;
            root.Q<Button>("adjust-btn").clicked += controller.ReturnToSetup;
            root.Q<Button>("back-to-menu-btn").clicked += HandleBackToMenu;
            root.Q<Button>("results-menu-btn").clicked += HandleBackToMenu;

            ApplyOptionalArt();
            BindSettings(root);
            RefreshSetupSummary();

            _bound = true;
        }

        private void ApplyOptionalArt()
        {
            if (balloonSprite != null)
            {
                _balloon.style.backgroundImage = new StyleBackground(balloonSprite);
                _balloon.style.backgroundColor = new StyleColor(Color.clear);
                _balloon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                _knot.style.display = DisplayStyle.None; // the sprite brings its own knot
            }

            if (backgroundSprite != null)
            {
                _root.style.backgroundImage = new StyleBackground(backgroundSprite);
                _root.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            }
        }

        // ---------------------------------------------------------------
        // Therapist settings
        // ---------------------------------------------------------------
        private void BindSettings(VisualElement root)
        {
            var config = controller.Config;

            var inflate = root.Q<Slider>("inflate-slider");
            inflate.SetValueWithoutNotify(config.inflateSeconds);
            inflate.RegisterValueChangedCallback(evt =>
            {
                float seconds = Mathf.Round(evt.newValue * 2f) / 2f; // 0.5 s steps
                inflate.SetValueWithoutNotify(seconds);
                controller.Config.inflateSeconds = seconds;
                RefreshSetupSummary();
            });

            var deflate = root.Q<Slider>("deflate-slider");
            deflate.SetValueWithoutNotify(config.deflateSeconds);
            deflate.RegisterValueChangedCallback(evt =>
            {
                float seconds = Mathf.Round(evt.newValue * 2f) / 2f;
                deflate.SetValueWithoutNotify(seconds);
                controller.Config.deflateSeconds = seconds;
                RefreshSetupSummary();
            });

            var cycles = root.Q<SliderInt>("cycles-slider");
            cycles.SetValueWithoutNotify(config.cycles);
            cycles.RegisterValueChangedCallback(evt =>
            {
                controller.Config.cycles = evt.newValue;
                RefreshSetupSummary();
            });

            var target = root.Q<SliderInt>("target-slider");
            target.SetValueWithoutNotify(Mathf.RoundToInt(config.inflateTarget * 100f));
            target.RegisterValueChangedCallback(evt =>
            {
                controller.Config.inflateTarget = evt.newValue / 100f;
                RefreshSetupSummary();
            });
        }

        private void RefreshSetupSummary()
        {
            var c = controller.Config;
            float total = c.cycles * (c.inflateSeconds + c.deflateSeconds);
            _setupSummary.text =
                $"{c.cycles} x ({c.inflateSeconds:0.#} s inflate + {c.deflateSeconds:0.#} s deflate) = {total:0.#} s";
        }

        // ---------------------------------------------------------------
        // Frame update
        // ---------------------------------------------------------------
        private void Update()
        {
            if (!_bound) return;

            if (_shownState != controller.State) ShowState(controller.State);

            UpdateBalloon();
            UpdateEffortMeter();
            UpdateTexts();
        }

        private void ShowState(PulmonaryState state)
        {
            _shownState = state;
            bool playing = state == PulmonaryState.Countdown || state == PulmonaryState.Running;

            SetVisible(_setupOverlay, state == PulmonaryState.Setup);
            SetVisible(_countdownOverlay, state == PulmonaryState.Countdown);
            SetVisible(_resultsOverlay, state == PulmonaryState.Finished);
            SetVisible(_stopBtn, playing);
            SetVisible(_ghost, playing);
            SetVisible(_effortBand, playing);
            SetVisible(_phaseBlock, playing);

            if (state == PulmonaryState.Finished)
            {
                var c = controller.Config;
                _resultCycles.text =
                    $"{c.cycles} cycles completed ({c.inflateSeconds:0.#} s inflate / {c.deflateSeconds:0.#} s deflate)";
                _resultAccuracy.text = $"{controller.Accuracy01 * 100f:0}%";
                _resultInBand.text = $"On target {controller.InBandFraction * 100f:0}% of the time";
            }
        }

        private void UpdateBalloon()
        {
            SetScale(_balloon, Mathf.Lerp(minScale, maxScale, controller.Level01));

            bool playing = controller.State == PulmonaryState.Countdown || controller.State == PulmonaryState.Running;
            if (playing) SetScale(_ghost, Mathf.Lerp(minScale, maxScale, controller.Target01));

            Color color = controller.State == PulmonaryState.Running
                ? (controller.InBand ? onTargetColor : offTargetColor)
                : idleColor;
            ApplyBalloonColor(color);
        }

        private void ApplyBalloonColor(Color color)
        {
            if (color == _lastColor) return;
            _lastColor = color;

            if (balloonSprite != null)
            {
                _balloon.style.unityBackgroundImageTintColor = color;
            }
            else
            {
                _balloon.style.backgroundColor = color;
                _knot.style.backgroundColor = color;
            }
        }

        private void UpdateEffortMeter()
        {
            _effortFill.style.height = Percent(controller.Level01 * 100f);

            if (controller.State == PulmonaryState.Countdown || controller.State == PulmonaryState.Running)
            {
                float low = Mathf.Clamp01(controller.Target01 - controller.Tolerance);
                float high = Mathf.Clamp01(controller.Target01 + controller.Tolerance);
                _effortBand.style.bottom = Percent(low * 100f);
                _effortBand.style.height = Percent((high - low) * 100f);
            }
        }

        private void UpdateTexts()
        {
            _signalLabel.text = controller.StatusMessage;

            switch (controller.State)
            {
                case PulmonaryState.Setup:
                    _setupStatus.text = controller.StatusMessage;
                    bool canStart = controller.HasDevice || controller.UsingKeyboardDebug;
                    if (canStart != _startEnabled)
                    {
                        _startEnabled = canStart;
                        _startBtn.SetEnabled(canStart);
                    }
                    break;

                case PulmonaryState.Countdown:
                    _countdownLabel.text = Mathf.CeilToInt(controller.CountdownRemaining).ToString();
                    _phaseLabel.text = "GET READY";
                    _phaseHintLabel.text = string.Empty;
                    _phaseTimerLabel.text = string.Empty;
                    _cycleLabel.text = string.Empty;
                    _progressFill.style.width = Percent(0f);
                    break;

                case PulmonaryState.Running:
                    bool inflate = controller.Phase == BreathPhase.Inflate;
                    _phaseLabel.text = inflate ? "BLOW" : "RELAX";
                    _phaseHintLabel.text = inflate
                        ? "Blow into the device to inflate the balloon"
                        : "Relax and let the balloon deflate";
                    _phaseTimerLabel.text = $"{controller.PhaseRemaining:0.0} s";
                    _cycleLabel.text = $"Cycle {controller.CycleNumber} / {controller.Config.cycles}";
                    _progressFill.style.width = Percent(controller.PhaseElapsed / controller.PhaseDuration * 100f);
                    _accuracyLabel.text = $"{controller.Accuracy01 * 100f:0}%";
                    break;
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetScale(VisualElement element, float scale)
        {
            element.style.scale = new StyleScale(new Scale(new Vector3(scale, scale, 1f)));
        }

        private static StyleLength Percent(float value)
        {
            return new StyleLength(new Length(Mathf.Clamp(value, 0f, 100f), LengthUnit.Percent));
        }

        private void HandleBackToMenu()
        {
            GameSessionService.Instance?.EndSession();
            if (SceneLoader.Instance != null) SceneLoader.Instance.LoadMenu();
            else Debug.LogWarning("[PulmonaryHud] No SceneLoader (Bootstrap scene not loaded) -- cannot return to the menu.");
        }
    }
}
