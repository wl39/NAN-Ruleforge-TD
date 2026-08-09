using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Responsive, read-only enemy information panel. It uses a right rail on
    /// desktop and a bottom sheet on compact screens, renders immutable
    /// inspection models, and never queries combat state directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneEnemyInspectionView : MonoBehaviour
    {
        public const float DesktopPanelWidth = 472f;
        public const float DesktopTopInset =
            StageOneHudLayoutMetrics.DesktopTopOccupiedHeight +
            StageOneHudLayoutMetrics.OverlaySeparation;
        public const float CompactPanelHeightRatio =
            StageOneHudLayoutMetrics
                .CompactBottomSheetHeightRatio;
        public const float PanelMargin = 16f;
        public static readonly Vector2 EnemyViewportAnchor =
            new Vector2(0.28f, 0.5f);

        private const float HeaderHeight = 112f;
        private const float OuterMargin = PanelMargin;
        private const float SectionSpacing = 10f;
        private const float StatusRowHeight = 116f;

        private static readonly Color PanelColor =
            Color.white;
        private static readonly Color PanelBorderColor =
            new Color32(239, 61, 54, 255);
        private static readonly Color SectionColor =
            new Color32(75, 47, 30, 245);
        private static readonly Color TextColor =
            new Color32(247, 241, 222, 255);
        private static readonly Color MutedTextColor =
            new Color32(205, 188, 151, 255);
        private static readonly Color HealthBackgroundColor =
            new Color32(48, 24, 27, 255);
        private static readonly Color HealthFillColor =
            new Color32(211, 62, 66, 255);
        private static readonly Color NormalRankColor =
            new Color32(74, 105, 58, 255);
        private static readonly Color EliteRankColor =
            new Color32(149, 76, 190, 255);
        private static readonly Color BossRankColor =
            new Color32(190, 48, 55, 255);

        private sealed class StatusRow
        {
            public GameObject Root;
            public Image Accent;
            public Text Name;
            public Text Summary;
            public Text Description;
            public Text Source;
        }

        private readonly List<StatusRow> statusRows =
            new List<StatusRow>(8);
        private readonly StringBuilder builder =
            new StringBuilder(256);

        private StageOneUiTextCatalog catalog;
        private Font font;
        private Canvas canvas;
        private StageOneResponsiveCanvasScaler responsiveScaler;
        private RectTransform safeArea;
        private RectTransform panelRoot;
        private CanvasGroup panelCanvasGroup;
        private Text nameText;
        private Text levelTypeText;
        private Text rankText;
        private Image rankBackground;
        private Button closeButton;
        private Text overviewTitleText;
        private Text descriptionText;
        private Text healthTitleText;
        private Text healthText;
        private Image healthFill;
        private Text shieldText;
        private Text combatTitleText;
        private Text primaryStatsText;
        private Text resistanceTitleText;
        private Text resistanceStatsText;
        private Text identityTitleText;
        private Text identityStatsText;
        private Text bossAbilityText;
        private Text statusHeaderText;
        private Text emptyStatusText;
        private ScrollRect scrollRect;
        private RectTransform statusContent;
        private bool built;
        private bool visible;
        private StageOneEnemyInspectionModel currentModel;
        private Vector2 focusViewportAnchor =
            EnemyViewportAnchor;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private Rect lastSafeArea;

        public event Action CloseRequested;

        public Canvas Canvas => canvas;
        public RectTransform PanelRoot => panelRoot;
        public Text NameText => nameText;
        public Text LevelTypeText => levelTypeText;
        public Text RankText => rankText;
        public Text OverviewTitleText => overviewTitleText;
        public Text HealthTitleText => healthTitleText;
        public Text CombatTitleText => combatTitleText;
        public Text ResistanceTitleText => resistanceTitleText;
        public Text IdentityTitleText => identityTitleText;
        public Text HealthText => healthText;
        public Image HealthFill => healthFill;
        public Text PrimaryStatsText => primaryStatsText;
        public Text ResistanceStatsText =>
            resistanceStatsText;
        public Text IdentityStatsText => identityStatsText;
        public Text StatusHeaderText => statusHeaderText;
        public Text EmptyStatusText => emptyStatusText;
        public ScrollRect ScrollRect => scrollRect;
        public RectTransform StatusContent => statusContent;
        public Button CloseButton => closeButton;
        public int VisibleStatusRowCount { get; private set; }
        public StageOneEnemyInspectionModel CurrentModel =>
            currentModel;
        public Vector2 FocusViewportAnchor =>
            focusViewportAnchor;
        public bool IsVisible =>
            visible &&
            panelRoot != null &&
            panelRoot.gameObject.activeSelf;
        public bool IsCompactLayout =>
            responsiveScaler != null &&
            responsiveScaler.IsCompactLayout;

        public static StageOneEnemyInspectionView CreateRuntime(
            StageOneUiTextCatalog textCatalog,
            Font uiFont,
            Transform parent = null)
        {
            var host = new GameObject(
                "Stage One Enemy Inspection");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            StageOneEnemyInspectionView view =
                host.AddComponent<
                    StageOneEnemyInspectionView>();
            view.catalog = textCatalog ??
                StageOneUiTextCatalog.FromJson(null);
            view.font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            view.BuildInterface();
            view.ApplyStaticLocalization();
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
            ApplyStaticLocalization();
        }

        private void Update()
        {
            if (built &&
                (lastScreenWidth != Screen.width ||
                 lastScreenHeight != Screen.height ||
                 lastSafeArea != Screen.safeArea))
            {
                ApplyResponsiveLayout();
            }
        }

        private void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(
                    HandleCloseClicked);
            }
        }

        public void Show(StageOneEnemyInspectionModel model)
        {
            if (model == null)
            {
                Hide();
                return;
            }

            BuildInterface();
            bool resetScroll =
                !IsVisible ||
                currentModel == null ||
                currentModel.EntityId != model.EntityId;
            bool statusStructureChanged =
                resetScroll ||
                HasStatusStructureChanged(
                    currentModel == null
                        ? null
                        : currentModel.Statuses,
                    model.Statuses);
            currentModel = model;
            visible = true;
            panelRoot.gameObject.SetActive(true);
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
            Render(
                model,
                statusStructureChanged,
                resetScroll);
            if (resetScroll)
            {
                RefreshFocusViewportAnchor();
            }
        }

        public void Hide()
        {
            visible = false;
            currentModel = null;
            VisibleStatusRowCount = 0;
            for (int i = 0; i < statusRows.Count; i++)
            {
                statusRows[i].Root.SetActive(false);
            }

            if (panelRoot != null)
            {
                panelRoot.gameObject.SetActive(false);
            }
        }

        private void Render(
            StageOneEnemyInspectionModel model,
            bool statusStructureChanged,
            bool resetScroll)
        {
            float preservedScrollPosition =
                scrollRect == null
                    ? 1f
                    : scrollRect.verticalNormalizedPosition;
            nameText.text = model.Name;
            levelTypeText.text = catalog.Format(
                model.IsAlive
                    ? "enemy_inspector.level_type_format"
                    : "enemy_inspector.defeated_level_type_format",
                model.Level,
                model.TypeName);
            rankText.text = model.RankName;
            rankBackground.color =
                ResolveRankColor(model.Rank);
            descriptionText.text = string.IsNullOrEmpty(
                    model.EliteTraitSummary)
                ? model.Description
                : model.Description + "\n\n" +
                  model.EliteTraitSummary;

            double currentHealth =
                model.CurrentHealthMilli / 1000d;
            double maximumHealth =
                model.MaximumHealthMilli / 1000d;
            float healthRatio =
                model.MaximumHealthMilli <= 0
                    ? 0f
                    : Mathf.Clamp01(
                        (float)model.CurrentHealthMilli /
                        model.MaximumHealthMilli);
            healthText.text = catalog.Format(
                "enemy_inspector.health_format",
                FormatNumber(currentHealth),
                FormatNumber(maximumHealth),
                Mathf.RoundToInt(healthRatio * 100f));
            healthFill.fillAmount = healthRatio;

            bool hasShield = model.ShieldMilli > 0;
            shieldText.gameObject.SetActive(hasShield);
            shieldText.text = hasShield
                ? catalog.Format(
                    "enemy_inspector.shield_format",
                    FormatNumber(
                        model.ShieldMilli / 1000d))
                : string.Empty;

            string armor = model.Armor == model.BaseArmor
                ? model.Armor.ToString(
                    CultureInfo.InvariantCulture)
                : catalog.Format(
                    "enemy_inspector.changed_value_format",
                    model.Armor,
                    model.BaseArmor);
            primaryStatsText.text = catalog.Format(
                "enemy_inspector.primary_stats_format",
                armor,
                model.CurrentSpeedPerSecond.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture),
                FormatPercent(model.SlowBps),
                FormatPercent(model.SizeMultiplierBps));
            resistanceStatsText.text = catalog.Format(
                "enemy_inspector.resistance_stats_format",
                FormatSignedPercent(
                    model.FireResistanceBps),
                FormatSignedPercent(
                    model.PoisonResistanceBps),
                FormatControlGauge(model),
                model.RewardBudget);
            identityStatsText.text = catalog.Format(
                "enemy_inspector.identity_stats_format",
                model.EntityId,
                model.LineageId,
                model.Generation,
                model.DeathBindingCount);

            bool showBossAbility =
                !string.IsNullOrEmpty(
                    model.BossAbilityName);
            bossAbilityText.gameObject.SetActive(
                showBossAbility);
            bossAbilityText.text = showBossAbility
                ? catalog.Format(
                    "enemy_inspector.boss_ability_format",
                    model.BossAbilityName)
                : string.Empty;

            RenderStatuses(model.Statuses);
            if (statusStructureChanged)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    scrollRect.content);
            }

            scrollRect.verticalNormalizedPosition =
                resetScroll
                    ? 1f
                    : preservedScrollPosition;
        }

        private void RenderStatuses(
            StageOneEnemyStatusDisplay[] statuses)
        {
            int count = statuses == null
                ? 0
                : statuses.Length;
            EnsureStatusRows(count);
            VisibleStatusRowCount = count;
            statusHeaderText.text = catalog.Format(
                "enemy_inspector.debuffs_count_format",
                count);
            emptyStatusText.gameObject.SetActive(
                count == 0);
            emptyStatusText.text = count == 0
                ? catalog.Get(
                    "enemy_inspector.no_debuffs")
                : string.Empty;

            for (int i = 0; i < statusRows.Count; i++)
            {
                bool active = i < count;
                StatusRow row = statusRows[i];
                row.Root.SetActive(active);
                if (!active)
                {
                    continue;
                }

                StageOneEnemyStatusDisplay status =
                    statuses[i];
                row.Name.text = status.Name;
                row.Summary.text = catalog.Format(
                    "enemy_inspector.debuff_summary_format",
                    status.Stacks,
                    status.MaximumStacks,
                    status.RemainingSeconds.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture));
                row.Description.text =
                    status.Description;
                row.Source.text =
                    FormatStatusSource(status);

                if (StageOneCardEffectPalette.TryGetStyle(
                        status.EffectId,
                        out StageOneCardEffectStyle style))
                {
                    row.Accent.color = style.Primary;
                    row.Name.color = style.Secondary;
                }
                else
                {
                    row.Accent.color =
                        PanelBorderColor;
                    row.Name.color = TextColor;
                }
            }
        }

        private string FormatStatusSource(
            in StageOneEnemyStatusDisplay status)
        {
            builder.Length = 0;
            if (status.TickIntervalSeconds > 0f)
            {
                builder.Append(
                    catalog.Format(
                        "enemy_inspector.tick_format",
                        status.TickIntervalSeconds.ToString(
                            "0.0",
                            CultureInfo.InvariantCulture)));
            }

            if (status.Intensity != 0)
            {
                AppendSeparator(builder);
                builder.Append(
                    catalog.Format(
                        "enemy_inspector.intensity_format",
                        status.Intensity));
            }

            if (status.ArmorIgnoreBps > 0)
            {
                AppendSeparator(builder);
                builder.Append(
                    catalog.Format(
                        "enemy_inspector.armor_ignore_format",
                        FormatPercent(
                            status.ArmorIgnoreBps)));
            }

            if (!string.IsNullOrEmpty(
                    status.SourceCardName))
            {
                AppendSeparator(builder);
                builder.Append(
                    catalog.Format(
                        "enemy_inspector.source_card_format",
                        status.SourceCardName));
            }
            else if (status.SourceTowerId >= 0)
            {
                AppendSeparator(builder);
                builder.Append(
                    catalog.Format(
                        "enemy_inspector.source_tower_format",
                        status.SourceTowerId));
            }
            else if (status.SourceCount > 1)
            {
                AppendSeparator(builder);
                builder.Append(
                    catalog.Format(
                        "enemy_inspector.source_count_format",
                        status.SourceCount));
            }

            return builder.ToString();
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            var canvasObject = new GameObject(
                "Enemy Inspection Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 125;

            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1600f, 900f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            responsiveScaler =
                canvasObject.AddComponent<
                    StageOneResponsiveCanvasScaler>();

            safeArea = new GameObject(
                "Safe Area",
                typeof(RectTransform),
                typeof(StageOneSafeAreaFitter))
                .GetComponent<RectTransform>();
            safeArea.SetParent(canvasObject.transform, false);
            Stretch(safeArea);

            var panelObject = new GameObject(
                "Selected Enemy Information",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            panelObject.transform.SetParent(safeArea, false);
            panelRoot =
                panelObject.GetComponent<RectTransform>();
            panelRoot.anchorMin = new Vector2(1f, 0f);
            panelRoot.anchorMax = new Vector2(1f, 1f);
            panelRoot.pivot = new Vector2(1f, 0.5f);
            panelRoot.sizeDelta =
                new Vector2(
                    DesktopPanelWidth,
                    -OuterMargin * 2f);
            panelRoot.anchoredPosition =
                new Vector2(-OuterMargin, 0f);
            RuleforgePixelUi.ApplyPanel(
                panelObject.GetComponent<Image>(),
                RuleforgePixelPanelRole.Workbench,
                PanelColor);
            panelCanvasGroup =
                panelObject.GetComponent<CanvasGroup>();

            RectTransform accent = new GameObject(
                    "Red Selection Accent",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image))
                .GetComponent<RectTransform>();
            accent.SetParent(panelRoot, false);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.sizeDelta = new Vector2(4f, 0f);
            accent.anchoredPosition = Vector2.zero;
            accent.GetComponent<Image>().color =
                PanelBorderColor;

            BuildHeader();
            BuildScrollableBody();
            ApplyResponsiveLayout();
            panelRoot.gameObject.SetActive(false);
            built = true;
        }

        private void ApplyResponsiveLayout()
        {
            if (panelRoot == null)
            {
                return;
            }

            if (responsiveScaler != null)
            {
                responsiveScaler.ApplyScale();
            }

            ApplyPanelLayout(
                responsiveScaler != null &&
                responsiveScaler.IsCompactLayout);
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = Screen.safeArea;
            RefreshFocusViewportAnchor();
        }

        /// <summary>
        /// Applies the same responsive contract with explicit viewport
        /// values. Device simulation and PlayMode layout tests use this path
        /// without introducing a second set of panel rules.
        /// </summary>
        public void ApplyResponsiveLayout(
            int screenWidth,
            int screenHeight,
            bool handheld)
        {
            if (panelRoot == null)
            {
                return;
            }

            if (responsiveScaler != null)
            {
                responsiveScaler.ApplyScale(
                    screenWidth,
                    screenHeight,
                    handheld);
            }

            ApplyPanelLayout(
                responsiveScaler != null &&
                responsiveScaler.IsCompactLayout);
            lastScreenWidth = Mathf.Max(1, screenWidth);
            lastScreenHeight = Mathf.Max(1, screenHeight);
            lastSafeArea = Screen.safeArea;
            RefreshFocusViewportAnchor();
        }

        private void ApplyPanelLayout(bool compact)
        {
            if (compact)
            {
                panelRoot.anchorMin = Vector2.zero;
                panelRoot.anchorMax = new Vector2(
                    1f,
                    CompactPanelHeightRatio);
                panelRoot.pivot = new Vector2(0.5f, 0.5f);
                panelRoot.offsetMin = new Vector2(
                    OuterMargin,
                    OuterMargin);
                panelRoot.offsetMax = new Vector2(
                    -OuterMargin,
                    -OuterMargin);
                return;
            }

            panelRoot.anchorMin = new Vector2(1f, 0f);
            panelRoot.anchorMax = Vector2.one;
            panelRoot.pivot = new Vector2(1f, 0.5f);
            panelRoot.offsetMin = new Vector2(
                -OuterMargin - DesktopPanelWidth,
                OuterMargin);
            panelRoot.offsetMax = new Vector2(
                -OuterMargin,
                -DesktopTopInset);
        }

        private void RefreshFocusViewportAnchor()
        {
            if (panelRoot == null ||
                Screen.width <= 0 ||
                Screen.height <= 0)
            {
                focusViewportAnchor =
                    EnemyViewportAnchor;
                return;
            }

            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            panelRoot.GetWorldCorners(corners);
            Rect safe = Screen.safeArea;
            if (IsCompactLayout)
            {
                float panelTop = Mathf.Clamp(
                    corners[1].y,
                    safe.yMin,
                    safe.yMax);
                bool portrait =
                    responsiveScaler != null &&
                    responsiveScaler.IsPortraitLayout;
                float referenceHeight = portrait
                    ? StageOneResponsiveCanvasScaler
                        .CompactPortraitReferenceResolution.y
                    : StageOneResponsiveCanvasScaler
                        .CompactLandscapeReferenceResolution.y;
                float topReservedRatio =
                    StageOneHudLayoutMetrics
                        .GetTopOccupiedHeight(portrait) /
                    Mathf.Max(1f, referenceHeight);
                float usableTop = Mathf.Clamp(
                    safe.yMax -
                    safe.height * topReservedRatio,
                    panelTop,
                    safe.yMax);
                if (usableTop <= panelTop + 1f)
                {
                    usableTop = safe.yMax;
                }

                focusViewportAnchor = new Vector2(
                    Mathf.Clamp01(
                        safe.center.x /
                        Screen.width),
                    Mathf.Clamp01(
                        (panelTop + usableTop) * 0.5f /
                        Screen.height));
                return;
            }

            float visibleRight = Mathf.Clamp(
                corners[0].x,
                safe.xMin,
                safe.xMax);
            float availableWidth =
                visibleRight - safe.xMin;
            float leftCenterX =
                safe.xMin +
                Mathf.Max(1f, availableWidth) * 0.5f;
            float preferredX =
                EnemyViewportAnchor.x * Screen.width;
            focusViewportAnchor = new Vector2(
                Mathf.Clamp01(
                    Mathf.Min(
                        preferredX,
                        leftCenterX) /
                    Screen.width),
                Mathf.Clamp01(
                    (corners[0].y + corners[1].y) * 0.5f /
                    Screen.height));
        }

        private static bool HasStatusStructureChanged(
            StageOneEnemyStatusDisplay[] previous,
            StageOneEnemyStatusDisplay[] next)
        {
            int previousCount =
                previous == null ? 0 : previous.Length;
            int nextCount =
                next == null ? 0 : next.Length;
            if (previousCount != nextCount)
            {
                return true;
            }

            for (int i = 0; i < previousCount; i++)
            {
                if (!string.Equals(
                        previous[i].EffectId,
                        next[i].EffectId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildHeader()
        {
            RectTransform header = new GameObject(
                    "Enemy Header",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image))
                .GetComponent<RectTransform>();
            header.SetParent(panelRoot, false);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, HeaderHeight);
            header.anchoredPosition = Vector2.zero;
            header.GetComponent<Image>().color =
                new Color32(59, 37, 24, 255);

            nameText = CreateText(
                "Enemy Name",
                header,
                28,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            Anchor(
                nameText.rectTransform,
                new Vector2(0f, 0.48f),
                new Vector2(1f, 1f),
                new Vector2(18f, 0f),
                new Vector2(-66f, -4f));

            levelTypeText = CreateText(
                "Level And Type",
                header,
                16,
                FontStyle.Normal,
                MutedTextColor,
                TextAnchor.MiddleLeft);
            Anchor(
                levelTypeText.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0.68f, 0.48f),
                new Vector2(18f, 5f),
                new Vector2(0f, 0f));

            RectTransform rankRoot = new GameObject(
                    "Rank Badge",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image))
                .GetComponent<RectTransform>();
            rankRoot.SetParent(header, false);
            rankRoot.anchorMin = new Vector2(0.68f, 0.08f);
            rankRoot.anchorMax = new Vector2(1f, 0.42f);
            rankRoot.offsetMin = new Vector2(0f, 0f);
            rankRoot.offsetMax = new Vector2(-54f, 0f);
            rankBackground = rankRoot.GetComponent<Image>();
            rankText = CreateText(
                "Rank",
                rankRoot,
                15,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleCenter);
            Stretch(rankText.rectTransform, 5f, 2f);

            closeButton = CreateButton(
                "Close Enemy Information",
                header,
                new Color32(80, 42, 45, 255),
                out Text closeLabel);
            RectTransform closeRect =
                closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(44f, 44f);
            closeRect.anchoredPosition =
                new Vector2(-8f, -8f);
            closeLabel.text = "×";
            closeLabel.fontSize = 24;
            closeButton.onClick.AddListener(
                HandleCloseClicked);
        }

        private void BuildScrollableBody()
        {
            RectTransform viewport = new GameObject(
                    "Information Viewport",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Mask))
                .GetComponent<RectTransform>();
            viewport.SetParent(panelRoot, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin =
                new Vector2(10f, 12f);
            viewport.offsetMax =
                new Vector2(-10f, -HeaderHeight - 8f);
            Image viewportImage =
                viewport.GetComponent<Image>();
            viewportImage.color =
                new Color(0f, 0f, 0f, 0.01f);
            viewport.GetComponent<Mask>()
                .showMaskGraphic = false;

            statusContent = new GameObject(
                    "Enemy Information Content",
                    typeof(RectTransform),
                    typeof(VerticalLayoutGroup),
                    typeof(ContentSizeFitter))
                .GetComponent<RectTransform>();
            statusContent.SetParent(viewport, false);
            statusContent.anchorMin = new Vector2(0f, 1f);
            statusContent.anchorMax = new Vector2(1f, 1f);
            statusContent.pivot = new Vector2(0.5f, 1f);
            statusContent.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout =
                statusContent.GetComponent<
                    VerticalLayoutGroup>();
            layout.padding =
                new RectOffset(8, 8, 8, 16);
            layout.spacing = SectionSpacing;
            layout.childAlignment =
                TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter =
                statusContent.GetComponent<
                    ContentSizeFitter>();
            fitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            scrollRect = panelRoot.gameObject.AddComponent<
                ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = statusContent;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 26f;

            BuildDescriptionSection();
            BuildHealthSection();
            primaryStatsText = CreateTextSection(
                "Combat Statistics",
                "enemy_inspector.combat_title",
                104f,
                out combatTitleText);
            resistanceStatsText = CreateTextSection(
                "Resistance Statistics",
                "enemy_inspector.resistance_title",
                104f,
                out resistanceTitleText);
            identityStatsText = CreateTextSection(
                "Identity Statistics",
                "enemy_inspector.identity_title",
                104f,
                out identityTitleText);
            bossAbilityText = CreateText(
                "Boss Ability",
                statusContent,
                17,
                FontStyle.Bold,
                new Color32(255, 185, 128, 255),
                TextAnchor.MiddleLeft);
            AddLayoutElement(
                bossAbilityText.gameObject,
                46f);
            bossAbilityText.rectTransform.offsetMin =
                new Vector2(12f, 0f);
            bossAbilityText.rectTransform.offsetMax =
                new Vector2(-12f, 0f);

            statusHeaderText = CreateText(
                "Active Debuffs Header",
                statusContent,
                21,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            AddLayoutElement(
                statusHeaderText.gameObject,
                38f);
            statusHeaderText.text =
                catalog.Get(
                    "enemy_inspector.debuffs_title");

            emptyStatusText = CreateText(
                "No Active Debuffs",
                statusContent,
                16,
                FontStyle.Italic,
                MutedTextColor,
                TextAnchor.MiddleCenter);
            AddLayoutElement(
                emptyStatusText.gameObject,
                64f);
        }

        private void BuildDescriptionSection()
        {
            RectTransform section = CreateSection(
                "Enemy Overview",
                104f);
            overviewTitleText = CreateText(
                "Overview Title",
                section,
                15,
                FontStyle.Bold,
                new Color32(255, 160, 136, 255),
                TextAnchor.MiddleLeft);
            Anchor(
                overviewTitleText.rectTransform,
                new Vector2(0f, 0.68f),
                new Vector2(1f, 1f),
                new Vector2(14f, 0f),
                new Vector2(-14f, 0f));
            overviewTitleText.text =
                catalog.Get(
                    "enemy_inspector.overview_title");

            descriptionText = CreateText(
                "Enemy Description",
                section,
                16,
                FontStyle.Normal,
                TextColor,
                TextAnchor.UpperLeft);
            Anchor(
                descriptionText.rectTransform,
                Vector2.zero,
                new Vector2(1f, 0.7f),
                new Vector2(14f, 10f),
                new Vector2(-14f, 0f));
            descriptionText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow =
                VerticalWrapMode.Truncate;
        }

        private void BuildHealthSection()
        {
            RectTransform section = CreateSection(
                "Health",
                100f);
            healthTitleText = CreateText(
                "Health Title",
                section,
                16,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            Anchor(
                healthTitleText.rectTransform,
                new Vector2(0f, 0.64f),
                new Vector2(1f, 1f),
                new Vector2(14f, 0f),
                new Vector2(-14f, 0f));
            healthTitleText.text =
                catalog.Get(
                    "enemy_inspector.health_title");

            RectTransform barBackground = new GameObject(
                    "Health Bar Background",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image))
                .GetComponent<RectTransform>();
            barBackground.SetParent(section, false);
            Anchor(
                barBackground,
                new Vector2(0f, 0.18f),
                new Vector2(1f, 0.58f),
                new Vector2(14f, 0f),
                new Vector2(-14f, 0f));
            barBackground.GetComponent<Image>().color =
                HealthBackgroundColor;

            RectTransform fill = new GameObject(
                    "Current Health Fill",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image))
                .GetComponent<RectTransform>();
            fill.SetParent(barBackground, false);
            Stretch(fill);
            healthFill = fill.GetComponent<Image>();
            healthFill.color = HealthFillColor;
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod =
                Image.FillMethod.Horizontal;
            healthFill.fillOrigin = 0;

            healthText = CreateText(
                "Health Value",
                barBackground,
                16,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleCenter);
            Stretch(healthText.rectTransform, 4f, 2f);

            shieldText = CreateText(
                "Shield Value",
                section,
                14,
                FontStyle.Bold,
                new Color32(127, 210, 255, 255),
                TextAnchor.MiddleRight);
            Anchor(
                shieldText.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.18f),
                new Vector2(14f, 0f),
                new Vector2(-14f, 0f));
        }

        private Text CreateTextSection(
            string objectName,
            string titleKey,
            float height,
            out Text title)
        {
            RectTransform section =
                CreateSection(objectName, height);
            title = CreateText(
                objectName + " Title",
                section,
                15,
                FontStyle.Bold,
                new Color32(255, 160, 136, 255),
                TextAnchor.MiddleLeft);
            Anchor(
                title.rectTransform,
                new Vector2(0f, 0.7f),
                new Vector2(1f, 1f),
                new Vector2(14f, 0f),
                new Vector2(-14f, 0f));
            title.text = catalog.Get(titleKey);

            Text value = CreateText(
                objectName + " Values",
                section,
                16,
                FontStyle.Normal,
                TextColor,
                TextAnchor.UpperLeft);
            Anchor(
                value.rectTransform,
                Vector2.zero,
                new Vector2(1f, 0.72f),
                new Vector2(14f, 8f),
                new Vector2(-14f, 0f));
            value.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            value.verticalOverflow =
                VerticalWrapMode.Truncate;
            return value;
        }

        private void ApplyStaticLocalization()
        {
            if (!built || catalog == null)
            {
                return;
            }

            SetText(
                overviewTitleText,
                "enemy_inspector.overview_title");
            SetText(
                healthTitleText,
                "enemy_inspector.health_title");
            SetText(
                combatTitleText,
                "enemy_inspector.combat_title");
            SetText(
                resistanceTitleText,
                "enemy_inspector.resistance_title");
            SetText(
                identityTitleText,
                "enemy_inspector.identity_title");
            SetText(
                statusHeaderText,
                "enemy_inspector.debuffs_title");
        }

        private void SetText(Text target, string key)
        {
            if (target != null)
            {
                target.text = catalog.Get(key);
            }
        }

        private RectTransform CreateSection(
            string objectName,
            float preferredHeight)
        {
            RectTransform section = new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(LayoutElement))
                .GetComponent<RectTransform>();
            section.SetParent(statusContent, false);
            section.GetComponent<Image>().color =
                SectionColor;
            AddLayoutElement(
                section.gameObject,
                preferredHeight);
            return section;
        }

        private void EnsureStatusRows(int count)
        {
            while (statusRows.Count < count)
            {
                statusRows.Add(CreateStatusRow(
                    statusRows.Count));
            }
        }

        private StatusRow CreateStatusRow(int index)
        {
            RectTransform root = CreateSection(
                "Debuff " + (index + 1),
                StatusRowHeight);
            Image accent = new GameObject(
                    "Accent",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image))
                .GetComponent<Image>();
            accent.transform.SetParent(root, false);
            RectTransform accentRect =
                accent.rectTransform;
            accentRect.anchorMin = Vector2.zero;
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(5f, 0f);
            accentRect.anchoredPosition = Vector2.zero;

            Text name = CreateText(
                "Name",
                root,
                18,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleLeft);
            Anchor(
                name.rectTransform,
                new Vector2(0f, 0.7f),
                new Vector2(0.54f, 1f),
                new Vector2(16f, 0f),
                Vector2.zero);

            Text summary = CreateText(
                "Summary",
                root,
                14,
                FontStyle.Bold,
                MutedTextColor,
                TextAnchor.MiddleRight);
            Anchor(
                summary.rectTransform,
                new Vector2(0.48f, 0.7f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(-12f, 0f));

            Text description = CreateText(
                "Description",
                root,
                14,
                FontStyle.Normal,
                TextColor,
                TextAnchor.UpperLeft);
            Anchor(
                description.rectTransform,
                new Vector2(0f, 0.24f),
                new Vector2(1f, 0.72f),
                new Vector2(16f, 0f),
                new Vector2(-12f, 0f));
            description.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            description.verticalOverflow =
                VerticalWrapMode.Truncate;

            Text source = CreateText(
                "Details And Source",
                root,
                13,
                FontStyle.Normal,
                MutedTextColor,
                TextAnchor.MiddleLeft);
            Anchor(
                source.rectTransform,
                Vector2.zero,
                new Vector2(1f, 0.27f),
                new Vector2(16f, 0f),
                new Vector2(-12f, 0f));

            return new StatusRow
            {
                Root = root.gameObject,
                Accent = accent,
                Name = name,
                Summary = summary,
                Description = description,
                Source = source
            };
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            Color color,
            out Text label)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            host.transform.SetParent(parent, false);
            Image image = host.GetComponent<Image>();
            image.color = color;
            Button button = host.GetComponent<Button>();
            button.targetGraphic = image;

            label = CreateText(
                "Label",
                host.transform,
                18,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            RuleforgePixelUi.Apply(
                button,
                RuleforgePixelButtonRole.Danger);
            return button;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            host.transform.SetParent(parent, false);
            Text text = host.GetComponent<Text>();
            RuleforgeUiTypography.Configure(
                text,
                font,
                fontSize,
                color,
                alignment,
                RuleforgeUiTypography.IsLight(color));
            return text;
        }

        private static void AddLayoutElement(
            GameObject target,
            float preferredHeight)
        {
            LayoutElement element =
                target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element =
                    target.AddComponent<LayoutElement>();
            }

            element.minHeight = preferredHeight;
            element.preferredHeight = preferredHeight;
            element.flexibleHeight = 0f;
        }

        private static void Anchor(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Stretch(
            RectTransform rect,
            float horizontalInset = 0f,
            float verticalInset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin =
                new Vector2(
                    horizontalInset,
                    verticalInset);
            rect.offsetMax =
                new Vector2(
                    -horizontalInset,
                    -verticalInset);
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

        private string FormatControlGauge(
            StageOneEnemyInspectionModel model)
        {
            return model.Rank == EnemyRank.Normal
                ? catalog.Get(
                    "enemy_inspector.control_not_applicable")
                : catalog.Format(
                    "enemy_inspector.control_gauge_format",
                    model.ControlGauge,
                    model.ControlThreshold);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString(
                value >= 100d ? "0" : "0.#",
                CultureInfo.InvariantCulture);
        }

        private static string FormatPercent(int basisPoints)
        {
            return (basisPoints / 100f).ToString(
                       "0.#",
                       CultureInfo.InvariantCulture) +
                   "%";
        }

        private static string FormatSignedPercent(
            int basisPoints)
        {
            float percentage = basisPoints / 100f;
            return percentage.ToString(
                       percentage > 0f
                           ? "+0.#;-0.#;0"
                           : "0.#;-0.#;0",
                       CultureInfo.InvariantCulture) +
                   "%";
        }

        private static Color ResolveRankColor(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.Elite:
                    return EliteRankColor;
                case EnemyRank.Boss:
                    return BossRankColor;
                default:
                    return NormalRankColor;
            }
        }

        private static void AppendSeparator(
            StringBuilder target)
        {
            if (target.Length > 0)
            {
                target.Append("  ·  ");
            }
        }

        private void HandleCloseClicked()
        {
            CloseRequested?.Invoke();
        }
    }
}
