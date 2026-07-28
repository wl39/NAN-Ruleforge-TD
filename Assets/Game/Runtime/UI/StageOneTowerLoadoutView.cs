using System;
using System.Collections;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.Towers.Archer;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    public readonly struct StageOneLoadoutCard
    {
        public StageOneLoadoutCard(
            int instanceId,
            StageOneCardDisplay display,
            bool equipped,
            bool equippedOnSelectedTower)
        {
            InstanceId = instanceId;
            Display = display;
            Equipped = equipped;
            EquippedOnSelectedTower = equippedOnSelectedTower;
        }

        public int InstanceId { get; }
        public StageOneCardDisplay Display { get; }
        public bool Equipped { get; }
        public bool EquippedOnSelectedTower { get; }
    }

    /// <summary>
    /// Blueprint workbench shown while a tower is selected. The left side is
    /// reserved for a large composite preview of the selected tower. The right
    /// side expresses the tower rule as vertically ordered, level-gated slots
    /// with their resolved descriptions and an owned-card inventory below.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneTowerLoadoutView : MonoBehaviour
    {
        private const int MaximumVisibleCards = 16;
        private const int MaximumSlots = 3;
        private const int LandscapeInventoryColumns = 6;
        private const int PortraitInventoryColumns = 2;
        private const float LandscapeWidth = 1480f;
        private const float LandscapeHeight = 820f;
        private const float PortraitWidth = 760f;
        private const float PortraitHeight = 1320f;
        private const float InventoryCardWidth = 146f;
        private const float InventoryCardHeight = 190f;
        private const float InventoryCardGap = 12f;
        private const float BlueprintTransitionDuration = 0.26f;
        private const float SlotDoubleClickWindowSeconds = 0.65f;

        private static readonly Color BlueprintRaised =
            new Color32(22, 58, 91, 248);
        private static readonly Color BlueprintLine =
            new Color32(91, 157, 192, 180);
        private static readonly Color ButtonColor =
            new Color32(35, 71, 99, 248);
        private static readonly Color ActiveColor =
            new Color32(242, 190, 62, 255);
        private static readonly Color ProjectileColor =
            new Color32(211, 105, 37, 255);
        private static readonly Color EnemyColor =
            new Color32(182, 48, 56, 255);
        private static readonly Color LockedColor =
            new Color32(35, 47, 57, 236);
        private static readonly Color LockedDescriptionColor =
            new Color32(26, 43, 56, 235);
        private static readonly Color TextColor =
            new Color32(247, 247, 228, 255);
        private static readonly Color MutedTextColor =
            new Color32(181, 205, 215, 255);
        private static readonly Color ScrollbarColor =
            new Color32(7, 24, 42, 220);

        private sealed class TowerPreviewBinding
        {
            public SpriteRenderer Source;
            public Image Image;
        }

        private StageOneUiTextCatalog catalog;
        private Font font;
        private Canvas canvas;
        private RectTransform blueprintBackdropRoot;
        private StageOneBlueprintGridGraphic backdropGraphic;
        private RectTransform panelRoot;
        private CanvasGroup panelCanvasGroup;
        private Image settingsTintImage;
        private RectTransform towerPreviewFrame;
        private RectTransform towerPreviewContent;
        private Text towerPreviewPlaceholder;
        private Text titleText;
        private Text sectionTitleText;
        private Text effectTitleText;
        private Text targetTitleText;
        private Text instructionText;
        private Text inventoryTitleText;
        private Button upgradeButton;
        private Text upgradeLabel;
        private Button closeButton;
        private ScrollRect cardScrollRect;
        private RectTransform cardViewport;
        private RectTransform cardContent;
        private GridLayoutGroup cardGridLayout;
        private RectTransform scrollbarRect;
        private Image effectBackplate;
        private readonly Button[] slotButtons =
            new Button[MaximumSlots];
        private readonly Text[] slotLabels =
            new Text[MaximumSlots];
        private readonly Image[] slotDescriptionBackplates =
            new Image[MaximumSlots];
        private readonly Text[] slotDescriptionTexts =
            new Text[MaximumSlots];
        private readonly Button[] slotProjectileButtons =
            new Button[MaximumSlots];
        private readonly Image[] slotProjectileArtworks =
            new Image[MaximumSlots];
        private readonly Text[] slotProjectileFallbacks =
            new Text[MaximumSlots];
        private readonly Button[] slotEnemyButtons =
            new Button[MaximumSlots];
        private readonly Image[] slotEnemyArtworks =
            new Image[MaximumSlots];
        private readonly Text[] slotEnemyFallbacks =
            new Text[MaximumSlots];
        private readonly Image[] slotSubjectAccentLines =
            new Image[MaximumSlots];
        private readonly StageOneSlotDoubleClickRelay[]
            slotDoubleClickRelays =
                new StageOneSlotDoubleClickRelay[MaximumSlots];
        private readonly bool[] slotHasCards =
            new bool[MaximumSlots];
        private readonly float[] lastSlotClickTimes =
        {
            float.NegativeInfinity,
            float.NegativeInfinity,
            float.NegativeInfinity
        };
        private readonly SubjectType[] slotSubjectTypes =
            new SubjectType[MaximumSlots];
        private readonly StageOneCardDropSlot[] slotDropTargets =
            new StageOneCardDropSlot[MaximumSlots];
        private readonly StageOneCardView[] cardViews =
            new StageOneCardView[MaximumVisibleCards];
        private readonly StageOneCardDragSource[] cardDragSources =
            new StageOneCardDragSource[MaximumVisibleCards];
        private readonly int[] visibleCardInstanceIds =
            new int[MaximumVisibleCards];
        private readonly int[] visibleCardSlotIndices =
            new int[MaximumVisibleCards];
        private readonly int[] presentedSlotCardInstanceIds =
        {
            -1,
            -1,
            -1
        };
        private readonly List<StageOneLoadoutCard> presentedCards =
            new List<StageOneLoadoutCard>(MaximumVisibleCards);
        private readonly List<TowerPreviewBinding> previewBindings =
            new List<TowerPreviewBinding>(16);
        private Transform towerPreviewSource;
        private ArcherTowerView towerPreviewAnimator;
        private int presentedUnlockedSlotCount;
        private int selectedSlot;
        private int visibleCardCount;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private bool portraitLayout;
        private bool editable;
        private bool built;
        private bool hasEverShown;
        private bool transitionRunning;
        private bool transitionShowing;
        private Coroutine transitionRoutine;
        private Sprite projectileSubjectSprite;
        private Sprite enemySubjectSprite;

        public event Action<int> SlotRequested;
        public event Action<int> CardRequested;
        public event Action<int, int> CardDropped;
        public event Action UpgradeRequested;
        public event Action<SubjectType> SubjectTypeRequested;
        public event Action<int, SubjectType>
            SlotSubjectTypeRequested;
        public event Action<int> SlotUnequipRequested;
        public event Action CloseRequested;

        public bool IsVisible =>
            blueprintBackdropRoot != null &&
            blueprintBackdropRoot.gameObject.activeSelf;
        public Button UpgradeButton => upgradeButton;
        public Button ProjectileButton =>
            slotProjectileButtons[
                Mathf.Clamp(
                    selectedSlot,
                    0,
                    MaximumSlots - 1)];
        public Button EnemyButton =>
            slotEnemyButtons[
                Mathf.Clamp(
                    selectedSlot,
                    0,
                    MaximumSlots - 1)];
        public Button CloseButton => closeButton;
        public Text TitleText => titleText;
        public int SelectedSlot => selectedSlot;
        public int VisibleCardCount => visibleCardCount;
        public ScrollRect CardScrollRect => cardScrollRect;
        public RectTransform TowerPreviewContent =>
            towerPreviewContent;
        public Transform TowerPreviewSource =>
            towerPreviewSource;
        public StageOneBlueprintGridGraphic BlueprintGraphic =>
            backdropGraphic;
        public StageOneBlueprintGridGraphic BackdropGraphic =>
            backdropGraphic;
        public Image SettingsTintImage => settingsTintImage;
        public Image EffectBackplate => effectBackplate;
        public Text CurrentEffectText => instructionText;
        public Image ProjectileArtwork =>
            slotProjectileArtworks[
                Mathf.Clamp(
                    selectedSlot,
                    0,
                    MaximumSlots - 1)];
        public Image EnemyArtwork =>
            slotEnemyArtworks[
                Mathf.Clamp(
                    selectedSlot,
                    0,
                    MaximumSlots - 1)];
        public bool IsPortraitLayout => portraitLayout;
        public bool IsTransitionRunning => transitionRunning;
        public float BlueprintRevealProgress =>
            backdropGraphic == null
                ? 0f
                : backdropGraphic.RevealProgress;

        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                Hide();
                return;
            }

            if (!built || !hasEverShown || IsVisible)
            {
                return;
            }

            BeginBlueprintTransition(true);
        }

        public static StageOneTowerLoadoutView CreateRuntime(
            StageOneUiTextCatalog textCatalog,
            Font uiFont,
            Transform parent)
        {
            var host = new GameObject("Stage One Tower Loadout");
            host.transform.SetParent(parent, false);
            StageOneTowerLoadoutView view =
                host.AddComponent<StageOneTowerLoadoutView>();
            view.catalog = textCatalog ??
                StageOneUiTextCatalog.FromJson(null);
            view.font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            view.BuildInterface();
            view.Hide();
            return view;
        }

        public void Show(
            string towerName,
            int level,
            SubjectType subjectType,
            int unlockedSlotCount,
            int[] slotCardInstanceIds,
            IReadOnlyList<StageOneLoadoutCard> cards,
            int activeSlot,
            bool canEdit,
            int upgradeCost = -1,
            bool canAffordUpgrade = true)
        {
            var subjects = new SubjectType[MaximumSlots];
            for (int slot = 0; slot < subjects.Length; slot++)
            {
                subjects[slot] = subjectType;
            }

            Show(
                towerName,
                level,
                subjects,
                unlockedSlotCount,
                slotCardInstanceIds,
                cards,
                activeSlot,
                canEdit,
                upgradeCost,
                canAffordUpgrade);
        }

        /// <summary>
        /// Preferred per-slot overload. Each card slot owns an independent
        /// interpretation target, while the single-subject overload remains
        /// available for older controller and test callers.
        /// </summary>
        public void Show(
            string towerName,
            int level,
            IReadOnlyList<SubjectType> subjects,
            int unlockedSlotCount,
            int[] slotCardInstanceIds,
            IReadOnlyList<StageOneLoadoutCard> cards,
            int activeSlot,
            bool canEdit,
            int upgradeCost = -1,
            bool canAffordUpgrade = true)
        {
            BuildInterface();
            bool alreadyOpen = IsVisible;
            float preservedInventoryScroll =
                cardScrollRect != null
                    ? cardScrollRect.verticalNormalizedPosition
                    : 1f;
            editable = canEdit;
            selectedSlot = Mathf.Clamp(
                activeSlot,
                0,
                Mathf.Max(0, unlockedSlotCount - 1));
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                slotSubjectTypes[slot] =
                    subjects != null &&
                    slot < subjects.Count &&
                    subjects[slot] == SubjectType.Enemy
                        ? SubjectType.Enemy
                        : SubjectType.Projectile;
            }
            CachePresentedLoadout(
                unlockedSlotCount,
                slotCardInstanceIds,
                cards);

            blueprintBackdropRoot.gameObject.SetActive(true);
            panelRoot.gameObject.SetActive(true);
            titleText.text = catalog.Format(
                "tower_panel.title_format",
                towerName,
                Mathf.Clamp(level, 1, 7));

            inventoryTitleText.text =
                catalog.Get("tower_panel.inventory");

            upgradeButton.interactable =
                editable &&
                level < 7 &&
                canAffordUpgrade;
            upgradeLabel.text = level >= 7
                ? catalog.Get("tower_panel.max_level")
                : upgradeCost >= 0
                    ? catalog.Format(
                        "tower_panel.level_up_cost_format",
                        upgradeCost)
                    : catalog.Get("tower_panel.level_up");
            ApplySelectedSubjectVisuals();

            RefreshSlots(
                unlockedSlotCount,
                slotCardInstanceIds,
                cards);
            RefreshActiveEffectSummary();
            RefreshCards(
                cards,
                slotCardInstanceIds,
                slotSubjectTypes[selectedSlot]);
            RefreshResponsiveScale(true);
            cardScrollRect.StopMovement();
            cardScrollRect.verticalNormalizedPosition =
                alreadyOpen
                    ? preservedInventoryScroll
                    : 1f;
            RefreshTowerPreviewVisuals();
            if (!alreadyOpen ||
                (transitionRunning && !transitionShowing))
            {
                BeginBlueprintTransition(true);
            }
            else if (!transitionRunning)
            {
                SetTransitionState(1f, true);
            }

            hasEverShown = true;
        }

        public void Hide()
        {
            if (panelRoot == null ||
                blueprintBackdropRoot == null)
            {
                return;
            }

            if (!hasEverShown ||
                !Application.isPlaying ||
                !blueprintBackdropRoot.gameObject.activeSelf)
            {
                HideImmediately();
                return;
            }

            if (transitionRunning && !transitionShowing)
            {
                return;
            }

            BeginBlueprintTransition(false);
        }

        /// <summary>
        /// Supplies the live tower hierarchy used to build the large left-side
        /// preview. Every active SpriteRenderer is composited, so multi-part
        /// towers and their current animation frame remain recognizable.
        /// </summary>
        public void SetTowerPreview(Transform towerRoot)
        {
            BuildInterface();
            if (towerPreviewSource == towerRoot &&
                previewBindings.Count > 0)
            {
                SetTowerPreviewAnimationEnabled(IsVisible);
                RefreshTowerPreviewVisuals();
                return;
            }

            SetTowerPreviewAnimationEnabled(false);
            towerPreviewSource = towerRoot;
            towerPreviewAnimator = towerPreviewSource == null
                ? null
                : towerPreviewSource.GetComponent<
                    ArcherTowerView>();
            if (towerPreviewAnimator == null &&
                towerPreviewSource != null)
            {
                towerPreviewAnimator =
                    towerPreviewSource.GetComponentInChildren<
                        ArcherTowerView>(true);
            }

            SetTowerPreviewAnimationEnabled(IsVisible);
            RebuildTowerPreview();
        }

        /// <summary>
        /// Returns the UI sprite currently mirroring a source renderer. This
        /// also provides a lightweight visual-state probe for browser and
        /// PlayMode verification.
        /// </summary>
        public Sprite GetTowerPreviewSprite(
            SpriteRenderer sourceRenderer)
        {
            for (int i = 0; i < previewBindings.Count; i++)
            {
                TowerPreviewBinding binding =
                    previewBindings[i];
                if (binding.Source == sourceRenderer &&
                    binding.Image != null)
                {
                    return binding.Image.sprite;
                }
            }

            return null;
        }

        /// <summary>
        /// Final art can be injected without changing the subject-toggle
        /// layout. Null sprites keep the readable text placeholders.
        /// </summary>
        public void SetSubjectArtworkSprites(
            Sprite projectileSprite,
            Sprite enemySprite)
        {
            BuildInterface();
            projectileSubjectSprite = projectileSprite;
            enemySubjectSprite = enemySprite;
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                RefreshSlotSubjectArtwork(slot);
            }
        }

        public Button GetSlotButton(int index)
        {
            ValidateIndex(index, slotButtons.Length);
            return slotButtons[index];
        }

        public Text GetSlotLabelText(int index)
        {
            ValidateIndex(index, slotLabels.Length);
            return slotLabels[index];
        }

        public Text GetSlotDescriptionText(int index)
        {
            ValidateIndex(index, slotDescriptionTexts.Length);
            return slotDescriptionTexts[index];
        }

        public Button GetSlotProjectileButton(int index)
        {
            ValidateIndex(index, slotProjectileButtons.Length);
            return slotProjectileButtons[index];
        }

        public Button GetSlotEnemyButton(int index)
        {
            ValidateIndex(index, slotEnemyButtons.Length);
            return slotEnemyButtons[index];
        }

        public SubjectType GetSlotSubjectType(int index)
        {
            ValidateIndex(index, slotSubjectTypes.Length);
            return slotSubjectTypes[index];
        }

        public Button GetSlotSubjectToggleButton(int index)
        {
            ValidateIndex(index, slotProjectileButtons.Length);
            return slotProjectileButtons[index];
        }

        public bool RequestSlotDoubleClick(int index)
        {
            ValidateIndex(index, slotDoubleClickRelays.Length);
            if (!editable ||
                !slotHasCards[index] ||
                !slotButtons[index].interactable)
            {
                return false;
            }

            HandleSlotDoubleClicked(index);
            return true;
        }

        public void SetSlotSubjectType(
            int index,
            SubjectType subjectType)
        {
            ValidateIndex(index, slotSubjectTypes.Length);
            slotSubjectTypes[index] =
                subjectType == SubjectType.Enemy
                    ? SubjectType.Enemy
                    : SubjectType.Projectile;
            RefreshSlotSubjectVisual(index);
            if (index == selectedSlot)
            {
                ApplySelectedSubjectVisuals();
            }

            RefreshActiveEffectSummary();
        }

        public void SetSlotSubjectTypes(
            IReadOnlyList<SubjectType> subjects)
        {
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                SetSlotSubjectType(
                    slot,
                    subjects != null &&
                    slot < subjects.Count
                        ? subjects[slot]
                        : SubjectType.Projectile);
            }
        }

        public Button GetCardButton(int index)
        {
            ValidateIndex(index, cardViews.Length);
            return cardViews[index].Button;
        }

        public StageOneCardView GetCardView(int index)
        {
            ValidateIndex(index, cardViews.Length);
            return cardViews[index];
        }

        public StageOneCardDropSlot GetSlotDropTarget(int index)
        {
            ValidateIndex(index, slotDropTargets.Length);
            return slotDropTargets[index];
        }

        public bool RequestCardDrop(
            int visibleCardIndex,
            int slotIndex)
        {
            if (visibleCardIndex < 0 ||
                visibleCardIndex >= visibleCardCount ||
                slotIndex < 0 ||
                slotIndex >= slotDropTargets.Length)
            {
                return false;
            }

            return slotDropTargets[slotIndex].TryAccept(
                visibleCardInstanceIds[visibleCardIndex]);
        }

        private void Update()
        {
            if (!IsVisible)
            {
                return;
            }

            RefreshResponsiveScale(false);
            RefreshTowerPreviewVisuals();
        }

        private void OnDisable()
        {
            SetTowerPreviewAnimationEnabled(false);
        }

        private void OnDestroy()
        {
            SetTowerPreviewAnimationEnabled(false);
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            catalog = catalog ??
                StageOneUiTextCatalog.FromJson(null);
            font = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");

            var canvasObject = new GameObject(
                "Tower Loadout Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
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

            var backdropObject = new GameObject(
                "Full Screen Blueprint Wipe",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(StageOneBlueprintGridGraphic));
            backdropObject.transform.SetParent(
                canvasObject.transform,
                false);
            blueprintBackdropRoot =
                backdropObject.GetComponent<RectTransform>();
            Stretch(blueprintBackdropRoot, 0f);
            backdropGraphic =
                backdropObject.GetComponent<
                    StageOneBlueprintGridGraphic>();
            backdropGraphic.Configure(
                new Color32(8, 30, 55, 255),
                new Color32(42, 94, 130, 88),
                new Color32(76, 139, 176, 142),
                24f);
            backdropGraphic.raycastTarget = true;
            backdropGraphic.SetRevealProgress(0f);

            RectTransform safeArea = new GameObject(
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
                "Tower Blueprint Workbench",
                typeof(RectTransform),
                typeof(CanvasGroup));
            panelObject.transform.SetParent(safeArea, false);
            panelRoot = panelObject.GetComponent<RectTransform>();
            panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
            panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            panelRoot.pivot = new Vector2(0.5f, 0.5f);
            panelRoot.anchoredPosition = Vector2.zero;
            panelRoot.sizeDelta =
                new Vector2(LandscapeWidth, LandscapeHeight);
            panelCanvasGroup =
                panelObject.GetComponent<CanvasGroup>();
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
            CreateHeader();
            CreateTowerPreview();
            CreateSettingsSurface();
            CreateSlots();
            CreateCardScroller();
            built = true;
            ApplyLandscapeLayout();
        }

        private void CreateHeader()
        {
            titleText = CreateText(
                "Tower Title",
                panelRoot,
                28,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            sectionTitleText = CreateText(
                "Workbench Title",
                panelRoot,
                15,
                FontStyle.Bold,
                MutedTextColor,
                TextAnchor.MiddleLeft);
            sectionTitleText.text =
                catalog.Get("tower_panel.blueprint_title");

            closeButton = CreateButton(
                "Close",
                panelRoot,
                LockedColor,
                out Text closeLabel,
                17);
            closeLabel.text = "X";
            closeButton.onClick.AddListener(HandleClose);

            upgradeButton = CreateButton(
                "Upgrade Tower",
                panelRoot,
                ActiveColor,
                out upgradeLabel,
                15);
            upgradeButton.onClick.AddListener(
                () => UpgradeRequested?.Invoke());
        }

        private void CreateTowerPreview()
        {
            towerPreviewFrame = new GameObject(
                "Selected Tower Preview",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            towerPreviewFrame.SetParent(panelRoot, false);
            towerPreviewContent = new GameObject(
                "Tower Preview Stage",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            towerPreviewContent.SetParent(
                towerPreviewFrame,
                false);
            Stretch(towerPreviewContent, 0f);

            towerPreviewPlaceholder = CreateText(
                "Tower Preview Placeholder",
                towerPreviewContent,
                18,
                FontStyle.Bold,
                MutedTextColor,
                TextAnchor.MiddleCenter);
            towerPreviewPlaceholder.text =
                catalog.Get("tower_panel.preview_title");
            Stretch(towerPreviewPlaceholder.rectTransform, 24f);
        }

        private void CreateSettingsSurface()
        {
            settingsTintImage = CreatePanelImage(
                "Subject Blueprint Tint",
                panelRoot,
                Color.clear,
                false);
            effectBackplate = CreatePanelImage(
                "Current Effect Backplate",
                panelRoot,
                BlueprintRaised,
                false);

            effectTitleText = CreateText(
                "Current Effect Title",
                panelRoot,
                14,
                FontStyle.Bold,
                MutedTextColor,
                TextAnchor.MiddleLeft);
            effectTitleText.text =
                catalog.Get("tower_panel.active_effect");

            targetTitleText = CreateText(
                "Target Selection Title",
                panelRoot,
                12,
                FontStyle.Bold,
                MutedTextColor,
                TextAnchor.MiddleCenter);
            targetTitleText.text =
                catalog.Get("tower_panel.target_title");

            instructionText = CreateText(
                "Loadout Instruction",
                panelRoot,
                14,
                FontStyle.Normal,
                TextColor,
                TextAnchor.MiddleLeft);
            instructionText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            instructionText.verticalOverflow =
                VerticalWrapMode.Truncate;
            instructionText.resizeTextForBestFit = true;
            instructionText.resizeTextMinSize = 10;
            instructionText.resizeTextMaxSize = 14;
            instructionText.lineSpacing = 1.05f;
        }

        private Button CreateSubjectButton(
            string objectName,
            out Image artwork,
            out Text fallback,
            out Image accentLine)
        {
            Button button = CreateButton(
                objectName,
                panelRoot,
                ButtonColor,
                out fallback,
                24);
            fallback.text = catalog.Get(
                "tower_panel.subject_projectile_short");
            Stretch(fallback.rectTransform, 8f);

            artwork = new GameObject(
                "Subject Artwork",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<Image>();
            artwork.transform.SetParent(button.transform, false);
            artwork.color = Color.white;
            artwork.preserveAspect = true;
            artwork.raycastTarget = false;
            RectTransform artworkRect = artwork.rectTransform;
            artworkRect.anchorMin = new Vector2(0f, 0f);
            artworkRect.anchorMax = new Vector2(1f, 1f);
            artworkRect.offsetMin = new Vector2(16f, 16f);
            artworkRect.offsetMax = new Vector2(-16f, -16f);

            accentLine = new GameObject(
                "Subject Accent",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<Image>();
            accentLine.transform.SetParent(
                button.transform,
                false);
            accentLine.color = ProjectileColor;
            accentLine.raycastTarget = false;
            RectTransform accentRect =
                accentLine.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(1f, 0f);
            accentRect.pivot = new Vector2(0.5f, 0f);
            accentRect.offsetMin = new Vector2(8f, 5f);
            accentRect.offsetMax = new Vector2(-8f, 9f);
            return button;
        }

        private void CreateSlots()
        {
            for (int slot = 0; slot < slotButtons.Length; slot++)
            {
                int capturedSlot = slot;
                slotButtons[slot] = CreateButton(
                    "Tower Slot " + (slot + 1),
                    panelRoot,
                    ButtonColor,
                    out slotLabels[slot],
                    14);
                slotButtons[slot].onClick.AddListener(
                    () => HandleSlotClicked(capturedSlot));

                Image descriptionBackplate = CreatePanelImage(
                    "Slot " + (slot + 1) + " Description",
                    panelRoot,
                    BlueprintRaised,
                    false);
                slotDescriptionBackplates[slot] =
                    descriptionBackplate;
                slotDescriptionTexts[slot] = CreateText(
                    "Description",
                    descriptionBackplate.transform,
                    14,
                    FontStyle.Normal,
                    TextColor,
                    TextAnchor.MiddleLeft);
                slotDescriptionTexts[slot].horizontalOverflow =
                    HorizontalWrapMode.Wrap;
                slotDescriptionTexts[slot].verticalOverflow =
                    VerticalWrapMode.Truncate;
                Stretch(
                    slotDescriptionTexts[slot].rectTransform,
                    14f);

                Button subjectToggle =
                    CreateSubjectButton(
                        "Slot " + (slot + 1) +
                        " Subject Toggle",
                        out slotProjectileArtworks[slot],
                        out slotProjectileFallbacks[slot],
                        out slotSubjectAccentLines[slot]);
                slotProjectileButtons[slot] = subjectToggle;
                slotEnemyButtons[slot] = subjectToggle;
                slotEnemyArtworks[slot] =
                    slotProjectileArtworks[slot];
                slotEnemyFallbacks[slot] =
                    slotProjectileFallbacks[slot];
                subjectToggle.onClick.AddListener(
                    () => HandleSlotSubjectToggle(
                        capturedSlot));
                ConfigureSubjectArtwork(
                    slotProjectileArtworks[slot],
                    slotProjectileFallbacks[slot],
                    null);

                StageOneCardDropSlot dropTarget =
                    slotButtons[slot].gameObject
                        .AddComponent<StageOneCardDropSlot>();
                dropTarget.Configure(slot, false);
                dropTarget.SetHighlight(
                    slotButtons[slot].targetGraphic,
                    ActiveColor);
                dropTarget.DropRequested += HandleCardDropped;
                slotDropTargets[slot] = dropTarget;

                StageOneSlotDoubleClickRelay clickRelay =
                    slotButtons[slot].gameObject
                        .AddComponent<
                            StageOneSlotDoubleClickRelay>();
                clickRelay.Configure(slot);
                slotDoubleClickRelays[slot] = clickRelay;
            }

            inventoryTitleText = CreateText(
                "Owned Cards Title",
                panelRoot,
                17,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
        }

        private void CreateCardScroller()
        {
            var viewportHost = new GameObject(
                "Card Inventory Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(RectMask2D),
                typeof(ScrollRect));
            viewportHost.transform.SetParent(panelRoot, false);
            cardViewport =
                viewportHost.GetComponent<RectTransform>();
            Image viewportImage =
                viewportHost.GetComponent<Image>();
            viewportImage.color =
                new Color32(12, 38, 62, 194);
            viewportImage.raycastTarget = true;

            cardContent = new GameObject(
                "Card Inventory Content",
                typeof(RectTransform),
                typeof(GridLayoutGroup))
                .GetComponent<RectTransform>();
            cardContent.SetParent(cardViewport, false);
            cardContent.anchorMin = new Vector2(0f, 1f);
            cardContent.anchorMax = new Vector2(1f, 1f);
            cardContent.pivot = new Vector2(0.5f, 1f);
            cardContent.anchoredPosition = Vector2.zero;
            cardContent.sizeDelta = Vector2.zero;
            cardGridLayout =
                cardContent.GetComponent<GridLayoutGroup>();
            cardGridLayout.startCorner =
                GridLayoutGroup.Corner.UpperLeft;
            cardGridLayout.startAxis =
                GridLayoutGroup.Axis.Horizontal;
            cardGridLayout.childAlignment =
                TextAnchor.UpperLeft;
            cardGridLayout.constraint =
                GridLayoutGroup.Constraint.FixedColumnCount;
            cardGridLayout.spacing =
                new Vector2(
                    InventoryCardGap,
                    InventoryCardGap);
            cardGridLayout.padding =
                new RectOffset(12, 12, 12, 12);

            cardScrollRect =
                viewportHost.GetComponent<ScrollRect>();
            cardScrollRect.viewport = cardViewport;
            cardScrollRect.content = cardContent;
            cardScrollRect.horizontal = false;
            cardScrollRect.vertical = true;
            cardScrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            cardScrollRect.inertia = true;
            cardScrollRect.decelerationRate = 0.12f;
            cardScrollRect.scrollSensitivity = 34f;

            CreateScrollbar();
            for (int i = 0; i < cardViews.Length; i++)
            {
                int capturedIndex = i;
                StageOneCardView cardView =
                    StageOneCardView.CreateRuntime(
                        "Inventory Card " + (i + 1),
                        cardContent,
                        font);
                cardView.Button.onClick.AddListener(
                    () => HandleCard(capturedIndex));
                StageOneCardDragSource dragSource =
                    cardView.gameObject.AddComponent<
                        StageOneCardDragSource>();
                dragSource.Configure(-1, canvas, false);
                cardViews[i] = cardView;
                cardDragSources[i] = dragSource;
                visibleCardInstanceIds[i] = -1;
            }
        }

        private void CreateScrollbar()
        {
            var scrollbarHost = new GameObject(
                "Card Inventory Scrollbar",
                typeof(RectTransform),
                typeof(Image),
                typeof(Scrollbar));
            scrollbarHost.transform.SetParent(panelRoot, false);
            scrollbarRect =
                scrollbarHost.GetComponent<RectTransform>();
            Image scrollbarBackground =
                scrollbarHost.GetComponent<Image>();
            scrollbarBackground.color = ScrollbarColor;

            Image handleImage = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(Image))
                .GetComponent<Image>();
            handleImage.transform.SetParent(
                scrollbarHost.transform,
                false);
            Stretch(handleImage.rectTransform, 2f);
            handleImage.color = ActiveColor;

            Scrollbar scrollbar =
                scrollbarHost.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleImage.rectTransform;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction =
                Scrollbar.Direction.BottomToTop;
            scrollbar.size = 1f;
            cardScrollRect.verticalScrollbar = scrollbar;
            cardScrollRect.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
        }

        private void RefreshResponsiveScale(bool force)
        {
            if (panelRoot == null ||
                (!force &&
                 lastScreenWidth == Screen.width &&
                 lastScreenHeight == Screen.height))
            {
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            Canvas.ForceUpdateCanvases();

            RectTransform safeArea =
                panelRoot.parent as RectTransform;
            float availableWidth = safeArea != null
                ? Mathf.Max(1f, safeArea.rect.width - 28f)
                : LandscapeWidth;
            float availableHeight = safeArea != null
                ? Mathf.Max(1f, safeArea.rect.height - 28f)
                : LandscapeHeight;
            bool usePortrait =
                availableHeight > availableWidth * 1.12f;
            if (portraitLayout != usePortrait || force)
            {
                portraitLayout = usePortrait;
                if (portraitLayout)
                {
                    ApplyPortraitLayout();
                }
                else
                {
                    ApplyLandscapeLayout();
                }
            }

            float scale = Mathf.Min(
                1f,
                Mathf.Min(
                    availableWidth / panelRoot.sizeDelta.x,
                    availableHeight / panelRoot.sizeDelta.y));
            panelRoot.localScale =
                new Vector3(scale, scale, 1f);
            LayoutInventoryCards();
            RefreshTowerPreviewLayout();
        }

        private void ApplyLandscapeLayout()
        {
            ApplyTypographyScale(false);
            panelRoot.sizeDelta =
                new Vector2(LandscapeWidth, LandscapeHeight);
            SetRect(titleText.rectTransform, 28f, 754f, 790f, 48f);
            SetRect(
                sectionTitleText.rectTransform,
                890f,
                765f,
                310f,
                28f);
            SetRect(
                upgradeButton.GetComponent<RectTransform>(),
                1218f,
                756f,
                182f,
                42f);
            SetRect(
                closeButton.GetComponent<RectTransform>(),
                1416f,
                760f,
                40f,
                38f);

            SetRect(towerPreviewFrame, 28f, 76f, 350f, 654f);
            SetRect(
                settingsTintImage.rectTransform,
                404f,
                26f,
                1048f,
                704f);
            SetRect(
                effectBackplate.rectTransform,
                430f,
                581f,
                1000f,
                128f);
            SetRect(
                effectTitleText.rectTransform,
                448f,
                675f,
                500f,
                24f);
            SetRect(
                instructionText.rectTransform,
                448f,
                594f,
                956f,
                78f);
            SetRect(
                targetTitleText.rectTransform,
                1334f,
                561f,
                96f,
                16f);

            float[] slotY = { 464f, 358f, 252f };
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                SetRect(
                    slotButtons[slot]
                        .GetComponent<RectTransform>(),
                    430f,
                    slotY[slot],
                    96f,
                    96f);
                SetRect(
                    slotDescriptionBackplates[slot]
                        .rectTransform,
                    540f,
                    slotY[slot],
                    764f,
                    96f);
                SetRect(
                    slotProjectileButtons[slot]
                        .GetComponent<RectTransform>(),
                    1334f,
                    slotY[slot],
                    96f,
                    96f);
            }

            SetRect(
                inventoryTitleText.rectTransform,
                430f,
                218f,
                900f,
                30f);
            SetRect(cardViewport, 430f, 26f, 982f, 184f);
            SetRect(scrollbarRect, 1416f, 26f, 18f, 184f);
            LayoutInventoryCards();
        }

        private void ApplyPortraitLayout()
        {
            ApplyTypographyScale(true);
            panelRoot.sizeDelta =
                new Vector2(PortraitWidth, PortraitHeight);
            SetRect(titleText.rectTransform, 24f, 1232f, 390f, 70f);
            SetRect(
                sectionTitleText.rectTransform,
                24f,
                1206f,
                390f,
                30f);
            SetRect(
                upgradeButton.GetComponent<RectTransform>(),
                430f,
                1214f,
                200f,
                90f);
            SetRect(
                closeButton.GetComponent<RectTransform>(),
                646f,
                1214f,
                90f,
                90f);

            SetRect(towerPreviewFrame, 24f, 900f, 712f, 310f);
            SetRect(
                settingsTintImage.rectTransform,
                24f,
                24f,
                712f,
                854f);
            SetRect(
                effectBackplate.rectTransform,
                48f,
                688f,
                666f,
                170f);
            SetRect(
                effectTitleText.rectTransform,
                64f,
                823f,
                410f,
                22f);
            SetRect(
                instructionText.rectTransform,
                64f,
                706f,
                630f,
                108f);
            SetRect(
                targetTitleText.rectTransform,
                620f,
                670f,
                94f,
                16f);

            float[] slotY = { 574f, 460f, 346f };
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                SetRect(
                    slotButtons[slot]
                        .GetComponent<RectTransform>(),
                    48f,
                    slotY[slot],
                    94f,
                    94f);
                SetRect(
                    slotDescriptionBackplates[slot]
                        .rectTransform,
                    154f,
                    slotY[slot],
                    448f,
                    94f);
                SetRect(
                    slotProjectileButtons[slot]
                        .GetComponent<RectTransform>(),
                    620f,
                    slotY[slot],
                    94f,
                    94f);
            }

            SetRect(
                inventoryTitleText.rectTransform,
                48f,
                304f,
                650f,
                30f);
            SetRect(cardViewport, 48f, 44f, 646f, 252f);
            SetRect(scrollbarRect, 700f, 44f, 18f, 252f);
            LayoutInventoryCards();
        }

        private void ApplyTypographyScale(bool portrait)
        {
            titleText.fontSize = portrait ? 40 : 28;
            sectionTitleText.fontSize = portrait ? 22 : 15;
            upgradeLabel.fontSize = portrait ? 22 : 15;
            Text closeLabel =
                closeButton.GetComponentInChildren<Text>(true);
            if (closeLabel != null)
            {
                closeLabel.fontSize = portrait ? 30 : 17;
            }

            towerPreviewPlaceholder.fontSize = portrait ? 26 : 18;
            effectTitleText.fontSize = portrait ? 20 : 14;
            targetTitleText.fontSize = portrait ? 18 : 12;
            instructionText.fontSize = portrait ? 21 : 14;
            instructionText.resizeTextMinSize =
                portrait ? 15 : 10;
            instructionText.resizeTextMaxSize =
                portrait ? 21 : 14;
            inventoryTitleText.fontSize = portrait ? 24 : 17;

            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                slotLabels[slot].fontSize =
                    portrait ? 20 : 14;
                slotDescriptionTexts[slot].fontSize =
                    portrait ? 20 : 14;
                slotProjectileFallbacks[slot].fontSize =
                    portrait ? 32 : 24;
            }

            float cardTextScale = portrait ? 1.75f : 1f;
            for (int i = 0; i < cardViews.Length; i++)
            {
                cardViews[i].SetTextScale(cardTextScale);
            }
        }

        private void RefreshSlots(
            int unlockedSlotCount,
            int[] slotCardInstanceIds,
            IReadOnlyList<StageOneLoadoutCard> cards)
        {
            for (int slot = 0; slot < slotButtons.Length; slot++)
            {
                bool unlocked = slot < unlockedSlotCount;
                int cardInstanceId =
                    slotCardInstanceIds != null &&
                    slot < slotCardInstanceIds.Length
                        ? slotCardInstanceIds[slot]
                        : -1;
                slotHasCards[slot] =
                    unlocked && cardInstanceId >= 0;
                if (!slotHasCards[slot])
                {
                    lastSlotClickTimes[slot] =
                        float.NegativeInfinity;
                }
                slotButtons[slot].interactable = unlocked;
                slotProjectileButtons[slot].interactable =
                    unlocked && editable;
                slotDropTargets[slot].SetDropEnabled(
                    unlocked && editable);
                slotLabels[slot].text = !unlocked
                    ? GetLockedSlotLabel()
                    : cardInstanceId < 0
                        ? catalog.Get("tower_panel.empty")
                        : ResolveSlotCardLabel(
                            cards,
                            cardInstanceId);
                slotDescriptionTexts[slot].color =
                    unlocked ? TextColor : MutedTextColor;
                slotDescriptionTexts[slot].text =
                    ResolveSlotDescription(
                        slot,
                        unlocked,
                        cards,
                        cardInstanceId);
                RefreshSlotSubjectVisual(slot);
                slotDropTargets[slot].RefreshRestingColor();
            }
        }

        private void RefreshCards(
            IReadOnlyList<StageOneLoadoutCard> cards,
            int[] slotCardInstanceIds,
            SubjectType subjectType)
        {
            visibleCardCount = Mathf.Min(
                cards == null ? 0 : cards.Count,
                cardViews.Length);
            for (int i = 0; i < cardViews.Length; i++)
            {
                bool visible = i < visibleCardCount;
                cardViews[i].gameObject.SetActive(visible);
                if (!visible)
                {
                    visibleCardInstanceIds[i] = -1;
                    visibleCardSlotIndices[i] = -1;
                    cardDragSources[i].Configure(
                        -1,
                        canvas,
                        false);
                    continue;
                }

                StageOneLoadoutCard card = cards[i];
                visibleCardInstanceIds[i] = card.InstanceId;
                visibleCardSlotIndices[i] =
                    card.EquippedOnSelectedTower
                        ? FindSlotIndex(
                            slotCardInstanceIds,
                            card.InstanceId)
                        : -1;
                SubjectType cardSubjectType =
                    visibleCardSlotIndices[i] >= 0
                        ? slotSubjectTypes[
                            visibleCardSlotIndices[i]]
                        : subjectType;
                string projectileTargetLabel =
                    catalog.Get("tower_panel.projectile");
                string enemyTargetLabel =
                    catalog.Get("tower_panel.enemy");
                var contextualDisplay =
                    new StageOneCardDisplay(
                        card.Display.StableId,
                        card.Display.Name,
                        projectileTargetLabel + " · " +
                        card.Display.ProjectileDescription,
                        enemyTargetLabel + " · " +
                        card.Display.EnemyDescription,
                        cardSubjectType == SubjectType.Enemy,
                        card.Display.Tier);
                CardTier tier = (CardTier)Mathf.Clamp(
                    card.Display.Tier,
                    1,
                    5);
                cardViews[i].Configure(
                    contextualDisplay,
                    tier,
                    cardSubjectType,
                    null,
                    card.Equipped,
                    card.EquippedOnSelectedTower
                        ? catalog.Get(
                            "tower_panel.equipped_badge")
                        : card.Equipped
                            ? catalog.Get(
                                "tower_panel.move_badge")
                            : null,
                    editable);
                cardViews[i].SetPlaceholderSymbol(
                    GetCardSymbol(
                        card.Display.StableId));
                cardDragSources[i].Configure(
                    card.InstanceId,
                    canvas,
                    editable);
            }

            LayoutInventoryCards();
        }

        private void LayoutInventoryCards()
        {
            if (cardContent == null || cardViewport == null)
            {
                return;
            }

            int columns = portraitLayout
                ? PortraitInventoryColumns
                : LandscapeInventoryColumns;
            float cardWidth = portraitLayout
                ? 299f
                : InventoryCardWidth;
            float cardHeight = portraitLayout
                ? 228f
                : InventoryCardHeight;
            int rows = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    visibleCardCount /
                    (float)columns));
            float contentHeight = Mathf.Max(
                cardViewport.rect.height,
                24f +
                rows * cardHeight +
                (rows - 1) * InventoryCardGap);
            cardContent.anchorMin = new Vector2(0f, 1f);
            cardContent.anchorMax = new Vector2(1f, 1f);
            cardContent.pivot = new Vector2(0.5f, 1f);
            cardContent.sizeDelta =
                new Vector2(0f, contentHeight);
            cardGridLayout.constraintCount = columns;
            cardGridLayout.cellSize =
                new Vector2(cardWidth, cardHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                cardContent);
            cardScrollRect.SetLayoutVertical();
        }

        private void ApplySelectedSubjectVisuals()
        {
            settingsTintImage.color = Color.clear;
            effectBackplate.color = BlueprintRaised;
        }

        private void RefreshSlotSubjectVisual(int slot)
        {
            if (slot < 0 ||
                slot >= MaximumSlots ||
                slotButtons[slot] == null)
            {
                return;
            }

            bool unlocked = slotButtons[slot].interactable;
            bool enemy =
                slotSubjectTypes[slot] == SubjectType.Enemy;
            Color targetColor =
                enemy ? EnemyColor : ProjectileColor;
            Color slotColor = enemy
                ? new Color32(112, 39, 50, 244)
                : new Color32(124, 66, 31, 244);

            SetButtonColor(
                slotButtons[slot],
                unlocked ? slotColor : LockedColor);
            slotDescriptionBackplates[slot].color =
                unlocked
                    ? BlueprintRaised
                    : LockedDescriptionColor;
            SetButtonColor(
                slotProjectileButtons[slot],
                ButtonColor);
            slotSubjectAccentLines[slot].color = targetColor;
            slotProjectileFallbacks[slot].text =
                enemy
                    ? catalog.Get(
                        "tower_panel.subject_enemy_short")
                    : catalog.Get(
                        "tower_panel.subject_projectile_short");
            RefreshSlotSubjectArtwork(slot);

            SetOutlineColor(
                slotButtons[slot].gameObject,
                !unlocked
                    ? BlueprintLine
                    : slot == selectedSlot
                        ? ActiveColor
                        : targetColor);
            SetOutlineColor(
                slotDescriptionBackplates[slot].gameObject,
                BlueprintLine);
            SetOutlineColor(
                slotProjectileButtons[slot].gameObject,
                BlueprintLine);
        }

        private void RefreshSlotSubjectArtwork(int slot)
        {
            if (slot < 0 ||
                slot >= MaximumSlots ||
                slotProjectileArtworks[slot] == null)
            {
                return;
            }

            bool enemy =
                slotSubjectTypes[slot] == SubjectType.Enemy;
            ConfigureSubjectArtwork(
                slotProjectileArtworks[slot],
                slotProjectileFallbacks[slot],
                enemy
                    ? enemySubjectSprite
                    : projectileSubjectSprite);
        }

        private void BeginBlueprintTransition(bool showing)
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }

            blueprintBackdropRoot.gameObject.SetActive(true);
            panelRoot.gameObject.SetActive(true);
            if (showing)
            {
                SetTowerPreviewAnimationEnabled(true);
            }

            transitionShowing = showing;
            transitionRoutine = StartCoroutine(
                RunBlueprintTransition(showing));
        }

        private IEnumerator RunBlueprintTransition(bool showing)
        {
            transitionRunning = true;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
            float start = backdropGraphic.RevealProgress;
            float end = showing ? 1f : 0f;
            float duration =
                Mathf.Max(
                    0.04f,
                    BlueprintTransitionDuration *
                    Mathf.Abs(end - start));
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized =
                    Mathf.Clamp01(elapsed / duration);
                float eased =
                    normalized * normalized *
                    (3f - 2f * normalized);
                SetTransitionState(
                    Mathf.Lerp(start, end, eased),
                    false);
                yield return null;
            }

            SetTransitionState(end, showing);
            transitionRunning = false;
            transitionRoutine = null;
            if (!showing)
            {
                HideImmediately();
            }
        }

        private void SetTransitionState(
            float reveal,
            bool fullyInteractive)
        {
            float progress = Mathf.Clamp01(reveal);
            backdropGraphic.SetRevealProgress(progress);
            float contentProgress = Mathf.InverseLerp(
                0.36f,
                0.9f,
                progress);
            panelCanvasGroup.alpha =
                contentProgress * contentProgress *
                (3f - 2f * contentProgress);
            panelCanvasGroup.interactable =
                fullyInteractive && progress >= 0.999f;
            panelCanvasGroup.blocksRaycasts =
                fullyInteractive && progress >= 0.999f;
        }

        private void HideImmediately()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }

            transitionRunning = false;
            transitionShowing = false;
            SetTowerPreviewAnimationEnabled(false);
            if (backdropGraphic != null)
            {
                backdropGraphic.SetRevealProgress(0f);
            }

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (panelRoot != null)
            {
                panelRoot.gameObject.SetActive(false);
            }

            if (blueprintBackdropRoot != null)
            {
                blueprintBackdropRoot.gameObject.SetActive(false);
            }
        }

        private void RebuildTowerPreview()
        {
            for (int i = 0; i < previewBindings.Count; i++)
            {
                Image image = previewBindings[i].Image;
                if (image == null)
                {
                    continue;
                }

                image.gameObject.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(image.gameObject);
                }
                else
                {
                    DestroyImmediate(image.gameObject);
                }
            }

            previewBindings.Clear();
            if (towerPreviewSource == null)
            {
                towerPreviewPlaceholder.gameObject.SetActive(true);
                return;
            }

            SpriteRenderer[] renderers =
                towerPreviewSource.GetComponentsInChildren<
                    SpriteRenderer>(true);
            Array.Sort(
                renderers,
                CompareSpriteRenderers);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer source = renderers[i];
                if (source == null ||
                    source.sprite == null)
                {
                    continue;
                }

                Image image = new GameObject(
                    "Tower Preview Part " +
                    previewBindings.Count,
                    typeof(RectTransform),
                    typeof(Image))
                    .GetComponent<Image>();
                image.transform.SetParent(
                    towerPreviewContent,
                    false);
                image.raycastTarget = false;
                image.preserveAspect = true;
                previewBindings.Add(
                    new TowerPreviewBinding
                    {
                        Source = source,
                        Image = image
                    });
            }

            towerPreviewPlaceholder.gameObject.SetActive(
                previewBindings.Count == 0);
            RefreshTowerPreviewLayout();
            RefreshTowerPreviewVisuals();
        }

        private void SetTowerPreviewAnimationEnabled(bool enabled)
        {
            if (towerPreviewAnimator != null)
            {
                towerPreviewAnimator.SetBlueprintPreviewAnimation(
                    enabled);
            }
        }

        private void RefreshTowerPreviewLayout()
        {
            if (towerPreviewSource == null ||
                previewBindings.Count == 0 ||
                towerPreviewContent == null)
            {
                return;
            }

            bool hasBounds = false;
            Bounds combined = default(Bounds);
            for (int i = 0; i < previewBindings.Count; i++)
            {
                SpriteRenderer source =
                    previewBindings[i].Source;
                if (source == null ||
                    !source.enabled ||
                    !source.gameObject.activeInHierarchy ||
                    source.sprite == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combined = source.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(source.bounds);
                }
            }

            if (!hasBounds)
            {
                return;
            }

            Rect previewRect = towerPreviewContent.rect;
            float availableWidth =
                Mathf.Max(1f, previewRect.width - 60f);
            float availableHeight =
                Mathf.Max(1f, previewRect.height - 76f);
            float pixelScale = Mathf.Min(
                availableWidth /
                    Mathf.Max(0.01f, combined.size.x),
                availableHeight /
                    Mathf.Max(0.01f, combined.size.y));
            pixelScale *= 0.88f;
            Vector3 center = combined.center;

            for (int i = 0; i < previewBindings.Count; i++)
            {
                TowerPreviewBinding binding =
                    previewBindings[i];
                SpriteRenderer source = binding.Source;
                Image image = binding.Image;
                if (source == null ||
                    image == null ||
                    source.sprite == null)
                {
                    continue;
                }

                RectTransform rect = image.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                Vector3 rendererCenter = source.bounds.center;
                rect.anchoredPosition = new Vector2(
                    (rendererCenter.x - center.x) * pixelScale,
                    (rendererCenter.y - center.y) * pixelScale);
                Vector3 lossyScale = source.transform.lossyScale;
                Vector3 spriteSize = source.sprite.bounds.size;
                rect.sizeDelta = new Vector2(
                    Mathf.Abs(spriteSize.x * lossyScale.x) *
                        pixelScale,
                    Mathf.Abs(spriteSize.y * lossyScale.y) *
                        pixelScale);
                float flip =
                    (source.flipX ? -1f : 1f) *
                    (lossyScale.x < 0f ? -1f : 1f);
                rect.localScale = new Vector3(flip, 1f, 1f);
            }
        }

        private void RefreshTowerPreviewVisuals()
        {
            bool needsRebuild = false;
            for (int i = 0; i < previewBindings.Count; i++)
            {
                TowerPreviewBinding binding =
                    previewBindings[i];
                if (binding.Source == null ||
                    binding.Image == null)
                {
                    needsRebuild = towerPreviewSource != null;
                    continue;
                }

                binding.Image.sprite = binding.Source.sprite;
                binding.Image.color = binding.Source.color;
                binding.Image.gameObject.SetActive(
                    binding.Source.enabled &&
                    binding.Source.gameObject.activeInHierarchy &&
                    binding.Source.sprite != null);
            }

            if (needsRebuild)
            {
                RebuildTowerPreview();
                return;
            }

            RefreshTowerPreviewLayout();
        }

        private void HandleCard(int visibleIndex)
        {
            if (visibleIndex < 0 ||
                visibleIndex >= visibleCardCount ||
                visibleCardInstanceIds[visibleIndex] < 0)
            {
                return;
            }

            CardRequested?.Invoke(
                visibleCardInstanceIds[visibleIndex]);
        }

        private void HandleSlotClicked(int slotIndex)
        {
            bool canUnequip =
                slotIndex >= 0 &&
                slotIndex < MaximumSlots &&
                editable &&
                slotHasCards[slotIndex];
            float now = Time.unscaledTime;
            bool isDoubleClick =
                canUnequip &&
                now - lastSlotClickTimes[slotIndex] <=
                    SlotDoubleClickWindowSeconds;
            lastSlotClickTimes[slotIndex] =
                canUnequip && !isDoubleClick
                    ? now
                    : float.NegativeInfinity;

            HandleSlotSelected(slotIndex);
            if (isDoubleClick)
            {
                HandleSlotDoubleClicked(slotIndex);
            }
        }

        private void HandleSlotSelected(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaximumSlots)
            {
                return;
            }

            selectedSlot = slotIndex;
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                RefreshSlotSubjectVisual(slot);
            }

            ApplySelectedSubjectVisuals();
            SlotRequested?.Invoke(slotIndex);
        }

        private void HandleSlotSubjectToggle(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaximumSlots)
            {
                return;
            }

            HandleSlotSelected(slotIndex);
            SubjectType subjectType =
                slotSubjectTypes[slotIndex] ==
                SubjectType.Enemy
                    ? SubjectType.Projectile
                    : SubjectType.Enemy;
            SetSlotSubjectType(slotIndex, subjectType);
            if (SlotSubjectTypeRequested != null)
            {
                SlotSubjectTypeRequested.Invoke(
                    slotIndex,
                    slotSubjectTypes[slotIndex]);
            }
            else
            {
                SubjectTypeRequested?.Invoke(
                    slotSubjectTypes[slotIndex]);
            }
        }

        private void HandleSlotDoubleClicked(int slotIndex)
        {
            if (slotIndex < 0 ||
                slotIndex >= MaximumSlots ||
                !editable ||
                !slotHasCards[slotIndex] ||
                !slotButtons[slotIndex].interactable)
            {
                return;
            }

            selectedSlot = slotIndex;
            SlotUnequipRequested?.Invoke(slotIndex);
        }

        private void HandleCardDropped(
            int cardInstanceId,
            int slotIndex)
        {
            selectedSlot = slotIndex;
            ApplySelectedSubjectVisuals();
            CardDropped?.Invoke(cardInstanceId, slotIndex);
        }

        private void HandleClose()
        {
            Hide();
            CloseRequested?.Invoke();
        }

        private void CachePresentedLoadout(
            int unlockedSlotCount,
            int[] slotCardInstanceIds,
            IReadOnlyList<StageOneLoadoutCard> cards)
        {
            presentedUnlockedSlotCount = Mathf.Clamp(
                unlockedSlotCount,
                0,
                MaximumSlots);
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                presentedSlotCardInstanceIds[slot] =
                    slotCardInstanceIds != null &&
                    slot < slotCardInstanceIds.Length
                        ? slotCardInstanceIds[slot]
                        : -1;
            }

            presentedCards.Clear();
            if (cards == null)
            {
                return;
            }

            for (int index = 0; index < cards.Count; index++)
            {
                presentedCards.Add(cards[index]);
            }
        }

        private void RefreshActiveEffectSummary()
        {
            if (instructionText == null)
            {
                return;
            }

            var orderedEffects = new List<string>(
                MaximumSlots);
            for (int slot = 0;
                 slot < presentedUnlockedSlotCount;
                 slot++)
            {
                int cardInstanceId =
                    presentedSlotCardInstanceIds[slot];
                if (cardInstanceId < 0)
                {
                    continue;
                }

                string effect =
                    ResolveContextualCardDescription(
                        presentedCards,
                        cardInstanceId,
                        slotSubjectTypes[slot]);
                if (!string.IsNullOrWhiteSpace(effect))
                {
                    orderedEffects.Add(effect);
                }
            }

            string summary = orderedEffects.Count == 0
                ? catalog.Get("tower_panel.instruction")
                : string.Join("\n→ ", orderedEffects);
            instructionText.text =
                (!editable
                    ? catalog.Get("tower_panel.combat_locked") +
                      "\n"
                    : string.Empty) +
                summary;
        }

        private string ResolveSlotCardLabel(
            IReadOnlyList<StageOneLoadoutCard> cards,
            int instanceId)
        {
            if (instanceId < 0 || cards == null)
            {
                return catalog.Get("tower_panel.empty");
            }

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].InstanceId == instanceId)
                {
                    string symbol = GetCardSymbol(
                        cards[i].Display.StableId);
                    return string.IsNullOrEmpty(symbol)
                        ? cards[i].Display.Name
                        : symbol;
                }
            }

            return catalog.Get("tower_panel.empty");
        }

        private string GetLockedSlotLabel()
        {
            return catalog.Get("tower_panel.locked");
        }

        private static int FindSlotIndex(
            int[] slotCardInstanceIds,
            int cardInstanceId)
        {
            if (slotCardInstanceIds == null ||
                cardInstanceId < 0)
            {
                return -1;
            }

            for (int slot = 0;
                 slot < slotCardInstanceIds.Length &&
                 slot < MaximumSlots;
                 slot++)
            {
                if (slotCardInstanceIds[slot] ==
                    cardInstanceId)
                {
                    return slot;
                }
            }

            return -1;
        }

        private string ResolveSlotDescription(
            int slot,
            bool unlocked,
            IReadOnlyList<StageOneLoadoutCard> cards,
            int instanceId)
        {
            if (!unlocked)
            {
                return catalog.Format(
                    "tower_panel.unlock_level_format",
                    GetUnlockLevel(slot));
            }

            string description =
                ResolveCardDescription(cards, instanceId);
            return string.IsNullOrEmpty(description)
                ? catalog.Get("tower_panel.slot_drop")
                : description;
        }

        private static string ResolveCardDescription(
            IReadOnlyList<StageOneLoadoutCard> cards,
            int instanceId)
        {
            if (instanceId < 0 || cards == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].InstanceId != instanceId)
                {
                    continue;
                }

                StageOneCardDisplay display = cards[i].Display;
                return string.IsNullOrEmpty(display.Description)
                    ? display.Name
                    : display.Name + " · " +
                      display.Description;
            }

            return string.Empty;
        }

        private string ResolveContextualCardDescription(
            IReadOnlyList<StageOneLoadoutCard> cards,
            int instanceId,
            SubjectType subjectType)
        {
            if (instanceId < 0 || cards == null)
            {
                return string.Empty;
            }

            for (int index = 0; index < cards.Count; index++)
            {
                StageOneLoadoutCard card = cards[index];
                if (card.InstanceId != instanceId)
                {
                    continue;
                }

                StageOneCardDisplay display = card.Display;
                string descriptionKey =
                    GetCardDescriptionKey(
                        display.StableId,
                        subjectType);
                string description =
                    catalog.Contains(descriptionKey)
                        ? catalog.Get(descriptionKey)
                        : display.Description;
                if (string.IsNullOrWhiteSpace(description))
                {
                    return display.Name;
                }

                return string.IsNullOrWhiteSpace(display.Name)
                    ? description
                    : display.Name + " · " + description;
            }

            return string.Empty;
        }

        private static string GetCardDescriptionKey(
            string stableId,
            SubjectType subjectType)
        {
            string normalizedId =
                string.IsNullOrWhiteSpace(stableId)
                    ? "unknown"
                    : stableId.Trim();
            if (!normalizedId.StartsWith(
                    "card.",
                    StringComparison.Ordinal))
            {
                normalizedId = "card." + normalizedId;
            }

            string suffix =
                subjectType == SubjectType.Enemy
                    ? ".enemy"
                    : ".projectile";
            return normalizedId.EndsWith(
                    suffix,
                    StringComparison.Ordinal)
                ? normalizedId
                : normalizedId + suffix;
        }

        private static int GetUnlockLevel(int slot)
        {
            switch (slot)
            {
                case 1:
                    return 4;
                case 2:
                    return 6;
                default:
                    return 1;
            }
        }

        private static int CompareSpriteRenderers(
            SpriteRenderer left,
            SpriteRenderer right)
        {
            if (left == right)
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int layerComparison =
                left.sortingLayerID.CompareTo(
                    right.sortingLayerID);
            if (layerComparison != 0)
            {
                return layerComparison;
            }

            int orderComparison =
                left.sortingOrder.CompareTo(
                    right.sortingOrder);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            return right.transform.position.y.CompareTo(
                left.transform.position.y);
        }

        private static void ConfigureSubjectArtwork(
            Image image,
            Text fallback,
            Sprite sprite)
        {
            if (image == null || fallback == null)
            {
                return;
            }

            image.sprite = sprite;
            image.gameObject.SetActive(sprite != null);
            fallback.gameObject.SetActive(sprite == null);
        }

        private string GetCardSymbol(string stableId)
        {
            switch (stableId)
            {
                case "split":
                    return catalog.Get("card_symbol.split");
                case "pierce":
                    return catalog.Get("card_symbol.pierce");
                case "burn":
                    return catalog.Get("card_symbol.burn");
                case "slow":
                    return catalog.Get("card_symbol.slow");
                case "explode":
                    return catalog.Get("card_symbol.explode");
                case "knockback":
                    return catalog.Get("card_symbol.knockback");
                case "mark":
                    return catalog.Get("card_symbol.mark");
                case "gold_bounty":
                    return catalog.Get("card_symbol.gold_bounty");
                case "poison":
                    return catalog.Get("card_symbol.poison");
                case "enlarge":
                    return catalog.Get("card_symbol.enlarge");
                case "shrink":
                    return catalog.Get("card_symbol.shrink");
                case "stun":
                    return catalog.Get("card_symbol.stun");
                case "ricochet":
                    return catalog.Get("card_symbol.ricochet");
                case "bleed":
                    return catalog.Get("card_symbol.bleed");
                case "accelerate":
                    return catalog.Get("card_symbol.accelerate");
                case "homing":
                    return catalog.Get("card_symbol.homing");
                case "delay":
                    return catalog.Get("card_symbol.delay");
                case "curse":
                    return catalog.Get("card_symbol.curse");
                case "bind":
                    return catalog.Get("card_symbol.bind");
                case "airborne":
                    return catalog.Get("card_symbol.airborne");
                case "shock":
                    return catalog.Get("card_symbol.shock");
                case "freeze":
                    return catalog.Get("card_symbol.freeze");
                case "afterimage":
                    return catalog.Get("card_symbol.afterimage");
                case "pulse":
                    return catalog.Get("card_symbol.pulse");
                case "magnet":
                    return catalog.Get("card_symbol.magnet");
                case "reflect":
                    return catalog.Get("card_symbol.reflect");
                case "contagion":
                    return catalog.Get("card_symbol.contagion");
                case "seal":
                    return catalog.Get("card_symbol.seal");
                case "corrosion":
                    return catalog.Get("card_symbol.corrosion");
                case "orbit":
                    return catalog.Get("card_symbol.orbit");
                case "lifesteal":
                    return catalog.Get("card_symbol.lifesteal");
                case "fear":
                    return catalog.Get("card_symbol.fear");
                default:
                    return null;
            }
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            Color color,
            out Text label,
            int fontSize)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(Outline));
            host.transform.SetParent(parent, false);
            Image image = host.GetComponent<Image>();
            image.color = color;
            Button button = host.GetComponent<Button>();
            button.targetGraphic = image;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            Outline outline = host.GetComponent<Outline>();
            outline.effectColor = BlueprintLine;
            outline.effectDistance = new Vector2(1f, -1f);
            label = CreateText(
                "Label",
                host.transform,
                fontSize,
                FontStyle.Bold,
                TextColor,
                TextAnchor.MiddleCenter);
            label.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            label.verticalOverflow =
                VerticalWrapMode.Truncate;
            Stretch(label.rectTransform, 5f);
            return button;
        }

        private Image CreatePanelImage(
            string objectName,
            Transform parent,
            Color color,
            bool outlined)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            host.transform.SetParent(parent, false);
            Image image = host.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (outlined)
            {
                Outline outline = host.AddComponent<Outline>();
                outline.effectColor = BlueprintLine;
                outline.effectDistance =
                    new Vector2(1f, -1f);
            }

            return image;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            FontStyle style,
            TextAnchor alignment)
        {
            return CreateText(
                objectName,
                parent,
                fontSize,
                style,
                TextColor,
                alignment);
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            FontStyle style,
            Color color,
            TextAnchor alignment)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Text),
                typeof(Outline));
            host.transform.SetParent(parent, false);
            Text text = host.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.supportRichText = false;
            Outline outline = host.GetComponent<Outline>();
            outline.effectColor =
                new Color(0f, 0f, 0f, 0.78f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        private static void SetButtonColor(
            Button button,
            Color color)
        {
            if (button != null &&
                button.targetGraphic is Image image)
            {
                image.color = color;
            }
        }

        private static void SetOutlineColor(
            GameObject target,
            Color color)
        {
            if (target != null &&
                target.TryGetComponent(
                    out Outline outline))
            {
                outline.effectColor = color;
            }
        }

        private static void SetRect(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(
            RectTransform rect,
            float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void ValidateIndex(
            int index,
            int count)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index));
            }
        }
    }
}
