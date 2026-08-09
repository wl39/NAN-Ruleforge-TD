using System;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Compact screen-space actions that follow the selected tower without
    /// obscuring its world-space attack range. It prefers the tower's right
    /// side and moves to the left when the safe area has insufficient room.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneTowerActionView : MonoBehaviour
    {
        private const float PanelWidth = 204f;
        private const float PanelHeight = 136f;
        private const float PanelGap = 14f;
        private const float EdgeMargin = 12f;

        private static readonly Color PanelColor =
            Color.white;
        private static readonly Color UpgradeColor =
            new Color32(219, 126, 40, 255);
        private static readonly Color CardsColor =
            new Color32(72, 96, 51, 255);
        private static readonly Color DisabledColor =
            new Color32(83, 75, 63, 230);
        private static readonly Color TextColor =
            new Color32(255, 244, 215, 255);
        private static readonly Color CostColor =
            new Color32(255, 218, 112, 255);
        private static readonly Color UnaffordableCostColor =
            new Color32(205, 184, 151, 255);

        private StageOneUiTextCatalog catalog;
        private Font font;
        private Canvas canvas;
        private RectTransform safeArea;
        private RectTransform panelRoot;
        private CanvasGroup panelCanvasGroup;
        private Button upgradeButton;
        private Button cardsButton;
        private Text upgradeLabel;
        private Text upgradeCostLabel;
        private Text cardsLabel;
        private TowerSelectionView target;
        private Camera worldCamera;
        private bool visible;
        private bool built;
        private bool placedOnLeft;
        private int currentUpgradeCost;
        private bool currentUpgradeIsMaximum;
        private bool currentUpgradeCostVisible;
        private bool currentUpgradeCanAfford;

        public event Action UpgradeRequested;
        public event Action CardsRequested;

        public Canvas Canvas => canvas;
        public RectTransform PanelRoot => panelRoot;
        public Button UpgradeButton => upgradeButton;
        public Text UpgradeCostLabel => upgradeCostLabel;
        public Button CardsButton => cardsButton;
        public TowerSelectionView Target => target;
        public bool IsVisible =>
            visible &&
            panelRoot != null &&
            panelRoot.gameObject.activeSelf;
        public bool IsPlacedOnLeft => placedOnLeft;

        public static StageOneTowerActionView CreateRuntime(
            StageOneUiTextCatalog textCatalog,
            Font uiFont,
            Transform parent = null)
        {
            var host = new GameObject("Stage One Tower Actions");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            StageOneTowerActionView view =
                host.AddComponent<StageOneTowerActionView>();
            view.catalog = textCatalog ??
                StageOneUiTextCatalog.FromJson(null);
            view.font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            view.BuildInterface();
            view.ApplyFont();
            view.RefreshText();
            return view;
        }

        private void Awake()
        {
            catalog = catalog ??
                StageOneUiTextCatalog.FromJson(null);
            font = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            BuildInterface();
            RefreshText();
        }

        private void LateUpdate()
        {
            if (visible)
            {
                RefreshPosition();
            }
        }

        private void OnDestroy()
        {
            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveListener(
                    HandleUpgradeClicked);
            }

            if (cardsButton != null)
            {
                cardsButton.onClick.RemoveListener(
                    HandleCardsClicked);
            }
        }

        public void Show(
            TowerSelectionView tower,
            bool canUpgrade)
        {
            Show(tower, canUpgrade, 0, true);
            currentUpgradeCostVisible = false;
            currentUpgradeIsMaximum = false;
            RefreshText();
        }

        public void Show(
            TowerSelectionView tower,
            bool canUpgrade,
            int upgradeCost,
            bool canAfford)
        {
            BuildInterface();
            target = tower;
            visible = target != null;
            currentUpgradeCost = Math.Max(0, upgradeCost);
            currentUpgradeIsMaximum = upgradeCost < 0;
            currentUpgradeCostVisible = upgradeCost >= 0;
            currentUpgradeCanAfford = canAfford;
            panelRoot.gameObject.SetActive(visible);
            upgradeButton.interactable =
                visible && canUpgrade && canAfford;
            SetButtonColor(
                upgradeButton,
                upgradeButton.interactable
                    ? UpgradeColor
                    : DisabledColor);
            RefreshText();
            cardsButton.interactable = visible;
            if (visible)
            {
                RefreshPosition();
            }
        }

        /// <summary>
        /// 게임 규칙 계층이 계산한 업그레이드 견적을 그대로 표시한다.
        /// 최대 레벨과 구매 가능 여부를 이 뷰에서 다시 추론하지 않는다.
        /// </summary>
        public void Show(
            TowerSelectionView tower,
            TowerUpgradeQuote quote)
        {
            BuildInterface();
            target = tower;
            visible = target != null && quote.Exists;
            currentUpgradeCost = Math.Max(0, quote.Cost);
            currentUpgradeIsMaximum = quote.IsMaximumLevel;
            currentUpgradeCostVisible = quote.HasNextLevel;
            currentUpgradeCanAfford = quote.CanAfford;
            panelRoot.gameObject.SetActive(visible);
            upgradeButton.interactable =
                visible && quote.CanUpgrade;
            SetButtonColor(
                upgradeButton,
                upgradeButton.interactable
                    ? UpgradeColor
                    : DisabledColor);
            RefreshText();
            cardsButton.interactable = visible;
            if (visible)
            {
                RefreshPosition();
            }
        }

        public void Hide()
        {
            visible = false;
            target = null;
            if (panelRoot != null)
            {
                panelRoot.gameObject.SetActive(false);
            }
        }

        public void RefreshPosition()
        {
            if (!visible ||
                target == null ||
                panelRoot == null ||
                safeArea == null)
            {
                return;
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (worldCamera == null)
            {
                panelCanvasGroup.alpha = 0f;
                return;
            }

            Bounds bounds = target.WorldHitBounds;
            Vector3 screenMin = worldCamera.WorldToScreenPoint(
                new Vector3(
                    bounds.min.x,
                    bounds.min.y,
                    bounds.center.z));
            Vector3 screenMax = worldCamera.WorldToScreenPoint(
                new Vector3(
                    bounds.max.x,
                    bounds.max.y,
                    bounds.center.z));
            if (screenMin.z < 0f || screenMax.z < 0f)
            {
                panelCanvasGroup.alpha = 0f;
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                safeArea,
                screenMin,
                null,
                out Vector2 localMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                safeArea,
                screenMax,
                null,
                out Vector2 localMax);

            Rect safeRect = safeArea.rect;
            float halfWidth = PanelWidth * 0.5f;
            float halfHeight = PanelHeight * 0.5f;
            float rightX =
                Mathf.Max(localMin.x, localMax.x) +
                PanelGap +
                halfWidth;
            float leftX =
                Mathf.Min(localMin.x, localMax.x) -
                PanelGap -
                halfWidth;
            placedOnLeft =
                rightX + halfWidth >
                safeRect.xMax - EdgeMargin;
            float x = placedOnLeft ? leftX : rightX;
            float y =
                (localMin.y + localMax.y) * 0.5f;
            x = Mathf.Clamp(
                x,
                safeRect.xMin + halfWidth + EdgeMargin,
                safeRect.xMax - halfWidth - EdgeMargin);
            y = Mathf.Clamp(
                y,
                safeRect.yMin + halfHeight + EdgeMargin,
                safeRect.yMax - halfHeight - EdgeMargin);
            panelRoot.anchoredPosition = new Vector2(x, y);

            bool onScreen =
                Mathf.Max(localMin.x, localMax.x) >=
                    safeRect.xMin &&
                Mathf.Min(localMin.x, localMax.x) <=
                    safeRect.xMax &&
                Mathf.Max(localMin.y, localMax.y) >=
                    safeRect.yMin &&
                Mathf.Min(localMin.y, localMax.y) <=
                    safeRect.yMax;
            panelCanvasGroup.alpha = onScreen ? 1f : 0f;
            panelCanvasGroup.interactable = onScreen;
            panelCanvasGroup.blocksRaycasts = onScreen;
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            var canvasObject = new GameObject(
                "Tower Action Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 110;

            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<
                StageOneResponsiveCanvasScaler>();

            safeArea = new GameObject(
                "Safe Area",
                typeof(RectTransform),
                typeof(StageOneSafeAreaFitter))
                .GetComponent<RectTransform>();
            safeArea.SetParent(canvasObject.transform, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;

            var panelObject = new GameObject(
                "Selected Tower Actions",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            panelObject.transform.SetParent(safeArea, false);
            panelRoot =
                panelObject.GetComponent<RectTransform>();
            panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
            panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            panelRoot.pivot = new Vector2(0.5f, 0.5f);
            panelRoot.sizeDelta =
                new Vector2(PanelWidth, PanelHeight);
            RuleforgePixelUi.ApplyPanel(
                panelObject.GetComponent<Image>(),
                RuleforgePixelPanelRole.Workbench,
                PanelColor);
            panelCanvasGroup =
                panelObject.GetComponent<CanvasGroup>();

            upgradeButton = CreateButton(
                "Upgrade Tower Button",
                panelRoot,
                UpgradeColor,
                out upgradeLabel);
            AnchorButton(
                upgradeButton.GetComponent<RectTransform>(),
                38f);
            upgradeButton.onClick.AddListener(
                HandleUpgradeClicked);
            upgradeLabel.alignment = TextAnchor.MiddleLeft;
            upgradeLabel.rectTransform.offsetMax =
                new Vector2(-74f, -5f);

            var costLabelObject = new GameObject(
                "Upgrade Cost",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            costLabelObject.transform.SetParent(
                upgradeButton.transform,
                false);
            upgradeCostLabel =
                costLabelObject.GetComponent<Text>();
            RuleforgeUiTypography.Configure(
                upgradeCostLabel,
                font,
                16,
                CostColor,
                TextAnchor.MiddleRight,
                true);
            RectTransform costRect =
                upgradeCostLabel.rectTransform;
            costRect.anchorMin = new Vector2(0.56f, 0f);
            costRect.anchorMax = Vector2.one;
            costRect.offsetMin = new Vector2(0f, 5f);
            costRect.offsetMax = new Vector2(-10f, -5f);

            cardsButton = CreateButton(
                "Equip Cards Button",
                panelRoot,
                CardsColor,
                out cardsLabel);
            AnchorButton(
                cardsButton.GetComponent<RectTransform>(),
                -30f);
            cardsButton.onClick.AddListener(
                HandleCardsClicked);

            panelRoot.gameObject.SetActive(false);
            built = true;
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            Color color,
            out Text label)
        {
            var buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = color;
            Button button =
                buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor =
                new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.disabledColor = Color.white;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            labelObject.transform.SetParent(
                buttonObject.transform,
                false);
            label = labelObject.GetComponent<Text>();
            RuleforgeUiTypography.Configure(
                label,
                font,
                18,
                TextColor,
                TextAnchor.MiddleCenter,
                true);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 5f);
            labelRect.offsetMax = new Vector2(-8f, -5f);
            RuleforgePixelUi.ApplyLegacyColor(button, color);
            return button;
        }

        private static void AnchorButton(
            RectTransform rect,
            float centerY)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, centerY);
            rect.sizeDelta = new Vector2(188f, 58f);
        }

        private static void SetButtonColor(
            Button button,
            Color color)
        {
            if (button != null &&
                button.targetGraphic != null)
            {
                RuleforgePixelUi.ApplyLegacyColor(button, color);
            }
        }

        private void RefreshText()
        {
            if (!built)
            {
                return;
            }

            upgradeLabel.text = currentUpgradeIsMaximum
                ? catalog.Get("tower_panel.max_level")
                : catalog.Get("tower_action.upgrade");
            bool showCost =
                !currentUpgradeIsMaximum &&
                currentUpgradeCostVisible;
            upgradeCostLabel.gameObject.SetActive(showCost);
            upgradeCostLabel.text = showCost
                ? catalog.Format(
                    "tower_action.upgrade_cost_value_format",
                    currentUpgradeCost)
                : string.Empty;
            upgradeCostLabel.color = currentUpgradeCanAfford
                ? CostColor
                : UnaffordableCostColor;
            upgradeLabel.alignment = showCost
                ? TextAnchor.MiddleLeft
                : TextAnchor.MiddleCenter;
            upgradeLabel.rectTransform.offsetMax = showCost
                ? new Vector2(-74f, -5f)
                : new Vector2(-8f, -5f);
            cardsLabel.text =
                catalog.Get("tower_action.cards");
        }

        private void ApplyFont()
        {
            Text[] labels =
                GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].font = font;
            }
        }

        private void HandleUpgradeClicked()
        {
            UpgradeRequested?.Invoke();
        }

        private void HandleCardsClicked()
        {
            CardsRequested?.Invoke();
        }
    }
}
