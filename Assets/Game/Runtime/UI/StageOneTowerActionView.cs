using System;
using RuleforgeTD.Battle;
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
            new Color32(18, 35, 49, 238);
        private static readonly Color UpgradeColor =
            new Color32(219, 126, 40, 255);
        private static readonly Color CardsColor =
            new Color32(48, 112, 151, 255);
        private static readonly Color DisabledColor =
            new Color32(75, 84, 91, 230);
        private static readonly Color TextColor =
            new Color32(255, 244, 215, 255);

        private StageOneUiTextCatalog catalog;
        private Font font;
        private Canvas canvas;
        private RectTransform safeArea;
        private RectTransform panelRoot;
        private CanvasGroup panelCanvasGroup;
        private Button upgradeButton;
        private Button cardsButton;
        private Text upgradeLabel;
        private Text cardsLabel;
        private TowerSelectionView target;
        private Camera worldCamera;
        private bool visible;
        private bool built;
        private bool placedOnLeft;

        public event Action UpgradeRequested;
        public event Action CardsRequested;

        public Canvas Canvas => canvas;
        public RectTransform PanelRoot => panelRoot;
        public Button UpgradeButton => upgradeButton;
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
            BuildInterface();
            target = tower;
            visible = target != null;
            panelRoot.gameObject.SetActive(visible);
            upgradeButton.interactable =
                visible && canUpgrade;
            SetButtonColor(
                upgradeButton,
                upgradeButton.interactable
                    ? UpgradeColor
                    : DisabledColor);
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
            panelObject.GetComponent<Image>().color =
                PanelColor;
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
            label.font = font;
            label.fontSize = 18;
            label.fontStyle = FontStyle.Bold;
            label.color = TextColor;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            label.verticalOverflow =
                VerticalWrapMode.Truncate;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 5f);
            labelRect.offsetMax = new Vector2(-8f, -5f);
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
                button.targetGraphic.color = color;
            }
        }

        private void RefreshText()
        {
            if (!built)
            {
                return;
            }

            upgradeLabel.text =
                catalog.Get("tower_action.upgrade");
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
