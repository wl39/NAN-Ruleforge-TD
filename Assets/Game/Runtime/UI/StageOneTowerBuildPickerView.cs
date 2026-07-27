using System;
using System.Collections.Generic;
using RuleforgeTD.Maps;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    public readonly struct StageOneTowerBuildOption
    {
        public StageOneTowerBuildOption(
            string definitionId,
            string name,
            string description,
            bool targetsEnemies)
        {
            DefinitionId = definitionId ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            TargetsEnemies = targetsEnemies;
        }

        public string DefinitionId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool TargetsEnemies { get; }
    }

    /// <summary>
    /// Screen-space tower picker shown after an available build spot is
    /// selected. It follows the spot, stays inside the safe area, and does not
    /// issue simulation commands itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneTowerBuildPickerView : MonoBehaviour
    {
        private const float PanelWidth = 380f;
        private const float HeaderHeight = 76f;
        private const float OptionHeight = 88f;
        private const float OptionGap = 8f;
        private const float BottomPadding = 12f;
        private const float PanelGap = 18f;
        private const float EdgeMargin = 12f;

        private static readonly Color PanelColor =
            new Color32(17, 35, 50, 248);
        private static readonly Color ProjectileColor =
            new Color32(184, 96, 32, 255);
        private static readonly Color EnemyColor =
            new Color32(42, 103, 125, 255);
        private static readonly Color TextColor =
            new Color32(255, 245, 215, 255);
        private static readonly Color MutedTextColor =
            new Color32(204, 218, 220, 255);

        private readonly List<Button> optionButtons =
            new List<Button>(4);
        private readonly List<string> optionIds =
            new List<string>(4);
        private readonly List<GameObject> optionObjects =
            new List<GameObject>(4);

        private StageOneUiTextCatalog catalog;
        private Font font;
        private Canvas canvas;
        private RectTransform safeArea;
        private RectTransform panelRoot;
        private CanvasGroup panelCanvasGroup;
        private Text titleLabel;
        private Text costLabel;
        private Button closeButton;
        private TowerBuildSiteView target;
        private Camera worldCamera;
        private bool visible;
        private bool built;
        private bool placedOnLeft;

        public event Action<string> TowerRequested;
        public event Action CloseRequested;

        public Canvas Canvas => canvas;
        public RectTransform PanelRoot => panelRoot;
        public TowerBuildSiteView Target => target;
        public int OptionCount => optionButtons.Count;
        public bool IsVisible =>
            visible &&
            panelRoot != null &&
            panelRoot.gameObject.activeSelf;
        public bool IsPlacedOnLeft => placedOnLeft;
        public Button CloseButton => closeButton;

        public static StageOneTowerBuildPickerView CreateRuntime(
            StageOneUiTextCatalog textCatalog,
            Font uiFont,
            Transform parent = null)
        {
            var host = new GameObject("Stage One Tower Build Picker");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            StageOneTowerBuildPickerView view =
                host.AddComponent<StageOneTowerBuildPickerView>();
            view.catalog = textCatalog ??
                StageOneUiTextCatalog.FromJson(null);
            view.font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            view.BuildInterface();
            view.ApplyFont();
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
            ClearOptions();
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(
                    HandleCloseClicked);
            }
        }

        public Button GetOptionButton(int optionIndex)
        {
            return optionIndex >= 0 &&
                   optionIndex < optionButtons.Count
                ? optionButtons[optionIndex]
                : null;
        }

        public string GetOptionId(int optionIndex)
        {
            return optionIndex >= 0 &&
                   optionIndex < optionIds.Count
                ? optionIds[optionIndex]
                : string.Empty;
        }

        public void Show(
            TowerBuildSiteView buildSite,
            IReadOnlyList<StageOneTowerBuildOption> options,
            int constructionCost)
        {
            BuildInterface();
            ClearOptions();
            target = buildSite;

            int optionCount = options == null
                ? 0
                : options.Count;
            for (int i = 0; i < optionCount; i++)
            {
                CreateOption(options[i], i);
            }

            visible = target != null && optionCount > 0;
            titleLabel.text =
                catalog.Get("tower_build.title");
            costLabel.text = constructionCost <= 0
                ? catalog.Get("tower_build.free")
                : catalog.Format(
                    "tower_build.cost_format",
                    constructionCost);
            panelRoot.sizeDelta = new Vector2(
                PanelWidth,
                HeaderHeight +
                optionCount * OptionHeight +
                Mathf.Max(0, optionCount - 1) * OptionGap +
                BottomPadding);
            panelRoot.gameObject.SetActive(visible);
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
                SetOnScreen(false);
                return;
            }

            Renderer targetRenderer =
                target.GetComponentInChildren<Renderer>();
            Bounds bounds = targetRenderer != null
                ? targetRenderer.bounds
                : new Bounds(
                    target.transform.position,
                    Vector3.one);
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
                SetOnScreen(false);
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
            float halfWidth = panelRoot.rect.width * 0.5f;
            float halfHeight = panelRoot.rect.height * 0.5f;
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
            SetOnScreen(onScreen);
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            var canvasObject = new GameObject(
                "Tower Build Picker Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 115;

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
                "Tower Build Options",
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
                new Vector2(PanelWidth, HeaderHeight);
            panelObject.GetComponent<Image>().color =
                PanelColor;
            panelCanvasGroup =
                panelObject.GetComponent<CanvasGroup>();

            titleLabel = CreateText(
                "Title",
                panelRoot,
                22,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            RectTransform titleRect = titleLabel.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(18f, -44f);
            titleRect.offsetMax = new Vector2(-62f, -8f);

            costLabel = CreateText(
                "Cost",
                panelRoot,
                15,
                FontStyle.Normal,
                MutedTextColor,
                TextAnchor.MiddleLeft);
            RectTransform costRect = costLabel.rectTransform;
            costRect.anchorMin = new Vector2(0f, 1f);
            costRect.anchorMax = new Vector2(1f, 1f);
            costRect.pivot = new Vector2(0.5f, 1f);
            costRect.offsetMin = new Vector2(18f, -68f);
            costRect.offsetMax = new Vector2(-18f, -43f);

            closeButton = CreateButton(
                "Close",
                panelRoot,
                new Color32(89, 99, 105, 255));
            RectTransform closeRect =
                closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-10f, -10f);
            closeRect.sizeDelta = new Vector2(54f, 54f);
            Text closeLabel = CreateText(
                "Label",
                closeRect,
                23,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleCenter);
            Stretch(closeLabel.rectTransform, 0f);
            closeLabel.text = "×";
            closeButton.onClick.AddListener(
                HandleCloseClicked);

            panelRoot.gameObject.SetActive(false);
            built = true;
        }

        private void CreateOption(
            StageOneTowerBuildOption option,
            int optionIndex)
        {
            Color optionColor = option.TargetsEnemies
                ? EnemyColor
                : ProjectileColor;
            Button button = CreateButton(
                "Build " + option.DefinitionId,
                panelRoot,
                optionColor);
            RectTransform buttonRect =
                button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            float y = -HeaderHeight -
                optionIndex * (OptionHeight + OptionGap);
            buttonRect.offsetMin =
                new Vector2(12f, y - OptionHeight);
            buttonRect.offsetMax =
                new Vector2(-12f, y);

            Text nameLabel = CreateText(
                "Name",
                buttonRect,
                19,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            RectTransform nameRect = nameLabel.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.offsetMin = new Vector2(14f, -35f);
            nameRect.offsetMax = new Vector2(-14f, -6f);
            nameLabel.text = option.Name;

            Text descriptionLabel = CreateText(
                "Description",
                buttonRect,
                13,
                FontStyle.Normal,
                MutedTextColor,
                TextAnchor.UpperLeft);
            RectTransform descriptionRect =
                descriptionLabel.rectTransform;
            descriptionRect.anchorMin = Vector2.zero;
            descriptionRect.anchorMax = Vector2.one;
            descriptionRect.offsetMin = new Vector2(14f, 8f);
            descriptionRect.offsetMax = new Vector2(-14f, -38f);
            descriptionLabel.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            descriptionLabel.verticalOverflow =
                VerticalWrapMode.Truncate;
            descriptionLabel.text = option.Description;

            string definitionId = option.DefinitionId;
            button.onClick.AddListener(
                () => HandleOptionClicked(definitionId));
            optionButtons.Add(button);
            optionIds.Add(definitionId);
            optionObjects.Add(button.gameObject);
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            Color color)
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
            return button;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int size,
            FontStyle style,
            Color color,
            TextAnchor alignment)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text label = textObject.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            return label;
        }

        private static void Stretch(
            RectTransform rect,
            float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }

        private void ClearOptions()
        {
            for (int i = 0; i < optionButtons.Count; i++)
            {
                if (optionButtons[i] != null)
                {
                    optionButtons[i].onClick.RemoveAllListeners();
                }
            }

            for (int i = 0; i < optionObjects.Count; i++)
            {
                if (optionObjects[i] != null)
                {
                    Destroy(optionObjects[i]);
                }
            }

            optionButtons.Clear();
            optionIds.Clear();
            optionObjects.Clear();
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

        private void SetOnScreen(bool onScreen)
        {
            if (panelCanvasGroup == null)
            {
                return;
            }

            panelCanvasGroup.alpha = onScreen ? 1f : 0f;
            panelCanvasGroup.interactable = onScreen;
            panelCanvasGroup.blocksRaycasts = onScreen;
        }

        private void HandleOptionClicked(string definitionId)
        {
            if (!visible ||
                string.IsNullOrWhiteSpace(definitionId))
            {
                return;
            }

            TowerRequested?.Invoke(definitionId);
        }

        private void HandleCloseClicked()
        {
            Hide();
            CloseRequested?.Invoke();
        }
    }
}
