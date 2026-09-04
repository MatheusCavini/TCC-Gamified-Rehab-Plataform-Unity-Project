using HalMenu.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HalMenu.Games.Bike
{
    [RequireComponent(typeof(UIDocument))]
    public class BikeHud : MonoBehaviour
    {
        [SerializeField] private BikeGameController controller;

        [Header("Minimap")]
        [Tooltip("The RenderTexture the minimap camera renders into. Create via Assets > Create > Render Texture and assign it here AND on the minimap Camera's Target Texture field.")]
        [SerializeField] private RenderTexture minimapTexture;

        private VisualElement _balanceRow;
        private VisualElement _minimapBox;
        private VisualElement _gaugeNeedle;
        private VisualElement _balanceNeedle;
        private VisualElement _headingNeedle;
        private Label _accuracyValueLabel;
        private Label _headingValueLabel;
        private Label _speedLabel;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _gaugeNeedle = root.Q<VisualElement>("gauge-needle");
            _balanceNeedle = root.Q<VisualElement>("balance-needle");
            _headingNeedle = root.Q<VisualElement>("heading-needle");
            _balanceRow = root.Q<VisualElement>("balance-row");
            _minimapBox = root.Q<VisualElement>("minimap-box");
            _accuracyValueLabel = root.Q<Label>("accuracy-value-label");
            _headingValueLabel = root.Q<Label>("heading-value-label");
            _speedLabel = root.Q<Label>("speed-label");

            root.Q<Button>("back-to-menu-btn").clicked += HandleBackToMenu;

            // Bilateral balance is only meaningful with two pressure sensors assigned;
            // hide the row entirely rather than showing a misleading single-input value.
            _balanceRow.style.display = controller.HasBilateralInputs ? DisplayStyle.Flex : DisplayStyle.None;

            if (minimapTexture != null)
                _minimapBox.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(minimapTexture));
            else
                Debug.LogWarning("[BikeHud] No minimap RenderTexture assigned -- the minimap box will render blank.");
        }

        private void Update()
        {
            if (controller == null) return;

            SetNeedle(_gaugeNeedle, controller.CurrentAccuracy01);
            _accuracyValueLabel.text = $"{controller.CurrentAccuracy01 * 100f:0}%";

            _speedLabel.text = $"{controller.CurrentSpeed:0.0}";

            if (controller.HasBilateralInputs)
                SetNeedle(_balanceNeedle, controller.CurrentBalance01);

            // Heading dial: 0 deg (straight) = needle pointing up. Clamped to +-90
            // for the visual so it doesn't spin past horizontal, even though the
            // underlying heading value itself is unbounded.
            float headingClamped = Mathf.Clamp(controller.HeadingDeg, -90f, 90f);
            _headingNeedle.style.rotate = new StyleRotate(new Rotate(new Angle(headingClamped, AngleUnit.Degree)));
            _headingValueLabel.text = $"{controller.HeadingDeg:0}°";
        }

        /// <summary>Positions a needle element along its gauge track, 0 = left/red end, 1 = right/green end.</summary>
        private static void SetNeedle(VisualElement needle, float value01)
        {
            if (needle == null) return;
            needle.style.left = new StyleLength(new Length(Mathf.Clamp01(value01) * 100f, LengthUnit.Percent));
        }

        private void HandleBackToMenu()
        {
            GameSessionService.Instance?.EndSession();
            SceneLoader.Instance.LoadMenu();
        }
    }
}
