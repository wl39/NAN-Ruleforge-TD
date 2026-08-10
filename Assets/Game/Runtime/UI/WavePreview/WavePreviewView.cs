using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 다음 웨이브 요약은 항상 작게 유지하고, 명시적인 클릭/터치로 상세를 연다.
    /// 마우스 오버와 색상만으로 정보를 전달하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WavePreviewView : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        private const float SummaryEdgeMargin = 8f;

        private static readonly Color PanelColor =
            Color.white;
        private static readonly Color DetailColor =
            Color.white;
        private static readonly Color NormalColor =
            new Color32(79, 88, 51, 255);
        private static readonly Color EliteColor =
            new Color32(120, 75, 47, 255);
        private static readonly Color BossColor =
            new Color32(113, 50, 65, 255);
        private static readonly Color AccentColor =
            new Color32(142, 77, 31, 255);
        private static readonly Color TextColor =
            new Color32(53, 35, 23, 255);
        private static readonly Color MutedColor =
            new Color32(104, 72, 43, 255);

        private readonly List<Button> summaryGroupButtons =
            new List<Button>();
        private readonly List<Button> detailGroupButtons =
            new List<Button>();
        private IWavePreviewLocalization textCatalog;
        private Font font;
        private Canvas canvas;
        private GraphicRaycaster raycaster;
        private RectTransform summaryPanel;
        private Button summaryButton;
        private Text titleText;
        private Text totalEnemyText;
        private Text compositionText;
        private Text coverageText;
        private RectTransform summaryGroupStrip;
        private RectTransform detailPanel;
        private Text detailTitleText;
        private Image detailIcon;
        private Text selectedEnemyText;
        private Text detailText;
        private RectTransform detailContent;
        private RectTransform detailViewport;
        private RectTransform detailSectionsRoot;
        private RectTransform detailGroupStrip;
        private ScrollRect detailScroll;
        private Button closeButton;
        private WavePreviewModel model;
        private int selectedGroupIndex;
        private bool built;
        private bool visible = true;
        private bool interactionBlocked;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private bool draggingSummary;
        private int dragPointerId = int.MinValue;
        private int suppressSummaryClickUntilFrame = -1;
        private bool hasCustomSummaryPosition;
        private Vector2 normalizedSummaryPosition;

        public Button SummaryButton => summaryButton;
        public Button CloseButton => closeButton;
        public Text TotalEnemyText => totalEnemyText;
        public Text DetailText => detailText;
        public GameObject DetailPanel =>
            detailPanel == null ? null : detailPanel.gameObject;
        public int GroupButtonCount => summaryGroupButtons.Count;
        public bool IsDetailVisible =>
            detailPanel != null && detailPanel.gameObject.activeSelf;
        public bool IsVisible => canvas != null && canvas.enabled;

        /// <summary>
        /// Closes only the expanded forecast while keeping the compact next
        /// wave summary available. Tutorial presentation uses this after the
        /// player has inspected the forecast so it never mutates battle state.
        /// </summary>
        public void CloseExpandedDetail()
        {
            CloseDetail();
        }

        public static WavePreviewView CreateRuntime(
            IWavePreviewLocalization catalog,
            Font uiFont,
            Transform parent = null)
        {
            var host = new GameObject("Next Wave Preview");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            WavePreviewView view =
                host.AddComponent<WavePreviewView>();
            view.Configure(catalog, uiFont);
            return view;
        }

        private void Update()
        {
            if (!built)
            {
                return;
            }

            if (lastScreenWidth != Screen.width ||
                lastScreenHeight != Screen.height)
            {
                ApplyResponsiveLayout();
            }
        }

        private void OnDestroy()
        {
            RemoveButtonListeners(summaryGroupButtons);
            RemoveButtonListeners(detailGroupButtons);
            if (summaryButton != null)
            {
                summaryButton.onClick.RemoveListener(ToggleDetail);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(CloseDetail);
            }
        }

        public void Configure(
            IWavePreviewLocalization catalog,
            Font uiFont)
        {
            textCatalog = catalog ??
                WavePreviewFallbackLocalization.Instance;
            font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            BuildInterface();
        }

        public void ApplyModel(WavePreviewModel nextModel)
        {
            BuildInterface();
            model = nextModel;
            selectedGroupIndex = 0;
            ApplyResponsiveLayout();
            bool hasModel = model != null && model.IsValid;
            summaryPanel.gameObject.SetActive(hasModel);
            if (!hasModel)
            {
                CloseDetail();
                return;
            }

            titleText.text = model.Title;
            totalEnemyText.text = model.TotalText;
            compositionText.text = model.CompositionText;
            coverageText.text = model.CoverageText;
            RebuildGroupButtons(
                summaryGroupStrip,
                summaryGroupButtons,
                false);
            RebuildGroupButtons(
                detailGroupStrip,
                detailGroupButtons,
                true);
            RefreshSelectedGroup();
        }

        public void SetVisible(bool shouldShow)
        {
            visible = shouldShow;
            if (canvas == null)
            {
                return;
            }

            canvas.enabled = shouldShow;
            RefreshRaycaster();
            if (!shouldShow)
            {
                CloseDetail();
            }
        }

        public void SetInteractionBlocked(bool blocked)
        {
            interactionBlocked = blocked;
            RefreshRaycaster();
            if (blocked)
            {
                CloseDetail();
            }
        }

        public void OpenGroup(int index)
        {
            if (interactionBlocked || model == null ||
                index < 0 || index >= model.Groups.Length)
            {
                return;
            }

            selectedGroupIndex = index;
            RefreshSelectedGroup();
            detailPanel.gameObject.SetActive(true);
        }

        public void CloseDetail()
        {
            if (detailPanel != null)
            {
                detailPanel.gameObject.SetActive(false);
            }
        }

        private void ToggleDetail()
        {
            if (Time.frameCount <= suppressSummaryClickUntilFrame)
            {
                return;
            }

            if (interactionBlocked || model == null || !model.IsValid)
            {
                return;
            }

            if (IsDetailVisible)
            {
                CloseDetail();
            }
            else
            {
                OpenGroup(selectedGroupIndex);
            }
        }

        private void RefreshRaycaster()
        {
            if (raycaster != null)
            {
                raycaster.enabled = visible && !interactionBlocked;
            }
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            EnsureEventSystem();
            GameObject canvasObject = new GameObject(
                "Next Wave Preview Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 104;
            raycaster = canvasObject.GetComponent<GraphicRaycaster>();

            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<RuntimeResponsiveCanvasScaler>();

            RectTransform safeArea = new GameObject(
                "Safe Area",
                typeof(RectTransform),
                typeof(RuntimeSafeAreaFitter))
                .GetComponent<RectTransform>();
            safeArea.SetParent(canvasObject.transform, false);
            Stretch(safeArea, 0f, 0f, 0f, 0f);

            BuildSummary(safeArea);
            BuildDetail(safeArea);
            built = true;
            ApplyResponsiveLayout();
            SetVisible(visible);
        }

        private void BuildSummary(Transform parent)
        {
            summaryPanel = CreatePanel(
                "Compact Preview",
                parent,
                PanelColor);
            RuleforgePixelUi.ApplyPanel(
                summaryPanel.GetComponent<Image>(),
                RuleforgePixelPanelRole.Parchment,
                PanelColor);
            summaryPanel.anchorMin = new Vector2(0f, 1f);
            summaryPanel.anchorMax = new Vector2(0f, 1f);
            summaryPanel.pivot = new Vector2(0f, 1f);
            summaryButton = summaryPanel.gameObject.AddComponent<Button>();
            summaryButton.targetGraphic =
                summaryPanel.GetComponent<Image>();
            summaryButton.onClick.AddListener(ToggleDetail);

            titleText = CreateText(
                "Preview Title",
                summaryPanel,
                16,
                FontStyle.Bold,
                AccentColor,
                TextAnchor.MiddleLeft);
            SetRect(titleText.rectTransform, 18f, -13f, 170f, 28f);

            totalEnemyText = CreateText(
                "Total Enemy Count",
                summaryPanel,
                22,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleRight);
            totalEnemyText.rectTransform.anchorMin = new Vector2(1f, 1f);
            totalEnemyText.rectTransform.anchorMax = new Vector2(1f, 1f);
            totalEnemyText.rectTransform.pivot = new Vector2(1f, 1f);
            totalEnemyText.rectTransform.anchoredPosition =
                new Vector2(-18f, -12f);
            totalEnemyText.rectTransform.sizeDelta =
                new Vector2(124f, 30f);

            compositionText = CreateText(
                "Composition",
                summaryPanel,
                13,
                FontStyle.Normal,
                MutedColor,
                TextAnchor.MiddleLeft);
            SetRect(
                compositionText.rectTransform,
                24f,
                -45f,
                344f,
                22f);

            coverageText = CreateText(
                "Coverage",
                summaryPanel,
                13,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            SetRect(
                coverageText.rectTransform,
                24f,
                -69f,
                344f,
                22f);

            RectTransform summaryDivider = CreateDivider(
                "Summary Divider",
                summaryPanel);
            SetRect(summaryDivider, 24f, -98f, 344f, 2f);

            summaryGroupStrip = new GameObject(
                "Enemy Groups",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            summaryGroupStrip.SetParent(summaryPanel, false);
            summaryGroupStrip.anchorMin = new Vector2(0f, 0f);
            summaryGroupStrip.anchorMax = new Vector2(1f, 0f);
            summaryGroupStrip.pivot = new Vector2(0.5f, 0f);
            summaryGroupStrip.anchoredPosition =
                new Vector2(0f, 14f);
            summaryGroupStrip.sizeDelta = new Vector2(-44f, 72f);

            // The always-visible forecast is intentionally a single row.
            // The composition, coverage, and enemy cards remain available in
            // the explicit detail view without covering the battlefield.
            compositionText.gameObject.SetActive(false);
            coverageText.gameObject.SetActive(false);
            summaryDivider.gameObject.SetActive(false);
            summaryGroupStrip.gameObject.SetActive(false);
        }

        private void BuildDetail(Transform parent)
        {
            detailPanel = CreatePanel(
                "Detailed Preview",
                parent,
                DetailColor);
            RuleforgePixelUi.ApplyPanel(
                detailPanel.GetComponent<Image>(),
                RuleforgePixelPanelRole.Parchment,
                DetailColor);
            detailPanel.anchorMin = new Vector2(1f, 0.5f);
            detailPanel.anchorMax = new Vector2(1f, 0.5f);
            detailPanel.pivot = new Vector2(1f, 0.5f);

            detailTitleText = CreateText(
                "Detail Title",
                detailPanel,
                18,
                FontStyle.Bold,
                AccentColor,
                TextAnchor.MiddleLeft);
            detailTitleText.rectTransform.anchorMin =
                new Vector2(0f, 1f);
            detailTitleText.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            detailTitleText.rectTransform.pivot =
                new Vector2(0.5f, 1f);
            detailTitleText.rectTransform.anchoredPosition =
                new Vector2(-22f, -12f);
            detailTitleText.rectTransform.sizeDelta =
                new Vector2(-122f, 34f);

            closeButton = CreateButton(
                "Close Detail",
                detailPanel,
                NormalColor,
                "×",
                30);
            closeButton.onClick.AddListener(CloseDetail);
            RectTransform closeRect =
                closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-12f, -10f);
            closeRect.sizeDelta = new Vector2(56f, 42f);

            detailIcon = new GameObject(
                "Selected Enemy Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image))
                .GetComponent<Image>();
            detailIcon.transform.SetParent(detailPanel, false);
            detailIcon.preserveAspect = true;
            detailIcon.color = Color.white;
            RectTransform iconRect = detailIcon.rectTransform;
            iconRect.anchorMin = new Vector2(0f, 1f);
            iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0f, 1f);
            iconRect.anchoredPosition = new Vector2(24f, -56f);
            iconRect.sizeDelta = new Vector2(112f, 112f);

            selectedEnemyText = CreateText(
                "Selected Enemy Identity",
                detailPanel,
                21,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            selectedEnemyText.rectTransform.anchorMin =
                new Vector2(0f, 1f);
            selectedEnemyText.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            selectedEnemyText.rectTransform.pivot =
                new Vector2(0.5f, 1f);
            selectedEnemyText.rectTransform.anchoredPosition =
                new Vector2(70f, -67f);
            selectedEnemyText.rectTransform.sizeDelta =
                new Vector2(-188f, 82f);
            selectedEnemyText.lineSpacing = 1.15f;

            RectTransform identityDivider = CreateDivider(
                "Identity Divider",
                detailPanel);
            identityDivider.anchorMin = new Vector2(0f, 1f);
            identityDivider.anchorMax = new Vector2(1f, 1f);
            identityDivider.pivot = new Vector2(0.5f, 1f);
            identityDivider.anchoredPosition = new Vector2(0f, -180f);
            identityDivider.sizeDelta = new Vector2(-40f, 2f);

            detailGroupStrip = new GameObject(
                "Detail Group Tabs",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            detailGroupStrip.SetParent(detailPanel, false);
            detailGroupStrip.anchorMin = new Vector2(0f, 1f);
            detailGroupStrip.anchorMax = new Vector2(1f, 1f);
            detailGroupStrip.pivot = new Vector2(0.5f, 1f);
            detailGroupStrip.anchoredPosition =
                new Vector2(0f, -194f);
            detailGroupStrip.sizeDelta = new Vector2(-40f, 76f);

            RectTransform viewport = CreatePanel(
                "Detail Scroll Viewport",
                detailPanel,
                new Color(0f, 0f, 0f, 0.01f));
            viewport.anchorMin = new Vector2(0f, 0f);
            viewport.anchorMax = new Vector2(1f, 1f);
            viewport.offsetMin = new Vector2(16f, 16f);
            viewport.offsetMax = new Vector2(-16f, -198f);
            detailViewport = viewport;
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            RectTransform content = new GameObject(
                "Detail Content",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(-22f, 0f);
            detailContent = content;
            VerticalLayoutGroup contentLayout =
                content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            ContentSizeFitter contentFitter =
                content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            detailText = CreateText(
                "Detail Text",
                content,
                15,
                FontStyle.Normal,
                TextColor,
                TextAnchor.UpperLeft);
            detailText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            detailText.verticalOverflow =
                VerticalWrapMode.Overflow;
            detailText.supportRichText = true;
            detailText.lineSpacing = 1.18f;
            ContentSizeFitter fitter =
                detailText.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            detailSectionsRoot = new GameObject(
                "Detail Sections",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter))
                .GetComponent<RectTransform>();
            detailSectionsRoot.SetParent(content, false);
            VerticalLayoutGroup sectionsLayout =
                detailSectionsRoot.GetComponent<VerticalLayoutGroup>();
            sectionsLayout.spacing = 12f;
            sectionsLayout.childAlignment = TextAnchor.UpperLeft;
            sectionsLayout.childControlWidth = true;
            sectionsLayout.childControlHeight = true;
            sectionsLayout.childForceExpandWidth = true;
            sectionsLayout.childForceExpandHeight = false;
            ContentSizeFitter sectionsFitter =
                detailSectionsRoot.GetComponent<ContentSizeFitter>();
            sectionsFitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            detailScroll =
                viewport.gameObject.AddComponent<ScrollRect>();
            detailScroll.viewport = viewport;
            detailScroll.content = content;
            detailScroll.horizontal = false;
            detailScroll.vertical = true;
            detailScroll.movementType = ScrollRect.MovementType.Clamped;
            detailScroll.scrollSensitivity = 28f;
            detailPanel.gameObject.SetActive(false);
        }

        private void RebuildGroupButtons(
            RectTransform parent,
            List<Button> buttons,
            bool detailed)
        {
            RemoveButtonListeners(buttons);
            for (int childIndex = parent.childCount - 1;
                 childIndex >= 0;
                 childIndex--)
            {
                Destroy(parent.GetChild(childIndex).gameObject);
            }
            buttons.Clear();

            if (model == null)
            {
                return;
            }

            WavePreviewGroupModel[] groups = model.Groups;
            float gap = 8f;
            bool crowded = groups.Length >= 3;
            bool stacked = crowded;
            for (int i = 0; i < groups.Length; i++)
            {
                int capturedIndex = i;
                WavePreviewGroupModel group = groups[i];
                Color color = group.IsBoss
                    ? BossColor
                    : group.IsElite
                        ? EliteColor
                        : NormalColor;
                Button button = CreateButton(
                    "Enemy Group " + i,
                    parent,
                    color,
                    crowded
                        ? "×" + group.Count
                        : detailed
                        ? group.DisplayName + "\n" +
                          group.RankLabel + " ×" + group.Count
                        : group.DisplayName + "\n×" + group.Count +
                          " · " + group.RankLabel,
                    crowded ? 14 :
                        detailed ? 13 : 14);
                RectTransform rect =
                    button.GetComponent<RectTransform>();
                float leftAnchor =
                    i / (float)Mathf.Max(1, groups.Length);
                float rightAnchor =
                    (i + 1) / (float)Mathf.Max(1, groups.Length);
                rect.anchorMin = new Vector2(leftAnchor, 0f);
                rect.anchorMax = new Vector2(rightAnchor, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.offsetMin = new Vector2(
                    i == 0 ? 0f : gap * 0.5f,
                    0f);
                rect.offsetMax = new Vector2(
                    i == groups.Length - 1 ? 0f : -gap * 0.5f,
                    0f);

                Image buttonImage = button.GetComponent<Image>();
                if (buttonImage != null)
                {
                    buttonImage.type = Image.Type.Sliced;
                    buttonImage.preserveAspect = false;
                }
                button.onClick.AddListener(
                    delegate { OpenGroup(capturedIndex); });
                if (group.Sprite != null ||
                    group.PreviewAnimatorController != null)
                {
                    Image icon = new GameObject(
                        "Monster Image",
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(Image))
                        .GetComponent<Image>();
                    icon.transform.SetParent(button.transform, false);
                    icon.sprite = group.Sprite;
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    RectTransform iconRect = icon.rectTransform;
                    iconRect.anchorMin = stacked
                        ? new Vector2(0.5f, 1f)
                        : new Vector2(0f, 0.5f);
                    iconRect.anchorMax = iconRect.anchorMin;
                    iconRect.pivot = stacked
                        ? new Vector2(0.5f, 1f)
                        : new Vector2(0f, 0.5f);
                    iconRect.anchoredPosition = stacked
                        ? new Vector2(0f, -4f)
                        : new Vector2(
                            crowded ? 8f : detailed ? 12f : 16f,
                            0f);
                    float iconSize = stacked
                        ? 48f
                        : detailed
                            ? 52f
                            : 60f;
                    iconRect.sizeDelta = new Vector2(
                        iconSize,
                        iconSize);
                    WavePreviewAnimatedImage animation =
                        icon.gameObject.AddComponent<
                            WavePreviewAnimatedImage>();
                    animation.Configure(
                        group.Sprite,
                        group.PreviewAnimatorController);
                    ApplyMonsterAppearance(
                        icon,
                        group,
                        stacked ? 1.2f : 1.3f);

                    Text labelText =
                        button.GetComponentInChildren<Text>();
                    if (labelText != null)
                    {
                        labelText.alignment = stacked
                            ? TextAnchor.MiddleCenter
                            : TextAnchor.MiddleLeft;
                        labelText.lineSpacing = crowded ? 0.86f : 0.92f;
                        RectTransform labelRect =
                            labelText.rectTransform;
                        if (stacked)
                        {
                            labelRect.offsetMin = new Vector2(4f, 2f);
                            labelRect.offsetMax = new Vector2(-4f, -52f);
                        }
                        else
                        {
                            labelRect.offsetMin = new Vector2(
                                detailed ? 72f : 84f,
                                labelRect.offsetMin.y);
                            labelRect.offsetMax = new Vector2(
                                -12f,
                                labelRect.offsetMax.y);
                        }
                    }
                }
                buttons.Add(button);
            }
        }

        private void RefreshSelectedGroup()
        {
            if (model == null || model.Groups.Length == 0)
            {
                return;
            }

            selectedGroupIndex = Mathf.Clamp(
                selectedGroupIndex,
                0,
                model.Groups.Length - 1);
            WavePreviewGroupModel group =
                model.Groups[selectedGroupIndex];
            detailTitleText.text = model.Title;
            detailText.text = group.DetailText +
                (model.LoadoutLocked
                    ? "\n\n" + textCatalog.Get(
                        "wave_preview.loadout_locked")
                    : string.Empty);
            selectedEnemyText.text = group.DisplayName + "\n" +
                group.RankLabel + "  ·  ×" + group.Count;
            WavePreviewAnimatedImage detailAnimation =
                detailIcon.GetComponent<WavePreviewAnimatedImage>();
            if (detailAnimation == null)
            {
                detailAnimation = detailIcon.gameObject.AddComponent<
                    WavePreviewAnimatedImage>();
            }
            detailAnimation.Configure(
                group.Sprite,
                group.PreviewAnimatorController);
            ApplyMonsterAppearance(
                detailIcon,
                group,
                1.2f);

            bool hasMultipleGroups = model.Groups.Length > 1;
            detailGroupStrip.gameObject.SetActive(hasMultipleGroups);
            if (detailViewport != null)
            {
                detailViewport.offsetMax = new Vector2(
                    -16f,
                    hasMultipleGroups ? -282f : -198f);
            }

            RebuildDetailSections(group);
            Canvas.ForceUpdateCanvases();
            if (detailContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    detailContent);
            }
            if (detailScroll != null)
            {
                detailScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void RebuildDetailSections(
            WavePreviewGroupModel group)
        {
            WavePreviewDetailSectionModel[] sections =
                group.DetailSections ??
                Array.Empty<WavePreviewDetailSectionModel>();
            bool hasSections = sections.Length > 0;
            detailText.gameObject.SetActive(!hasSections);
            detailSectionsRoot.gameObject.SetActive(hasSections);
            if (!hasSections)
            {
                return;
            }

            for (int i = detailSectionsRoot.childCount - 1;
                 i >= 0;
                 i--)
            {
                Destroy(detailSectionsRoot.GetChild(i).gameObject);
            }

            for (int i = 0; i < sections.Length; i++)
            {
                CreateDetailSection(
                    sections[i].Title,
                    sections[i].Body,
                    i == sections.Length - 1);
            }

            if (model.LoadoutLocked)
            {
                CreateDetailSection(
                    textCatalog.Get(
                        "wave_preview.loadout_locked_label"),
                    textCatalog.Get("wave_preview.loadout_locked"),
                    true);
            }
        }

        private void CreateDetailSection(
            string title,
            string body,
            bool highlighted)
        {
            Image section = new GameObject(
                "Detail Section " + title,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter))
                .GetComponent<Image>();
            section.transform.SetParent(detailSectionsRoot, false);
            section.color = highlighted
                ? new Color32(225, 187, 129, 92)
                : new Color32(245, 224, 187, 64);
            section.raycastTarget = false;

            VerticalLayoutGroup layout =
                section.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 10, 12);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter sectionFitter =
                section.GetComponent<ContentSizeFitter>();
            sectionFitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            Text heading = CreateText(
                "Section Heading",
                section.transform,
                16,
                FontStyle.Bold,
                AccentColor,
                TextAnchor.MiddleLeft);
            heading.text = title;
            LayoutElement headingLayout =
                heading.gameObject.AddComponent<LayoutElement>();
            headingLayout.preferredHeight = 24f;

            Image divider = new GameObject(
                "Section Divider",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement))
                .GetComponent<Image>();
            divider.transform.SetParent(section.transform, false);
            divider.color = new Color32(142, 77, 31, 150);
            divider.raycastTarget = false;
            LayoutElement dividerLayout =
                divider.GetComponent<LayoutElement>();
            dividerLayout.preferredHeight = 2f;

            Text sectionBody = CreateText(
                "Section Body",
                section.transform,
                15,
                FontStyle.Normal,
                TextColor,
                TextAnchor.UpperLeft);
            sectionBody.text = body;
            sectionBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            sectionBody.verticalOverflow = VerticalWrapMode.Overflow;
            sectionBody.lineSpacing = 1.18f;
            ContentSizeFitter bodyFitter =
                sectionBody.gameObject.AddComponent<ContentSizeFitter>();
            bodyFitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
        }

        private void ApplyResponsiveLayout()
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            if (summaryPanel == null || detailPanel == null)
            {
                return;
            }

            bool compact = Screen.width < 900 ||
                           Screen.height > Screen.width;
            bool portrait = Screen.height > Screen.width;
            float topOffset =
                StageOneHudLayoutMetrics.GetTopOccupiedHeight(portrait) +
                StageOneHudLayoutMetrics.OverlaySeparation;
            summaryPanel.sizeDelta = compact
                ? new Vector2(292f, 54f)
                : new Vector2(320f, 54f);
            if (hasCustomSummaryPosition)
            {
                ApplyNormalizedSummaryPosition();
            }
            else
            {
                summaryPanel.anchoredPosition = new Vector2(
                    compact ? SummaryEdgeMargin : 12f,
                    -topOffset);
                ClampSummaryPosition();
            }

            if (compact)
            {
                detailPanel.anchorMin = new Vector2(0f, 0f);
                detailPanel.anchorMax = new Vector2(1f, 0.72f);
                detailPanel.pivot = new Vector2(0.5f, 0f);
                detailPanel.anchoredPosition = new Vector2(0f, 0f);
                detailPanel.sizeDelta = new Vector2(-12f, 0f);
            }
            else
            {
                detailPanel.anchorMin = new Vector2(1f, 0.5f);
                detailPanel.anchorMax = new Vector2(1f, 0.5f);
                detailPanel.pivot = new Vector2(1f, 0.5f);
                detailPanel.anchoredPosition = new Vector2(-14f, -8f);
                detailPanel.sizeDelta = new Vector2(560f, 720f);
            }
        }

        /// <summary>
        /// Lets mouse and touch users move the compact forecast out of their
        /// preferred play area. The position is kept normalized so a browser
        /// resize does not snap the panel back under stage navigation.
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null ||
                interactionBlocked ||
                summaryPanel == null ||
                !summaryPanel.gameObject.activeInHierarchy ||
                eventData.button != PointerEventData.InputButton.Left ||
                !IsSummaryPointer(eventData))
            {
                return;
            }

            draggingSummary = true;
            dragPointerId = eventData.pointerId;
            suppressSummaryClickUntilFrame = Time.frameCount;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!draggingSummary ||
                eventData == null ||
                eventData.pointerId != dragPointerId ||
                canvas == null)
            {
                return;
            }

            float scaleFactor = Mathf.Max(0.01f, canvas.scaleFactor);
            summaryPanel.anchoredPosition +=
                eventData.delta / scaleFactor;
            ClampSummaryPosition();
            CaptureNormalizedSummaryPosition();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!draggingSummary ||
                eventData == null ||
                eventData.pointerId != dragPointerId)
            {
                return;
            }

            ClampSummaryPosition();
            CaptureNormalizedSummaryPosition();
            draggingSummary = false;
            dragPointerId = int.MinValue;
            suppressSummaryClickUntilFrame = Time.frameCount;
        }

        private bool IsSummaryPointer(PointerEventData eventData)
        {
            GameObject pressed = eventData.pointerPress != null
                ? eventData.pointerPress
                : eventData.pointerPressRaycast.gameObject;
            return pressed != null &&
                (pressed == summaryPanel.gameObject ||
                 pressed.transform.IsChildOf(summaryPanel));
        }

        private void ClampSummaryPosition()
        {
            RectTransform parent = summaryPanel == null
                ? null
                : summaryPanel.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            float horizontalRange = Mathf.Max(
                0f,
                parent.rect.width - summaryPanel.rect.width -
                SummaryEdgeMargin * 2f);
            float verticalRange = Mathf.Max(
                0f,
                parent.rect.height - summaryPanel.rect.height -
                SummaryEdgeMargin * 2f);
            Vector2 position = summaryPanel.anchoredPosition;
            position.x = SummaryEdgeMargin + Mathf.Clamp(
                position.x - SummaryEdgeMargin,
                0f,
                horizontalRange);
            float distanceFromTop = Mathf.Clamp(
                -position.y - SummaryEdgeMargin,
                0f,
                verticalRange);
            position.y = -SummaryEdgeMargin - distanceFromTop;
            summaryPanel.anchoredPosition = position;
        }

        private void CaptureNormalizedSummaryPosition()
        {
            RectTransform parent = summaryPanel == null
                ? null
                : summaryPanel.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            float horizontalRange = Mathf.Max(
                0f,
                parent.rect.width - summaryPanel.rect.width -
                SummaryEdgeMargin * 2f);
            float verticalRange = Mathf.Max(
                0f,
                parent.rect.height - summaryPanel.rect.height -
                SummaryEdgeMargin * 2f);
            normalizedSummaryPosition = new Vector2(
                horizontalRange <= 0.01f
                    ? 0f
                    : (summaryPanel.anchoredPosition.x -
                       SummaryEdgeMargin) / horizontalRange,
                verticalRange <= 0.01f
                    ? 0f
                    : (-summaryPanel.anchoredPosition.y -
                       SummaryEdgeMargin) / verticalRange);
            normalizedSummaryPosition.x = Mathf.Clamp01(
                normalizedSummaryPosition.x);
            normalizedSummaryPosition.y = Mathf.Clamp01(
                normalizedSummaryPosition.y);
            hasCustomSummaryPosition = true;
        }

        private void ApplyNormalizedSummaryPosition()
        {
            RectTransform parent = summaryPanel == null
                ? null
                : summaryPanel.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            float horizontalRange = Mathf.Max(
                0f,
                parent.rect.width - summaryPanel.rect.width -
                SummaryEdgeMargin * 2f);
            float verticalRange = Mathf.Max(
                0f,
                parent.rect.height - summaryPanel.rect.height -
                SummaryEdgeMargin * 2f);
            summaryPanel.anchoredPosition = new Vector2(
                SummaryEdgeMargin +
                normalizedSummaryPosition.x * horizontalRange,
                -SummaryEdgeMargin -
                normalizedSummaryPosition.y * verticalRange);
            ClampSummaryPosition();
        }

        private Text CreateText(
            string name,
            Transform parent,
            int size,
            FontStyle style,
            Color color,
            TextAnchor alignment)
        {
            Text text = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text))
                .GetComponent<Text>();
            text.transform.SetParent(parent, false);
            RuleforgeUiTypography.Configure(
                text,
                font,
                size,
                color,
                alignment);
            return text;
        }

        private static void ApplyMonsterAppearance(
            Image icon,
            WavePreviewGroupModel group,
            float maximumScale)
        {
            if (icon == null)
            {
                return;
            }

            icon.color = group.PreviewTint;
            icon.rectTransform.localScale = Vector3.one * Mathf.Clamp(
                group.PreviewVisualScale,
                0.8f,
                maximumScale);

            Outline outline = icon.GetComponent<Outline>();
            if (!group.HasPreviewOutline)
            {
                if (outline != null)
                {
                    outline.enabled = false;
                }
                return;
            }

            if (outline == null)
            {
                outline = icon.gameObject.AddComponent<Outline>();
            }
            outline.enabled = true;
            outline.effectColor = group.PreviewOutlineColor;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;
        }

        private Button CreateButton(
            string name,
            Transform parent,
            Color color,
            string label,
            int size)
        {
            Image image = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button))
                .GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            Button button = image.GetComponent<Button>();
            button.targetGraphic = image;
            Text text = CreateText(
                "Label",
                image.transform,
                size,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 4f, 3f, 4f, 3f);
            text.text = label;
            RuleforgePixelUi.ApplyTint(
                button,
                name.IndexOf(
                    "close",
                    StringComparison.OrdinalIgnoreCase) >= 0
                    ? RuleforgePixelButtonRole.Danger
                    : RuleforgePixelButtonRole.Secondary,
                color);
            return button;
        }

        private static RectTransform CreatePanel(
            string name,
            Transform parent,
            Color color)
        {
            Image image = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image))
                .GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            return image.rectTransform;
        }

        private static RectTransform CreateDivider(
            string name,
            Transform parent)
        {
            Image image = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image))
                .GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = new Color32(142, 77, 31, 150);
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private static void RemoveButtonListeners(List<Button> buttons)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i] != null)
                {
                    buttons[i].onClick.RemoveAllListeners();
                }
            }
        }

        private static void SetRect(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }
    }
}
