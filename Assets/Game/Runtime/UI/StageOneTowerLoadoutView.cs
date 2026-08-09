using System;
using System.Collections;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
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
            : this(
                instanceId,
                display,
                equipped,
                equippedOnSelectedTower,
                -1)
        {
        }

        public StageOneLoadoutCard(
            int instanceId,
            StageOneCardDisplay display,
            bool equipped,
            bool equippedOnSelectedTower,
            int equippedTowerId)
        {
            InstanceId = instanceId;
            Display = display;
            Equipped = equipped;
            EquippedOnSelectedTower = equippedOnSelectedTower;
            EquippedTowerId = equippedTowerId;
        }

        public int InstanceId { get; }
        public StageOneCardDisplay Display { get; }
        public bool Equipped { get; }
        public bool EquippedOnSelectedTower { get; }
        public int EquippedTowerId { get; }
    }

    /// <summary>
    /// Parchment workbench shown while a tower is selected. Landscape uses
    /// three columns: tower preview, equipped-card rules, and owned cards.
    /// Portrait keeps the stacked mobile composition with its large preview.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneTowerLoadoutView : MonoBehaviour
    {
        private const int InitialCardViewCapacity = 16;
        private const int MaximumSlots = 3;
        private const int LandscapeInventoryColumns = 3;
        private const int PortraitInventoryColumns = 3;
        private const float LandscapeWidth = 1480f;
        private const float LandscapeHeight = 820f;
        private const float PortraitWidth = 760f;
        private const float PortraitHeight = 1320f;
        private const float InventoryCardWidth = 113f;
        private const float InventoryCardHeight = 192f;
        private const float InventoryCardGap = 8f;
        private const float PortraitCardTextScale = 1.1f;
        private const float WorkbenchTransitionDuration = 0.26f;
        private const float SlotDoubleClickWindowSeconds = 0.65f;
        private const string ProjectileSubjectResourcePath =
            "RuleforgeTD/UI/Loadout/RuleforgeSubjectProjectile";
        private const string EnemySubjectResourcePath =
            "RuleforgeTD/UI/Loadout/RuleforgeSubjectEnemy";
        private const string WorkbenchBackdropResourcePath =
            "RuleforgeTD/UI/Backgrounds/" +
            "RuleforgeLoadoutParchmentWorkbench_1672x941";

        private static readonly Color ParchmentRaised =
            Color.white;
        private static readonly Color WoodLine =
            new Color32(155, 112, 60, 210);
        private static readonly Color ButtonColor =
            new Color32(73, 48, 31, 255);
        private static readonly Color ActiveColor =
            new Color32(210, 151, 57, 255);
        private static readonly Color ProjectileColor =
            new Color32(211, 105, 37, 255);
        private static readonly Color EnemyColor =
            new Color32(182, 48, 56, 255);
        private static readonly Color LockedColor =
            new Color32(52, 48, 42, 255);
        private static readonly Color LockedDescriptionColor =
            new Color32(174, 166, 148, 255);
        private static readonly Color TextColor =
            new Color32(244, 232, 197, 255);
        private static readonly Color MutedTextColor =
            new Color32(201, 184, 145, 255);
        private static readonly Color ParchmentTextColor =
            new Color32(66, 39, 23, 255);
        private static readonly Color ParchmentMutedTextColor =
            new Color32(112, 75, 43, 255);
        private static readonly Color ScrollbarColor =
            new Color32(31, 22, 17, 238);

        private sealed class TowerPreviewBinding
        {
            public SpriteRenderer Source;
            public Image Image;
        }

        private StageOneUiTextCatalog catalog;
        private Font font;
        private Canvas canvas;
        private RectTransform workbenchBackdropRoot;
        private StageOneBlueprintGridGraphic backdropGraphic;
        private CanvasGroup workbenchBackdropCanvasGroup;
        private Image workbenchBackdropImage;
        private RectTransform panelRoot;
        private CanvasGroup panelCanvasGroup;
        private Image settingsTintImage;
        private RectTransform towerPreviewFrame;
        private Image towerPreviewBackplate;
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
        private readonly RectTransform[] slotRowRoots =
            new RectTransform[MaximumSlots];
        private readonly Image[] slotRowDropSurfaces =
            new Image[MaximumSlots];
        private readonly Button[] slotButtons =
            new Button[MaximumSlots];
        private readonly Text[] slotLabels =
            new Text[MaximumSlots];
        private readonly Image[] slotCardArtworks =
            new Image[MaximumSlots];
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
        private readonly StageOneHoverRelay[]
            slotSubjectHoverRelays =
                new StageOneHoverRelay[MaximumSlots];
        private readonly bool[] slotHasCards =
            new bool[MaximumSlots];
        private readonly SubjectType[] slotSubjectTypes =
            new SubjectType[MaximumSlots];
        private readonly StageOneCardDropSlot[] slotDropTargets =
            new StageOneCardDropSlot[MaximumSlots];
        // 카드 카탈로그 크기는 콘텐츠 데이터가 소유한다. 이 목록은 현재
        // 인벤토리 수만큼 런타임에 확장되며, 16장 같은 UI 상한으로 신규
        // 콘텐츠가 조용히 잘리지 않게 한다. 줄어든 뒤 남는 뷰는 풀처럼
        // 비활성화해 반복 새로고침에서 GameObject를 다시 만들지 않는다.
        private readonly List<StageOneCardView> cardViews =
            new List<StageOneCardView>(InitialCardViewCapacity);
        private readonly List<StageOneCardDragSource> cardDragSources =
            new List<StageOneCardDragSource>(InitialCardViewCapacity);
        private readonly List<StageOneHoverRelay> cardHoverRelays =
            new List<StageOneHoverRelay>(InitialCardViewCapacity);
        private readonly List<int> visibleCardInstanceIds =
            new List<int>(InitialCardViewCapacity);
        private readonly List<int> visibleCardSlotIndices =
            new List<int>(InitialCardViewCapacity);
        private readonly List<float> lastCardClickTimes =
            new List<float>(InitialCardViewCapacity);
        private readonly List<int>
            lastCardClickStartedSlotIndices =
                new List<int>(InitialCardViewCapacity);
        private readonly int[] presentedSlotCardInstanceIds =
        {
            -1,
            -1,
            -1
        };
        private readonly List<StageOneLoadoutCard> presentedCards =
            new List<StageOneLoadoutCard>(InitialCardViewCapacity);
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
        private RectTransform hoverPopupRoot;
        private Image hoverPopupBackground;
        private Text hoverPopupTitle;
        private Text hoverPopupBody;
        private StageOneCardUsageMiniMapGraphic usageMiniMap;
        private RectTransform activeHoverSource;
        private int activeHoveredCardIndex = -1;

        public event Action<int> SlotRequested;
        public event Action<int> CardRequested;
        public event Action<int, int> CardDoubleClickRequested;
        public event Action<int, int> CardDropped;
        public event Action UpgradeRequested;
        public event Action<SubjectType> SubjectTypeRequested;
        public event Action<int, SubjectType>
            SlotSubjectTypeRequested;
        public event Action<int> SlotUnequipRequested;
        public event Action CloseRequested;

        public bool IsVisible =>
            workbenchBackdropRoot != null &&
            workbenchBackdropRoot.gameObject.activeSelf;
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
        public RectTransform CardViewport => cardViewport;
        public RectTransform TowerPreviewContent =>
            towerPreviewContent;
        public Image TowerPreviewBackplate =>
            towerPreviewBackplate;
        public Transform TowerPreviewSource =>
            towerPreviewSource;
        public StageOneBlueprintGridGraphic BlueprintGraphic =>
            backdropGraphic;
        public StageOneBlueprintGridGraphic BackdropGraphic =>
            backdropGraphic;
        public Image WorkbenchBackdropImage =>
            workbenchBackdropImage;
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
        public RectTransform HoverPopupRoot => hoverPopupRoot;
        public Text HoverPopupTitle => hoverPopupTitle;
        public Text HoverPopupBody => hoverPopupBody;
        public StageOneCardUsageMiniMapGraphic UsageMiniMap =>
            usageMiniMap;
        public bool IsPortraitLayout => portraitLayout;
        public int ActiveHoveredCardIndex => activeHoveredCardIndex;
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

            BeginWorkbenchTransition(true);
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
            bool canAffordUpgrade = true,
            int maximumLevel = int.MaxValue,
            bool canUpgradeNow = true,
            bool isMaximumLevel = false)
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
                canAffordUpgrade,
                maximumLevel,
                canUpgradeNow,
                isMaximumLevel);
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
            TowerUpgradeQuote upgradeQuote)
        {
            Show(
                towerName,
                level,
                subjects,
                unlockedSlotCount,
                slotCardInstanceIds,
                cards,
                activeSlot,
                canEdit,
                upgradeQuote.HasNextLevel
                    ? upgradeQuote.Cost
                    : -1,
                upgradeQuote.CanAfford,
                upgradeQuote.MaximumLevel,
                upgradeQuote.IsEligible,
                upgradeQuote.IsMaximumLevel);
        }

        /// <summary>
        /// 호환용 원시 값 오버로드다. 게임 흐름에서는 규칙 계층의
        /// <see cref="TowerUpgradeQuote"/> 오버로드를 사용한다.
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
            bool canAffordUpgrade = true,
            int maximumLevel = int.MaxValue,
            bool canUpgradeNow = true,
            bool isMaximumLevel = false)
        {
            BuildInterface();
            int resolvedMaximumLevel =
                Math.Max(1, maximumLevel);
            bool alreadyOpen = IsVisible;
            HideHoverPopup();
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

            workbenchBackdropRoot.gameObject.SetActive(true);
            panelRoot.gameObject.SetActive(true);
            titleText.text = catalog.Format(
                "tower_panel.title_format",
                towerName,
                Mathf.Clamp(
                    level,
                    1,
                    resolvedMaximumLevel));

            inventoryTitleText.text =
                catalog.Get("tower_panel.inventory");

            upgradeButton.interactable =
                editable &&
                canUpgradeNow &&
                canAffordUpgrade;
            upgradeLabel.text = isMaximumLevel
                ? catalog.Get("tower_panel.max_level_compact")
                : upgradeCost >= 0
                    ? catalog.Format(
                        "tower_panel.level_up_cost_compact_format",
                        upgradeCost)
                    : catalog.Get("tower_panel.level_up_compact");
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
                BeginWorkbenchTransition(true);
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
                workbenchBackdropRoot == null)
            {
                return;
            }

            HideHoverPopup();

            if (!hasEverShown ||
                !Application.isPlaying ||
                !workbenchBackdropRoot.gameObject.activeSelf)
            {
                HideImmediately();
                return;
            }

            if (transitionRunning && !transitionShowing)
            {
                return;
            }

            BeginWorkbenchTransition(false);
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

        /// <summary>
        /// Supplies an intentionally simplified stage overview for equipped
        /// card hover. The custom graphic owns only presentation colors and
        /// never reads or mutates placement state.
        /// </summary>
        public void SetMapOverview(
            IReadOnlyList<Vector2> pathPoints,
            IReadOnlyList<StageOneLoadoutMapSite> buildSites)
        {
            BuildInterface();
            usageMiniMap.SetMap(pathPoints, buildSites);
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

        public Image GetSlotCardArtwork(int index)
        {
            ValidateIndex(index, slotCardArtworks.Length);
            return slotCardArtworks[index];
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

        public StageOneHoverRelay GetSlotSubjectHoverRelay(
            int index)
        {
            ValidateIndex(index, slotSubjectHoverRelays.Length);
            return slotSubjectHoverRelays[index];
        }

        public StageOneHoverRelay GetCardHoverRelay(int index)
        {
            ValidateIndex(index, cardHoverRelays.Count);
            return cardHoverRelays[index];
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
            ValidateIndex(index, cardViews.Count);
            return cardViews[index].Button;
        }

        public StageOneCardView GetCardView(int index)
        {
            ValidateIndex(index, cardViews.Count);
            return cardViews[index];
        }

        public StageOneCardDropSlot GetSlotDropTarget(int index)
        {
            ValidateIndex(index, slotDropTargets.Length);
            return slotDropTargets[index];
        }

        public Image GetSlotDropSurface(int index)
        {
            ValidateIndex(index, slotRowDropSurfaces.Length);
            return slotRowDropSurfaces[index];
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
            HideHoverPopup();
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
            projectileSubjectSprite =
                Resources.Load<Sprite>(
                    ProjectileSubjectResourcePath);
            enemySubjectSprite =
                Resources.Load<Sprite>(
                    EnemySubjectResourcePath);

            var canvasObject = new GameObject(
                "Tower Loadout Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            // The blueprint is a full-screen modal. Keep its authored frame
            // above persistent battle navigation as well as the HUD so the
            // stage-return button cannot cut through the title in portrait.
            canvas.sortingOrder = 900;
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
                "Full Screen Parchment Workbench",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(StageOneBlueprintGridGraphic),
                typeof(CanvasGroup));
            backdropObject.transform.SetParent(
                canvasObject.transform,
                false);
            workbenchBackdropRoot =
                backdropObject.GetComponent<RectTransform>();
            Stretch(workbenchBackdropRoot, 0f);
            backdropGraphic =
                backdropObject.GetComponent<
                    StageOneBlueprintGridGraphic>();
            backdropGraphic.Configure(
                new Color32(49, 27, 16, 255),
                Color.clear,
                Color.clear,
                24f);
            backdropGraphic.raycastTarget = true;
            backdropGraphic.SetRevealProgress(0f);
            workbenchBackdropCanvasGroup =
                backdropObject.GetComponent<CanvasGroup>();
            workbenchBackdropCanvasGroup.alpha = 0f;

            var parchmentObject = new GameObject(
                "Parchment Drafting Surface",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(AspectRatioFitter));
            parchmentObject.transform.SetParent(
                workbenchBackdropRoot,
                false);
            RectTransform parchmentRect =
                parchmentObject.GetComponent<RectTransform>();
            Stretch(parchmentRect, 0f);
            workbenchBackdropImage =
                parchmentObject.GetComponent<Image>();
            workbenchBackdropImage.sprite =
                RuleforgeUiTextureSampling.ConfigureResponsive(
                    Resources.Load<Sprite>(
                        WorkbenchBackdropResourcePath));
            workbenchBackdropImage.type = Image.Type.Simple;
            workbenchBackdropImage.preserveAspect = false;
            workbenchBackdropImage.color = Color.white;
            workbenchBackdropImage.raycastTarget = false;
            AspectRatioFitter backdropAspect =
                parchmentObject.GetComponent<AspectRatioFitter>();
            backdropAspect.aspectMode =
                AspectRatioFitter.AspectMode.EnvelopeParent;
            backdropAspect.aspectRatio = 1672f / 941f;

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
                "Tower Parchment Workbench",
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
            CreateHoverPopup();
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
                ParchmentTextColor,
                TextAnchor.MiddleLeft);
            titleText.resizeTextForBestFit = true;
            titleText.resizeTextMinSize = 22;
            titleText.resizeTextMaxSize = 28;
            sectionTitleText = CreateText(
                "Workbench Title",
                panelRoot,
                15,
                FontStyle.Bold,
                ParchmentMutedTextColor,
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
            var previewObject = new GameObject(
                "Selected Tower Preview",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            towerPreviewFrame =
                previewObject.GetComponent<RectTransform>();
            towerPreviewFrame.SetParent(panelRoot, false);
            towerPreviewBackplate =
                previewObject.GetComponent<Image>();
            towerPreviewBackplate.raycastTarget = false;
            RuleforgePixelUi.ApplyExactPanel(
                towerPreviewBackplate,
                RuleforgeExactPanelAsset.TowerPreviewLandscape280x660,
                Color.white);
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
                TextColor,
                TextAnchor.MiddleCenter);
            towerPreviewPlaceholder.text =
                catalog.Get("tower_panel.preview_title");
            Stretch(towerPreviewPlaceholder.rectTransform, 24f);
        }

        private void CreateSettingsSurface()
        {
            settingsTintImage = CreatePanelImage(
                "Subject Workbench Tint",
                panelRoot,
                Color.clear,
                false);
            effectBackplate = CreatePanelImage(
                "Current Effect Backplate",
                panelRoot,
                ParchmentRaised,
                false);
            RuleforgePixelUi.ApplyExactPanel(
                effectBackplate,
                RuleforgeExactPanelAsset.Effect876x120,
                Color.white);

            effectTitleText = CreateText(
                "Current Effect Title",
                panelRoot,
                14,
                FontStyle.Bold,
                ParchmentMutedTextColor,
                TextAnchor.MiddleLeft);
            effectTitleText.text =
                catalog.Get("tower_panel.active_effect");

            targetTitleText = CreateText(
                "Target Selection Title",
                panelRoot,
                12,
                FontStyle.Bold,
                ParchmentMutedTextColor,
                TextAnchor.MiddleCenter);
            targetTitleText.text =
                catalog.Get("tower_panel.target_title");

            instructionText = CreateText(
                "Loadout Instruction",
                panelRoot,
                12,
                FontStyle.Normal,
                ParchmentTextColor,
                TextAnchor.UpperLeft);
            instructionText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            instructionText.verticalOverflow =
                VerticalWrapMode.Truncate;
            instructionText.resizeTextForBestFit = true;
            instructionText.lineSpacing = 0.95f;
        }

        private Button CreateSubjectButton(
            string objectName,
            Transform parent,
            out Image artwork,
            out Text fallback,
            out Image accentLine)
        {
            Button button = CreateButton(
                objectName,
                parent,
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
                var rowObject = new GameObject(
                    "Tower Slot " + (slot + 1) + " Drop Row",
                    typeof(RectTransform));
                rowObject.transform.SetParent(panelRoot, false);
                slotRowRoots[slot] =
                    rowObject.GetComponent<RectTransform>();

                Image rowDropSurface = CreatePanelImage(
                    "Drop Surface",
                    slotRowRoots[slot],
                    Color.clear,
                    false);
                // The transparent row graphic receives raycasts in the gaps
                // between its three visible panels. Child graphics bubble
                // IDropHandler events to the same row component.
                rowDropSurface.raycastTarget = true;
                slotRowDropSurfaces[slot] = rowDropSurface;

                slotButtons[slot] = CreateButton(
                    "Tower Slot " + (slot + 1),
                    slotRowRoots[slot],
                    ButtonColor,
                    out slotLabels[slot],
                    14);
                slotButtons[slot].onClick.AddListener(
                    () => HandleSlotClicked(capturedSlot));
                slotCardArtworks[slot] = new GameObject(
                    "Equipped Card Artwork",
                    typeof(RectTransform),
                    typeof(Image))
                    .GetComponent<Image>();
                slotCardArtworks[slot].transform.SetParent(
                    slotButtons[slot].transform,
                    false);
                slotCardArtworks[slot].preserveAspect = true;
                slotCardArtworks[slot].raycastTarget = false;
                Stretch(
                    slotCardArtworks[slot].rectTransform,
                    11f);
                slotCardArtworks[slot].gameObject.SetActive(false);

                Image descriptionBackplate = CreatePanelImage(
                    "Slot " + (slot + 1) + " Description",
                    slotRowRoots[slot],
                    ParchmentRaised,
                    false);
                RuleforgePixelUi.ApplyExactPanel(
                    descriptionBackplate,
                    RuleforgeExactPanelAsset.Slot670x80,
                    Color.white);
                slotDescriptionBackplates[slot] =
                    descriptionBackplate;
                slotDescriptionTexts[slot] = CreateText(
                    "Description",
                    descriptionBackplate.transform,
                    14,
                    FontStyle.Normal,
                    ParchmentTextColor,
                    TextAnchor.MiddleLeft);
                slotDescriptionTexts[slot].horizontalOverflow =
                    HorizontalWrapMode.Wrap;
                slotDescriptionTexts[slot].verticalOverflow =
                    VerticalWrapMode.Truncate;
                slotDescriptionTexts[slot].lineSpacing = 1.05f;
                Stretch(
                    slotDescriptionTexts[slot].rectTransform,
                    14f);

                Button subjectToggle =
                    CreateSubjectButton(
                        "Slot " + (slot + 1) +
                        " Subject Toggle",
                        slotRowRoots[slot],
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
                StageOneHoverRelay subjectHover =
                    subjectToggle.gameObject.AddComponent<
                        StageOneHoverRelay>();
                subjectHover.Entered += source =>
                    HandleSubjectHoverEntered(
                        capturedSlot,
                        source);
                subjectHover.Exited += HandleHoverExited;
                slotSubjectHoverRelays[slot] = subjectHover;
                ConfigureSubjectArtwork(
                    slotProjectileArtworks[slot],
                    slotProjectileFallbacks[slot],
                    projectileSubjectSprite);

                StageOneCardDropSlot dropTarget =
                    slotRowRoots[slot].gameObject
                        .AddComponent<StageOneCardDropSlot>();
                dropTarget.Configure(slot, false);
                dropTarget.SetHighlight(
                    slotDescriptionBackplates[slot],
                    ActiveColor);
                dropTarget.DropRequested += HandleCardDropped;
                slotDropTargets[slot] = dropTarget;

                StageOneSlotDoubleClickRelay clickRelay =
                    AddSlotDoubleClickRelay(
                        slotButtons[slot].gameObject,
                        slot);
                slotDoubleClickRelays[slot] = clickRelay;
                AddSlotDoubleClickRelay(
                    slotRowRoots[slot].gameObject,
                    slot);
            }

            inventoryTitleText = CreateText(
                "Owned Cards Title",
                panelRoot,
                17,
                FontStyle.Bold,
                ParchmentTextColor,
                TextAnchor.MiddleLeft);
        }

        private StageOneSlotDoubleClickRelay
            AddSlotDoubleClickRelay(
                GameObject target,
                int slotIndex)
        {
            StageOneSlotDoubleClickRelay relay =
                target.AddComponent<
                    StageOneSlotDoubleClickRelay>();
            relay.Configure(slotIndex);
            relay.DoubleClicked += HandleSlotDoubleClicked;
            return relay;
        }

        private void CreateHoverPopup()
        {
            hoverPopupBackground = CreatePanelImage(
                "Loadout Hover Popup",
                panelRoot,
                Color.white,
                false);
            RuleforgePixelUi.ApplyPanel(
                hoverPopupBackground,
                RuleforgePixelPanelRole.Parchment,
                Color.white);
            hoverPopupBackground.raycastTarget = false;
            hoverPopupRoot = hoverPopupBackground.rectTransform;
            hoverPopupRoot.anchorMin =
                new Vector2(0.5f, 0.5f);
            hoverPopupRoot.anchorMax =
                new Vector2(0.5f, 0.5f);
            hoverPopupRoot.pivot =
                new Vector2(0.5f, 0.5f);
            hoverPopupRoot.sizeDelta =
                new Vector2(360f, 142f);

            hoverPopupTitle = CreateText(
                "Hover Popup Title",
                hoverPopupRoot,
                16,
                FontStyle.Bold,
                ParchmentTextColor,
                TextAnchor.UpperLeft);
            hoverPopupTitle.resizeTextForBestFit = true;
            hoverPopupTitle.resizeTextMinSize = 12;
            hoverPopupTitle.resizeTextMaxSize = 16;
            SetRect(
                hoverPopupTitle.rectTransform,
                24f,
                98f,
                312f,
                24f);

            hoverPopupBody = CreateText(
                "Hover Popup Body",
                hoverPopupRoot,
                13,
                FontStyle.Normal,
                ParchmentMutedTextColor,
                TextAnchor.UpperLeft);
            hoverPopupBody.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            hoverPopupBody.verticalOverflow =
                VerticalWrapMode.Truncate;
            hoverPopupBody.resizeTextForBestFit = true;
            hoverPopupBody.resizeTextMinSize = 10;
            hoverPopupBody.resizeTextMaxSize = 13;
            SetRect(
                hoverPopupBody.rectTransform,
                24f,
                22f,
                312f,
                70f);

            usageMiniMap = new GameObject(
                "Card Usage Mini Map",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(StageOneCardUsageMiniMapGraphic))
                .GetComponent<StageOneCardUsageMiniMapGraphic>();
            usageMiniMap.transform.SetParent(
                hoverPopupRoot,
                false);
            SetRect(
                usageMiniMap.rectTransform,
                24f,
                24f,
                252f,
                162f);
            usageMiniMap.gameObject.SetActive(false);
            hoverPopupRoot.gameObject.SetActive(false);
            hoverPopupRoot.SetAsLastSibling();
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
            RuleforgePixelUi.ApplyExactPanel(
                viewportImage,
                RuleforgeExactPanelAsset.Inventory862x208,
                Color.white);
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
                new RectOffset(4, 4, 4, 4);

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
        }

        private void EnsureCardViewCapacity(int requiredCount)
        {
            int normalizedCount = Math.Max(0, requiredCount);
            while (cardViews.Count < normalizedCount)
            {
                int capturedIndex = cardViews.Count;
                StageOneCardView cardView =
                    StageOneCardView.CreateRuntime(
                        "Inventory Card " +
                        (capturedIndex + 1),
                        cardContent,
                        font);
                cardView.Button.onClick.AddListener(
                    () => HandleCard(capturedIndex));
                StageOneCardDragSource dragSource =
                    cardView.gameObject.AddComponent<
                        StageOneCardDragSource>();
                dragSource.Configure(-1, canvas, false);
                StageOneHoverRelay hoverRelay =
                    cardView.gameObject.AddComponent<
                        StageOneHoverRelay>();
                hoverRelay.Entered += source =>
                    HandleCardHoverEntered(
                        capturedIndex,
                        source);
                hoverRelay.Exited += HandleHoverExited;

                cardViews.Add(cardView);
                cardDragSources.Add(dragSource);
                cardHoverRelays.Add(hoverRelay);
                visibleCardInstanceIds.Add(-1);
                visibleCardSlotIndices.Add(-1);
                lastCardClickTimes.Add(float.NegativeInfinity);
                lastCardClickStartedSlotIndices.Add(-1);
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
            towerPreviewFrame.gameObject.SetActive(true);
            towerPreviewBackplate.enabled = true;
            panelRoot.sizeDelta =
                new Vector2(LandscapeWidth, LandscapeHeight);
            SetRect(titleText.rectTransform, 150f, 754f, 668f, 48f);
            SetRect(
                sectionTitleText.rectTransform,
                336f,
                724f,
                670f,
                28f);
            SetRect(
                upgradeButton.GetComponent<RectTransform>(),
                1264f,
                754f,
                132f,
                44f);
            SetRect(
                closeButton.GetComponent<RectTransform>(),
                1420f,
                760f,
                36f,
                36f);

            SetRect(
                towerPreviewFrame,
                28f,
                52f,
                280f,
                660f);
            SetRect(
                settingsTintImage.rectTransform,
                326f,
                52f,
                696f,
                660f);
            SetRect(
                effectBackplate.rectTransform,
                336f,
                584f,
                670f,
                120f);
            SetRect(
                effectTitleText.rectTransform,
                364f,
                664f,
                344f,
                20f);
            SetRect(
                instructionText.rectTransform,
                364f,
                608f,
                614f,
                46f);
            SetRect(
                targetTitleText.rectTransform,
                928f,
                563f,
                80f,
                16f);

            float[] slotY = { 486f, 392f, 298f };
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                Stretch(slotRowRoots[slot], 0f);
                SetRect(
                    slotRowDropSurfaces[slot].rectTransform,
                    336f,
                    slotY[slot],
                    672f,
                    80f);
                SetRect(
                    slotButtons[slot]
                        .GetComponent<RectTransform>(),
                    336f,
                    slotY[slot],
                    80f,
                    80f);
                SetRect(
                    slotDescriptionBackplates[slot]
                        .rectTransform,
                    428f,
                    slotY[slot],
                    480f,
                    80f);
                SetRect(
                    slotProjectileButtons[slot]
                        .GetComponent<RectTransform>(),
                    928f,
                    slotY[slot],
                    80f,
                    80f);
            }

            SetRect(
                inventoryTitleText.rectTransform,
                1040f,
                724f,
                380f,
                28f);
            SetRect(cardViewport, 1040f, 52f, 380f, 660f);
            SetRect(scrollbarRect, 1428f, 52f, 14f, 660f);
            ApplyExactLandscapeAssets();
            LayoutInventoryCards();
        }

        private void ApplyPortraitLayout()
        {
            ApplyTypographyScale(true);
            towerPreviewFrame.gameObject.SetActive(true);
            towerPreviewBackplate.enabled = false;
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
                80f,
                810f,
                394f,
                24f);
            SetRect(
                instructionText.rectTransform,
                80f,
                720f,
                598f,
                76f);
            SetRect(
                targetTitleText.rectTransform,
                620f,
                670f,
                94f,
                16f);

            float[] slotY = { 574f, 460f, 346f };
            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                Stretch(slotRowRoots[slot], 0f);
                SetRect(
                    slotRowDropSurfaces[slot].rectTransform,
                    48f,
                    slotY[slot],
                    666f,
                    94f);
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
            ApplyExactPortraitAssets();
            LayoutInventoryCards();
        }

        private void ApplyExactLandscapeAssets()
        {
            ApplyExactButtonAsset(
                upgradeButton,
                RuleforgeExactButtonAsset.Upgrade132x44);
            ApplyExactButtonAsset(
                closeButton,
                RuleforgeExactButtonAsset.Square36);
            RuleforgePixelUi.ApplyExactPanel(
                towerPreviewBackplate,
                RuleforgeExactPanelAsset.TowerPreviewLandscape280x660,
                Color.white);
            RuleforgePixelUi.ApplyExactPanel(
                effectBackplate,
                RuleforgeExactPanelAsset.EffectLandscapeMiddle670x120,
                Color.white);
            RuleforgePixelUi.ApplyExactPanel(
                cardViewport.GetComponent<Image>(),
                RuleforgeExactPanelAsset.InventoryLandscapeSide380x660,
                Color.white);

            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                ApplyExactButtonAsset(
                    slotButtons[slot],
                    RuleforgeExactButtonAsset.Square80);
                ApplyExactButtonAsset(
                    slotProjectileButtons[slot],
                    RuleforgeExactButtonAsset.Square80);
                RuleforgePixelUi.ApplyExactPanel(
                    slotDescriptionBackplates[slot],
                    RuleforgeExactPanelAsset.SlotLandscapeMiddle480x80,
                    slotDescriptionBackplates[slot].color);
            }
        }

        private void ApplyExactPortraitAssets()
        {
            ApplyExactButtonAsset(
                upgradeButton,
                RuleforgeExactButtonAsset.UpgradePortrait200x90);
            ApplyExactButtonAsset(
                closeButton,
                RuleforgeExactButtonAsset.Square90);
            RuleforgePixelUi.ApplyExactPanel(
                effectBackplate,
                RuleforgeExactPanelAsset.EffectPortrait666x170,
                Color.white);
            RuleforgePixelUi.ApplyExactPanel(
                cardViewport.GetComponent<Image>(),
                RuleforgeExactPanelAsset.InventoryPortrait646x252,
                Color.white);

            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                ApplyExactButtonAsset(
                    slotButtons[slot],
                    RuleforgeExactButtonAsset.Square94);
                ApplyExactButtonAsset(
                    slotProjectileButtons[slot],
                    RuleforgeExactButtonAsset.Square94);
                RuleforgePixelUi.ApplyExactPanel(
                    slotDescriptionBackplates[slot],
                    RuleforgeExactPanelAsset.SlotPortrait448x94,
                    slotDescriptionBackplates[slot].color);
            }
        }

        private static void ApplyExactButtonAsset(
            Button button,
            RuleforgeExactButtonAsset asset)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.targetGraphic as Image;
            RuleforgePixelButtonSkin skin =
                button.GetComponent<RuleforgePixelButtonSkin>();
            RuleforgePixelUi.ApplyExact(
                button,
                asset,
                skin == null
                    ? RuleforgePixelButtonRole.Secondary
                    : skin.Role,
                image == null ? Color.white : image.color);
        }

        private void ApplyTypographyScale(bool portrait)
        {
            titleText.fontSize = portrait ? 34 : 28;
            titleText.resizeTextMinSize = portrait ? 22 : 18;
            titleText.resizeTextMaxSize = portrait ? 34 : 28;
            sectionTitleText.fontSize = portrait ? 18 : 15;
            upgradeLabel.fontSize = portrait ? 18 : 14;
            Text closeLabel =
                closeButton.GetComponentInChildren<Text>(true);
            if (closeLabel != null)
            {
                closeLabel.fontSize = portrait ? 26 : 16;
            }

            towerPreviewPlaceholder.fontSize = portrait ? 26 : 18;
            effectTitleText.fontSize = portrait ? 16 : 13;
            targetTitleText.fontSize = portrait ? 16 : 12;
            instructionText.fontSize = portrait ? 14 : 12;
            instructionText.resizeTextMinSize = portrait ? 10 : 9;
            instructionText.resizeTextMaxSize =
                instructionText.fontSize;
            inventoryTitleText.fontSize = portrait ? 21 : 17;

            for (int slot = 0; slot < MaximumSlots; slot++)
            {
                slotLabels[slot].fontSize =
                    portrait ? 18 : 14;
                slotDescriptionTexts[slot].fontSize =
                    portrait ? 17 : 14;
                slotProjectileFallbacks[slot].fontSize =
                    portrait ? 32 : 24;
            }

            float cardTextScale = portrait
                ? PortraitCardTextScale
                : 1f;
            for (int i = 0; i < cardViews.Count; i++)
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
                slotButtons[slot].interactable = unlocked;
                slotProjectileButtons[slot].interactable =
                    unlocked && editable;
                slotSubjectHoverRelays[slot].SetHoverEnabled(
                    unlocked);
                slotDropTargets[slot].SetDropEnabled(
                    unlocked && editable);
                slotLabels[slot].text = !unlocked
                    ? GetLockedSlotLabel()
                    : cardInstanceId < 0
                        ? catalog.Get("tower_panel.empty")
                        : ResolveSlotCardLabel(
                            cards,
                            cardInstanceId);
                Sprite cardArtwork = unlocked &&
                        cardInstanceId >= 0
                    ? ResolveSlotCardArtwork(
                        cards,
                        cardInstanceId)
                    : null;
                slotCardArtworks[slot].sprite = cardArtwork;
                slotCardArtworks[slot].color = Color.white;
                slotCardArtworks[slot].gameObject.SetActive(
                    cardArtwork != null);
                slotLabels[slot].gameObject.SetActive(
                    cardArtwork == null);
                slotDescriptionTexts[slot].color =
                    unlocked
                        ? ParchmentTextColor
                        : ParchmentMutedTextColor;
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
            visibleCardCount = cards == null
                ? 0
                : cards.Count;
            EnsureCardViewCapacity(visibleCardCount);
            float cardTextScale = portraitLayout
                ? PortraitCardTextScale
                : 1f;
            for (int i = 0; i < cardViews.Count; i++)
            {
                bool visible = i < visibleCardCount;
                cardViews[i].gameObject.SetActive(visible);
                if (!visible)
                {
                    visibleCardInstanceIds[i] = -1;
                    visibleCardSlotIndices[i] = -1;
                    lastCardClickStartedSlotIndices[i] = -1;
                    cardDragSources[i].Configure(
                        -1,
                        canvas,
                        false);
                    cardHoverRelays[i].SetHoverEnabled(false);
                    continue;
                }

                StageOneLoadoutCard card = cards[i];
                cardViews[i].SetTextScale(cardTextScale);
                if (visibleCardInstanceIds[i] != card.InstanceId)
                {
                    lastCardClickTimes[i] =
                        float.NegativeInfinity;
                    lastCardClickStartedSlotIndices[i] = -1;
                }
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
                        card.Display.Tier,
                        card.Display.SymbolKey);
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
                        card.Display.SymbolKey));
                cardDragSources[i].Configure(
                    card.InstanceId,
                    canvas,
                    editable);
                cardHoverRelays[i].SetHoverEnabled(
                    card.Equipped);
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
                ? 171f
                : InventoryCardWidth;
            float cardHeight = portraitLayout
                ? 292f
                : InventoryCardHeight;
            int rows = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    visibleCardCount /
                    (float)columns));
            float contentHeight = Mathf.Max(
                cardViewport.rect.height,
                cardGridLayout.padding.vertical +
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
            effectBackplate.color = Color.white;
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
                    ? Color.white
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
                    ? WoodLine
                    : slot == selectedSlot
                        ? ActiveColor
                        : targetColor);
            SetOutlineColor(
                slotDescriptionBackplates[slot].gameObject,
                WoodLine);
            SetOutlineColor(
                slotProjectileButtons[slot].gameObject,
                WoodLine);
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

        private void HandleSubjectHoverEntered(
            int slot,
            RectTransform source)
        {
            if (slot < 0 ||
                slot >= MaximumSlots ||
                source == null ||
                !slotButtons[slot].interactable)
            {
                return;
            }

            SubjectType subjectType = slotSubjectTypes[slot];
            StageOneLoadoutCard card = default;
            bool hasCard = TryFindPresentedCard(
                presentedSlotCardInstanceIds[slot],
                out card);
            string title = catalog.Get(
                subjectType == SubjectType.Enemy
                    ? "tower_panel.subject_enemy_title"
                    : "tower_panel.subject_projectile_title");
            string body;
            if (!hasCard)
            {
                body = catalog.Get(
                    subjectType == SubjectType.Enemy
                        ? "tower_panel.subject_enemy_empty_tooltip"
                        : "tower_panel.subject_projectile_empty_tooltip");
            }
            else
            {
                string description =
                    card.Display.GetDescription(subjectType);
                body = catalog.Format(
                    subjectType == SubjectType.Enemy
                        ? "tower_panel.subject_enemy_tooltip_format"
                        : "tower_panel.subject_projectile_tooltip_format",
                    card.Display.Name,
                    description);
            }

            ShowHoverPopup(
                source,
                title,
                body,
                false,
                -1);
        }

        private void HandleCardHoverEntered(
            int visibleIndex,
            RectTransform source)
        {
            if (visibleIndex < 0 ||
                visibleIndex >= visibleCardCount ||
                visibleIndex >= presentedCards.Count ||
                source == null)
            {
                return;
            }

            StageOneLoadoutCard card =
                presentedCards[visibleIndex];
            if (!card.Equipped)
            {
                return;
            }

            activeHoveredCardIndex = visibleIndex;
            ShowHoverPopup(
                source,
                catalog.Format(
                    "tower_panel.usage_map_title_format",
                    card.Display.Name),
                catalog.Get("tower_panel.usage_map_body"),
                true,
                card.EquippedTowerId);
        }

        private void ShowHoverPopup(
            RectTransform source,
            string title,
            string body,
            bool showMap,
            int focusedTowerId)
        {
            if (hoverPopupRoot == null || source == null)
            {
                return;
            }

            activeHoverSource = source;
            if (!showMap)
            {
                activeHoveredCardIndex = -1;
            }

            hoverPopupTitle.text = title ?? string.Empty;
            hoverPopupBody.text = body ?? string.Empty;
            usageMiniMap.gameObject.SetActive(showMap);
            usageMiniMap.SetFocusedTower(
                showMap ? focusedTowerId : -1);

            if (showMap)
            {
                hoverPopupRoot.sizeDelta =
                    new Vector2(300f, 270f);
                SetRect(
                    hoverPopupTitle.rectTransform,
                    24f,
                    224f,
                    252f,
                    24f);
                SetRect(
                    hoverPopupBody.rectTransform,
                    24f,
                    190f,
                    252f,
                    28f);
                SetRect(
                    usageMiniMap.rectTransform,
                    24f,
                    24f,
                    252f,
                    158f);
            }
            else
            {
                hoverPopupRoot.sizeDelta =
                    new Vector2(360f, 142f);
                SetRect(
                    hoverPopupTitle.rectTransform,
                    24f,
                    98f,
                    312f,
                    24f);
                SetRect(
                    hoverPopupBody.rectTransform,
                    24f,
                    22f,
                    312f,
                    70f);
            }

            PositionHoverPopup(source);
            hoverPopupRoot.SetAsLastSibling();
            hoverPopupRoot.gameObject.SetActive(true);
        }

        private void PositionHoverPopup(RectTransform source)
        {
            Vector3[] corners = new Vector3[4];
            source.GetWorldCorners(corners);
            Vector3 centerWorld =
                (corners[0] + corners[2]) * 0.5f;
            Vector2 center = panelRoot.InverseTransformPoint(
                centerWorld);
            Vector2 leftBottom = panelRoot.InverseTransformPoint(
                corners[0]);
            Vector2 rightBottom = panelRoot.InverseTransformPoint(
                corners[3]);
            float sourceWidth = Mathf.Abs(
                rightBottom.x - leftBottom.x);
            Vector2 popupSize = hoverPopupRoot.sizeDelta;
            Rect bounds = panelRoot.rect;
            float direction = center.x >= 0f ? -1f : 1f;
            float x = center.x + direction *
                (sourceWidth * 0.5f +
                 popupSize.x * 0.5f +
                 14f);
            float y = center.y;
            x = Mathf.Clamp(
                x,
                bounds.xMin + popupSize.x * 0.5f + 8f,
                bounds.xMax - popupSize.x * 0.5f - 8f);
            y = Mathf.Clamp(
                y,
                bounds.yMin + popupSize.y * 0.5f + 8f,
                bounds.yMax - popupSize.y * 0.5f - 8f);
            hoverPopupRoot.anchoredPosition =
                new Vector2(x, y);
        }

        private void HandleHoverExited(RectTransform source)
        {
            if (activeHoverSource == source)
            {
                HideHoverPopup();
            }
        }

        private void HideHoverPopup()
        {
            activeHoverSource = null;
            activeHoveredCardIndex = -1;
            if (usageMiniMap != null)
            {
                usageMiniMap.SetFocusedTower(-1);
            }

            if (hoverPopupRoot != null)
            {
                hoverPopupRoot.gameObject.SetActive(false);
            }
        }

        private bool TryFindPresentedCard(
            int instanceId,
            out StageOneLoadoutCard card)
        {
            for (int index = 0;
                 index < presentedCards.Count;
                 index++)
            {
                if (presentedCards[index].InstanceId == instanceId)
                {
                    card = presentedCards[index];
                    return true;
                }
            }

            card = default;
            return false;
        }

        private void BeginWorkbenchTransition(bool showing)
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }

            workbenchBackdropRoot.gameObject.SetActive(true);
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
                    WorkbenchTransitionDuration *
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
            if (workbenchBackdropCanvasGroup != null)
            {
                workbenchBackdropCanvasGroup.alpha = progress;
            }
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
            HideHoverPopup();
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

            if (workbenchBackdropCanvasGroup != null)
            {
                workbenchBackdropCanvasGroup.alpha = 0f;
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

            if (workbenchBackdropRoot != null)
            {
                workbenchBackdropRoot.gameObject.SetActive(false);
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

            float now = Time.unscaledTime;
            bool isDoubleClick =
                editable &&
                now - lastCardClickTimes[visibleIndex] <=
                    SlotDoubleClickWindowSeconds;
            int cardInstanceId =
                visibleCardInstanceIds[visibleIndex];
            if (isDoubleClick)
            {
                lastCardClickTimes[visibleIndex] =
                    float.NegativeInfinity;
                int startedSlotIndex =
                    lastCardClickStartedSlotIndices[visibleIndex];
                lastCardClickStartedSlotIndices[visibleIndex] = -1;
                CardDoubleClickRequested?.Invoke(
                    cardInstanceId,
                    startedSlotIndex);
                return;
            }

            lastCardClickTimes[visibleIndex] = now;
            lastCardClickStartedSlotIndices[visibleIndex] =
                visibleCardSlotIndices[visibleIndex];
            CardRequested?.Invoke(cardInstanceId);
        }

        private void HandleSlotClicked(int slotIndex)
        {
            HandleSlotSelected(slotIndex);
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
                        cards[i].Display.SymbolKey);
                    return string.IsNullOrEmpty(symbol)
                        ? cards[i].Display.Name
                        : symbol;
                }
            }

            return catalog.Get("tower_panel.empty");
        }

        private static Sprite ResolveSlotCardArtwork(
            IReadOnlyList<StageOneLoadoutCard> cards,
            int instanceId)
        {
            if (instanceId < 0 || cards == null)
            {
                return null;
            }

            for (int index = 0; index < cards.Count; index++)
            {
                if (cards[index].InstanceId == instanceId)
                {
                    return StageOneCardArtworkCatalog.Load(
                        cards[index].Display.StableId);
                }
            }

            return null;
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

        private string GetCardSymbol(string symbolKey)
        {
            return string.IsNullOrWhiteSpace(symbolKey)
                ? null
                : catalog.Get(symbolKey);
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
                typeof(Button));
            host.transform.SetParent(parent, false);
            Image image = host.GetComponent<Image>();
            image.color = color;
            Button button = host.GetComponent<Button>();
            button.targetGraphic = image;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
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
            RuleforgePixelButtonRole role = color == ActiveColor
                ? RuleforgePixelButtonRole.Primary
                : RuleforgePixelButtonRole.Secondary;
            RuleforgePixelUi.ApplyTint(button, role, color);
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
                outline.effectColor = WoodLine;
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

        private static void SetButtonColor(
            Button button,
            Color color)
        {
            if (button != null &&
                button.targetGraphic is Image image)
            {
                RuleforgePixelUi.ApplyTint(
                    button,
                    RuleforgePixelButtonRole.Secondary,
                    color);
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
