using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode.UI
{
    public sealed class StageOneHudViewTests
    {
        private const string TestCatalogJson = @"
{
  ""locale"": ""ko-KR"",
  ""strings"": [
    { ""key"": ""hud.summary_format"", ""value"": ""웨이브 {0}/{1} · 본진 {2} · 골드 {3} · {4}"" },
    { ""key"": ""hud.play"", ""value"": ""시작"" },
    { ""key"": ""hud.pause"", ""value"": ""일시정지"" },
    { ""key"": ""hud.resume"", ""value"": ""계속"" },
    { ""key"": ""speed.0_5x"", ""value"": ""0.5배"" },
    { ""key"": ""speed.1x"", ""value"": ""1배"" },
    { ""key"": ""speed.2x"", ""value"": ""2배"" },
    { ""key"": ""speed.3x"", ""value"": ""3배"" },
    { ""key"": ""hud.cards_title"", ""value"": ""장착 카드"" },
    { ""key"": ""hud.empty_card"", ""value"": ""빈 카드 슬롯"" },
    { ""key"": ""hud.card_format"", ""value"": ""{0}\n{1}"" },
    { ""key"": ""reward.title"", ""value"": ""보상 카드 선택"" },
    { ""key"": ""reward.instruction"", ""value"": ""한 장을 선택하세요."" },
    { ""key"": ""reward.card_simple_format"", ""value"": ""{0}\n{1}"" },
    { ""key"": ""phase.planning"", ""value"": ""계획"" },
    { ""key"": ""phase.card_pack_choice"", ""value"": ""카드팩 선택"" },
    { ""key"": ""status.ready"", ""value"": ""준비되었습니다."" },
    { ""key"": ""status.test_format"", ""value"": ""테스트 {0}"" },
    { ""key"": ""tower_panel.empty"", ""value"": ""비어 있음"" },
    { ""key"": ""tower_panel.locked"", ""value"": ""잠김"" },
    { ""key"": ""tower_panel.projectile"", ""value"": ""탄환에 적용"" },
    { ""key"": ""tower_panel.enemy"", ""value"": ""적에게 적용"" },
    { ""key"": ""tower_panel.subject_projectile_short"", ""value"": ""탄"" },
    { ""key"": ""tower_panel.subject_enemy_short"", ""value"": ""적"" },
    { ""key"": ""card_symbol.split"", ""value"": ""×2"" },
    { ""key"": ""card_symbol.pierce"", ""value"": ""→"" },
    { ""key"": ""card_symbol.burn"", ""value"": ""불"" },
    { ""key"": ""card_symbol.slow"", ""value"": ""느림"" },
    { ""key"": ""card_symbol.explode"", ""value"": ""폭발"" },
    { ""key"": ""card_symbol.knockback"", ""value"": ""밀침"" },
    { ""key"": ""card_symbol.mark"", ""value"": ""표식"" },
    { ""key"": ""card_symbol.gold_bounty"", ""value"": ""G"" },
    { ""key"": ""card_symbol.poison"", ""value"": ""독"" },
    { ""key"": ""card_symbol.enlarge"", ""value"": ""+"" },
    { ""key"": ""card_symbol.shrink"", ""value"": ""−"" },
    { ""key"": ""card_symbol.stun"", ""value"": ""기절"" }
  ],
  ""cards"": [
    {
      ""id"": ""split"",
      ""name"": ""분열"",
      ""projectile"": ""탄환이 두 발로 나뉩니다."",
      ""enemy"": ""적이 두 개체로 나뉩니다.""
    },
    {
      ""id"": ""burn"",
      ""name"": ""화상"",
      ""projectile"": ""적중 시 화상을 부여합니다."",
      ""enemy"": ""지속 화염 피해를 받습니다.""
    },
    {
      ""id"": ""poison"",
      ""name"": ""중독"",
      ""projectile"": ""적중 시 중독을 부여합니다."",
      ""enemy"": ""지속 독 피해를 받습니다.""
    }
  ],
  ""towers"": [
    {
      ""id"": ""ballista"",
      ""name"": ""궁수 타워"",
      ""description"": ""카드를 탄환 효과로 실행합니다.""
    }
  ]
}";

        [Test]
        public void TextCatalog_LoadsTypedEntriesAndFallsBackToKeys()
        {
            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);

            Assert.That(catalog.IsLoaded, Is.True);
            Assert.That(catalog.Locale, Is.EqualTo("ko-KR"));
            Assert.That(catalog.Get("hud.play"), Is.EqualTo("시작"));
            Assert.That(
                catalog.GetPhase("CardPackChoice"),
                Is.EqualTo("카드팩 선택"));
            Assert.That(
                catalog.GetCardName("split"),
                Is.EqualTo("분열"));
            Assert.That(
                catalog.GetCardProjectileDescription("split"),
                Does.Contain("두 발"));
            Assert.That(
                catalog.GetCardEnemyDescription("split"),
                Does.Contain("두 개체"));
            Assert.That(
                catalog.GetTowerName("ballista"),
                Is.EqualTo("궁수 타워"));
            Assert.That(
                catalog.GetTowerDescription("ballista"),
                Does.Contain("탄환 효과"));
            Assert.That(
                catalog.Get("missing.prototype.key"),
                Is.EqualTo("missing.prototype.key"));

            StageOneCardDisplay display =
                catalog.GetCardDisplay("poison");
            Assert.That(display.StableId, Is.EqualTo("poison"));
            Assert.That(display.Name, Is.EqualTo("중독"));
            Assert.That(display.Description, Does.Contain("중독"));

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void ResponsiveCanvasScaler_UsesPhoneSizedDesignSurface()
        {
            var host = new GameObject(
                "Responsive Canvas Test",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(StageOneResponsiveCanvasScaler));
            CanvasScaler scaler = host.GetComponent<CanvasScaler>();
            StageOneResponsiveCanvasScaler responsive =
                host.GetComponent<StageOneResponsiveCanvasScaler>();

            responsive.ApplyScale(390, 844, false);
            Assert.That(responsive.IsCompactLayout, Is.True);
            Assert.That(responsive.IsPortraitLayout, Is.True);
            Assert.That(
                scaler.referenceResolution,
                Is.EqualTo(
                    StageOneResponsiveCanvasScaler
                        .CompactPortraitReferenceResolution));

            responsive.ApplyScale(1920, 1080, false);
            Assert.That(responsive.IsCompactLayout, Is.False);
            Assert.That(responsive.IsPortraitLayout, Is.False);
            Assert.That(
                scaler.referenceResolution,
                Is.EqualTo(
                    StageOneResponsiveCanvasScaler
                        .DesktopReferenceResolution));

            responsive.ApplyScale(844, 390, true);
            Assert.That(responsive.IsCompactLayout, Is.True);
            Assert.That(responsive.IsPortraitLayout, Is.False);
            Assert.That(
                scaler.referenceResolution,
                Is.EqualTo(
                    StageOneResponsiveCanvasScaler
                        .CompactLandscapeReferenceResolution));

            Object.DestroyImmediate(host);
        }

        [UnityTest]
        public IEnumerator RuntimeHud_BuildsControlsAndForwardsIntentEvents()
        {
            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);
            StageOneHudView hud =
                StageOneHudView.CreateRuntime(catalog);

            Assert.That(hud.IsBuilt, Is.True);
            Assert.That(hud.HudCanvas, Is.Not.Null);
            Assert.That(
                hud.HudCanvas.GetComponent<
                    StageOneResponsiveCanvasScaler>(),
                Is.Not.Null);
            Assert.That(
                hud.HudCanvas.renderMode,
                Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(hud.IsVisible, Is.True);
            hud.SetVisible(false);
            Assert.That(hud.IsVisible, Is.False);
            Assert.That(hud.HudCanvas.enabled, Is.False);
            Assert.That(
                hud.HudCanvas.GetComponent<GraphicRaycaster>()
                    .enabled,
                Is.False);
            hud.SetVisible(true);
            Assert.That(hud.IsVisible, Is.True);
            Assert.That(hud.HudText.font, Is.Not.Null);
            Assert.That(
                Object.FindObjectOfType<EventSystem>(),
                Is.Not.Null);
            Assert.That(
                hud.EquippedCardPanelCount,
                Is.EqualTo(StageOneHudView.EquippedCardCapacity));
            Assert.That(
                hud.RewardButtonCount,
                Is.EqualTo(StageOneHudView.RewardChoiceCapacity));

            int playRequests = 0;
            int speedRequests = 0;
            float selectedSpeed = -1f;
            int selectedReward = -1;
            hud.PlayRequested += () => playRequests++;
            hud.SpeedRequested += () => speedRequests++;
            hud.SpeedSelected += value => selectedSpeed = value;
            hud.RewardChoiceRequested += index =>
                selectedReward = index;

            hud.SetHud(1, 9, 20, 35, "Planning");
            Assert.That(hud.HudText.text, Does.Contain("웨이브 1/9"));
            Assert.That(hud.HudText.text, Does.Contain("계획"));

            hud.SetPlayState(StageOnePlayState.Ready);
            Assert.That(hud.PlayButton.interactable, Is.True);
            Assert.That(hud.PlayButtonLabel.text, Is.EqualTo("시작"));
            hud.PlayButton.onClick.Invoke();
            Assert.That(playRequests, Is.EqualTo(1));

            hud.SetPlayState(StageOnePlayState.Playing);
            Assert.That(
                hud.PlayButtonLabel.text,
                Is.EqualTo("일시정지"));
            hud.SetPlayState(StageOnePlayState.Paused);
            Assert.That(hud.PlayButtonLabel.text, Is.EqualTo("계속"));

            hud.SetSpeed(2);
            Assert.That(hud.SpeedMultiplier, Is.EqualTo(2f));
            Assert.That(hud.SpeedButtonLabel.text, Is.EqualTo("2배"));
            hud.SpeedButton.onClick.Invoke();
            Assert.That(speedRequests, Is.EqualTo(1));
            Assert.That(selectedSpeed, Is.EqualTo(2f));
            Assert.That(hud.SpeedButtonCount, Is.EqualTo(4));
            hud.GetSpeedButton(0.5f).onClick.Invoke();
            Assert.That(hud.SpeedMultiplier, Is.EqualTo(0.5f));
            Assert.That(selectedSpeed, Is.EqualTo(0.5f));
            hud.GetSpeedButton(3f).onClick.Invoke();
            Assert.That(hud.SpeedMultiplier, Is.EqualTo(3f));
            Assert.That(selectedSpeed, Is.EqualTo(3f));

            hud.SetStatus("status.test_format", 7);
            Assert.That(hud.StatusText.text, Is.EqualTo("테스트 7"));

            hud.SetEquippedCards(new[]
            {
                catalog.GetCardDisplay("split"),
                catalog.GetCardDisplay("burn"),
                catalog.GetCardDisplay("poison")
            });
            Assert.That(
                hud.GetEquippedCardText(0).text,
                Does.Contain("분열"));
            Assert.That(
                hud.GetEquippedCardText(2).text,
                Does.Contain("중독"));

            hud.ShowRewardChoices(new[]
            {
                catalog.GetCardDisplay("split"),
                catalog.GetCardDisplay("burn"),
                catalog.GetCardDisplay("poison")
            });
            Assert.That(hud.IsRewardVisible, Is.True);
            Assert.That(hud.VisibleRewardChoiceCount, Is.EqualTo(3));
            Assert.That(
                hud.GetRewardChoiceText(1).text,
                Does.Contain("화상"));
            Assert.That(
                hud.GetRewardChoiceCard(1).NameText.text,
                Is.EqualTo("화상"));
            Assert.That(
                hud.GetRewardChoiceCard(1).TierBadgeText.text,
                Is.EqualTo("T1"));
            Assert.That(
                hud.GetRewardChoiceCard(1).BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.ProjectileBodyColor));
            Button secondChoice = hud.GetRewardChoiceButton(1);
            Assert.That(secondChoice.interactable, Is.True);
            secondChoice.onClick.Invoke();
            Assert.That(selectedReward, Is.EqualTo(1));

            EventSystem.current.SetSelectedGameObject(
                secondChoice.gameObject);
            Assert.That(
                EventSystem.current.currentSelectedGameObject,
                Is.SameAs(secondChoice.gameObject));
            hud.HideRewardChoices();
            Assert.That(hud.IsRewardVisible, Is.False);
            Assert.That(hud.VisibleRewardChoiceCount, Is.Zero);
            Assert.That(
                EventSystem.current.currentSelectedGameObject,
                Is.Null,
                "Closing a reward must release its hidden UI focus.");

            Object.Destroy(hud.gameObject);
            Object.Destroy(asset);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            CardView_UsesTargetAndTierVisualsAndDropSlotsForwardCards()
        {
            var canvasHost = new GameObject(
                "Card UI Test Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            StageOneCardView card =
                StageOneCardView.CreateRuntime(
                    "Mythic Enemy Card",
                    canvasHost.transform);
            card.Configure(
                new StageOneCardDisplay(
                    "test",
                    "시험 카드",
                    "적에게 강한 효과를 적용합니다.",
                    5),
                CardTier.Mythic,
                SubjectType.Enemy,
                null,
                true,
                "장착",
                true);

            Assert.That(card.NameText.text, Is.EqualTo("시험 카드"));
            Assert.That(
                card.DescriptionText.text,
                Does.Contain("적에게"));
            Assert.That(
                card.BodyImage.color,
                Is.EqualTo(StageOneCardView.EnemyBodyColor));
            Assert.That(card.TierBadgeText.text, Is.EqualTo("T5"));
            Assert.That(card.EquippedBadgeRoot.activeSelf, Is.True);

            var tierColors = new HashSet<Color>();
            for (int tier = 1; tier <= 5; tier++)
            {
                tierColors.Add(
                    StageOneCardView.GetTierColor(
                        (CardTier)tier));
            }

            Assert.That(tierColors.Count, Is.EqualTo(5));

            StageOneCardDragSource source =
                card.gameObject.AddComponent<
                    StageOneCardDragSource>();
            source.Configure(31, canvasHost.GetComponent<Canvas>());
            var slotHost = new GameObject(
                "Drop Slot",
                typeof(RectTransform),
                typeof(Image),
                typeof(StageOneCardDropSlot));
            slotHost.transform.SetParent(canvasHost.transform, false);
            StageOneCardDropSlot slot =
                slotHost.GetComponent<StageOneCardDropSlot>();
            slot.Configure(2, true);
            int droppedCard = -1;
            int droppedSlot = -1;
            slot.DropRequested += (cardId, slotIndex) =>
            {
                droppedCard = cardId;
                droppedSlot = slotIndex;
            };

            Assert.That(slot.TryAccept(source), Is.True);
            Assert.That(droppedCard, Is.EqualTo(31));
            Assert.That(droppedSlot, Is.EqualTo(2));

            Object.Destroy(canvasHost);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            CardDragGhost_PreservesPointerOffsetAndScreenDelta()
        {
            EventSystem eventSystem =
                Object.FindObjectOfType<EventSystem>();
            bool createdEventSystem = eventSystem == null;
            if (createdEventSystem)
            {
                eventSystem = new GameObject(
                    "Card Drag Test Event System",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule))
                    .GetComponent<EventSystem>();
            }

            var canvasHost = new GameObject(
                "Scaled Card Drag Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler =
                canvasHost.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            var sourceHost = new GameObject(
                "Drag Source",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(StageOneCardDragSource));
            RectTransform sourceRect =
                sourceHost.GetComponent<RectTransform>();
            sourceRect.SetParent(canvasHost.transform, false);
            sourceRect.anchorMin = new Vector2(0.5f, 0.5f);
            sourceRect.anchorMax = new Vector2(0.5f, 0.5f);
            sourceRect.pivot = new Vector2(0.5f, 0.5f);
            sourceRect.sizeDelta = new Vector2(188f, 228f);
            sourceRect.anchoredPosition = new Vector2(137f, -93f);

            StageOneCardDragSource source =
                sourceHost.GetComponent<StageOneCardDragSource>();
            source.Configure(71, canvas, true);
            Canvas.ForceUpdateCanvases();

            Vector2 sourcePivotScreen =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    sourceRect.position);
            Vector2 pointerOffset =
                new Vector2(31f, -17f);
            Vector2 pointerStart =
                sourcePivotScreen + pointerOffset;
            var eventData =
                new PointerEventData(eventSystem)
                {
                    button =
                        PointerEventData.InputButton.Left,
                    position = pointerStart
                };

            source.OnBeginDrag(eventData);
            Assert.That(source.IsDragging, Is.True);
            Assert.That(source.DragGhost, Is.Not.Null);
            Vector2 ghostStartScreen =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    source.DragGhost.position);
            Assert.That(
                Vector2.Distance(
                    sourcePivotScreen,
                    ghostStartScreen),
                Is.LessThan(1f));

            Vector2 pointerDelta = new Vector2(123f, 79f);
            eventData.position = pointerStart + pointerDelta;
            source.OnDrag(eventData);
            Vector2 ghostMovedScreen =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    source.DragGhost.position);
            Assert.That(
                Vector2.Distance(
                    sourcePivotScreen + pointerDelta,
                    ghostMovedScreen),
                Is.LessThan(1f));
            Assert.That(
                Vector2.Distance(
                    eventData.position - ghostMovedScreen,
                    pointerOffset),
                Is.LessThan(1f));

            source.OnEndDrag(eventData);
            Assert.That(source.IsDragging, Is.False);

            Object.Destroy(canvasHost);
            if (createdEventSystem)
            {
                Object.Destroy(eventSystem.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator
            TowerLoadout_InventoryGridScrollsToEveryCardRow()
        {
            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);
            var host = new GameObject(
                "Scrollable Inventory UI Test Host");
            StageOneTowerLoadoutView view =
                StageOneTowerLoadoutView.CreateRuntime(
                    catalog,
                    null,
                    host.transform);
            var cards = new List<StageOneLoadoutCard>();
            for (int index = 0; index < 16; index++)
            {
                cards.Add(
                    new StageOneLoadoutCard(
                        100 + index,
                        catalog.GetCardDisplay(
                            "split",
                            true,
                            1),
                        false,
                        false));
            }

            view.Show(
                "궁수 타워",
                1,
                SubjectType.Projectile,
                1,
                new[] { -1, -1, -1 },
                cards,
                0,
                true);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();

            ScrollRect scrollRect = view.CardScrollRect;
            Assert.That(
                scrollRect.content.GetComponent<GridLayoutGroup>(),
                Is.Not.Null,
                "The inventory must use a layout component so inactive " +
                "and multi-row cards contribute stable content bounds.");
            Assert.That(
                scrollRect.content.rect.height,
                Is.GreaterThan(scrollRect.viewport.rect.height));
            Assert.That(
                scrollRect.verticalScrollbar.gameObject.activeInHierarchy,
                Is.True);
            Assert.That(
                scrollRect.verticalScrollbar.size,
                Is.LessThan(0.99f));

            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            yield return null;

            Rect viewportScreenRect =
                GetScreenRect(scrollRect.viewport);
            Rect lastCardScreenRect =
                GetScreenRect(
                    view.GetCardView(15)
                        .GetComponent<RectTransform>());
            Assert.That(
                viewportScreenRect.Overlaps(
                    lastCardScreenRect,
                    true),
                Is.True,
                "Moving the scrollbar to the bottom must expose the " +
                "last owned card row.");

            // StageOneBattleController refreshes the open blueprint every
            // presentation frame. Re-presenting the same loadout must not
            // force the inventory back to its first row.
            view.Show(
                "궁수 타워",
                1,
                SubjectType.Projectile,
                1,
                new[] { -1, -1, -1 },
                cards,
                0,
                true);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.That(
                scrollRect.verticalNormalizedPosition,
                Is.LessThan(0.01f),
                "Refreshing an already-open blueprint must preserve the " +
                "user's inventory scroll position.");
            lastCardScreenRect =
                GetScreenRect(
                    view.GetCardView(15)
                        .GetComponent<RectTransform>());
            Assert.That(
                viewportScreenRect.Overlaps(
                    lastCardScreenRect,
                    true),
                Is.True,
                "The last card row must remain visible after a live HUD " +
                "refresh.");

            Object.Destroy(host);
            Object.Destroy(asset);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            CardDragSource_ScrollsInsideInventoryAndDragsAfterExit()
        {
            EventSystem eventSystem =
                Object.FindObjectOfType<EventSystem>();
            bool createdEventSystem = eventSystem == null;
            if (createdEventSystem)
            {
                eventSystem = new GameObject(
                    "Inventory Drag Test Event System",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule))
                    .GetComponent<EventSystem>();
            }

            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);
            var host = new GameObject(
                "Inventory Drag Routing Test Host");
            StageOneTowerLoadoutView view =
                StageOneTowerLoadoutView.CreateRuntime(
                    catalog,
                    null,
                    host.transform);
            var cards = new List<StageOneLoadoutCard>();
            for (int index = 0; index < 12; index++)
            {
                cards.Add(
                    new StageOneLoadoutCard(
                        200 + index,
                        catalog.GetCardDisplay(
                            "split",
                            true,
                            1),
                        false,
                        false));
            }

            view.Show(
                "궁수 타워",
                1,
                SubjectType.Projectile,
                1,
                new[] { -1, -1, -1 },
                cards,
                0,
                true);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return new WaitForSecondsRealtime(0.34f);

            StageOneCardDragSource source =
                view.GetCardView(0)
                    .GetComponent<StageOneCardDragSource>();
            RectTransform sourceRect =
                source.GetComponent<RectTransform>();
            RectTransform viewport =
                view.CardScrollRect.viewport;
            Vector2 start =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    sourceRect.position);
            var eventData =
                new PointerEventData(eventSystem)
                {
                    button =
                        PointerEventData.InputButton.Left,
                    position = start,
                    pressPosition = start,
                    pointerDrag = source.gameObject
                };

            ScrollRect inventoryScroll =
                view.CardScrollRect;
            inventoryScroll.StopMovement();
            inventoryScroll.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
            Vector2 contentBeforeDrag =
                inventoryScroll.content.anchoredPosition;
            source.OnBeginDrag(eventData);
            Assert.That(
                source.IsDragging,
                Is.False,
                "A gesture that begins inside the inventory must first " +
                "belong to its ScrollRect.");
            Assert.That(source.DragGhost, Is.Null);
            Assert.That(source.IsForwardingScrollDrag, Is.True);

            float scrollGestureDistance =
                Mathf.Min(
                    12f,
                    viewport.rect.height * 0.08f);
            eventData.position =
                start + Vector2.up * scrollGestureDistance;
            eventData.delta =
                Vector2.up * scrollGestureDistance;
            source.OnDrag(eventData);
            Canvas.ForceUpdateCanvases();
            Assert.That(
                source.IsForwardingScrollDrag,
                Is.True,
                "The first in-viewport pointer move must remain owned by " +
                "the inventory scroll gesture.");
            Assert.That(
                inventoryScroll.content.anchoredPosition.y,
                Is.GreaterThan(
                    contentBeforeDrag.y + 1f),
                "Dragging a card upward inside the inventory must move " +
                "the actual ScrollRect content toward lower card rows.");
            Assert.That(
                inventoryScroll.verticalNormalizedPosition,
                Is.LessThan(0.999f),
                "A real pointer drag must change the scroll position, " +
                "not only enter forwarding state.");

            Vector3[] viewportCorners = new Vector3[4];
            viewport.GetWorldCorners(viewportCorners);
            eventData.position =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    (viewportCorners[0] +
                     viewportCorners[2]) *
                    0.5f);
            source.OnDrag(eventData);
            Assert.That(source.IsDragging, Is.False);
            Assert.That(source.IsForwardingScrollDrag, Is.True);

            eventData.position =
                new Vector2(
                    Screen.width * 0.5f,
                    Screen.height + 200f);
            Assert.That(
                RectTransformUtility.RectangleContainsScreenPoint(
                    viewport,
                    eventData.position,
                    null),
                Is.False);
            source.OnDrag(eventData);
            Assert.That(
                source.IsDragging,
                Is.True,
                "Leaving the viewport must convert the same pointer " +
                "gesture into a card drag toward a tower slot.");
            Assert.That(source.DragGhost, Is.Not.Null);
            source.OnEndDrag(eventData);

            Object.Destroy(host);
            Object.Destroy(asset);
            if (createdEventSystem)
            {
                Object.Destroy(eventSystem.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator
            TowerLoadout_OwnsPerSlotSubjectsAndUsesDiagonalWipe()
        {
            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);
            var host = new GameObject("Loadout UI Test Host");
            StageOneTowerLoadoutView view =
                StageOneTowerLoadoutView.CreateRuntime(
                    catalog,
                    null,
                    host.transform);
            var cards = new List<StageOneLoadoutCard>
            {
                new StageOneLoadoutCard(
                    11,
                    catalog.GetCardDisplay(
                        "split",
                        false,
                        1),
                    true,
                    true),
                new StageOneLoadoutCard(
                    12,
                    catalog.GetCardDisplay(
                        "burn",
                        true,
                        1),
                    false,
                    false)
            };
            var subjects = new[]
            {
                SubjectType.Projectile,
                SubjectType.Enemy,
                SubjectType.Projectile
            };

            view.Show(
                "궁수 타워",
                6,
                subjects,
                3,
                new[] { 11, -1, -1 },
                cards,
                0,
                true);

            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.IsTransitionRunning, Is.True);
            Assert.That(
                view.GetSlotSubjectType(0),
                Is.EqualTo(SubjectType.Projectile));
            Assert.That(
                view.GetSlotSubjectType(1),
                Is.EqualTo(SubjectType.Enemy));
            Assert.That(
                view.GetSlotProjectileButton(0),
                Is.Not.SameAs(
                    view.GetSlotProjectileButton(1)));
            Assert.That(
                view.GetSlotProjectileButton(0),
                Is.SameAs(view.GetSlotEnemyButton(0)));
            Assert.That(
                view.BlueprintGraphic,
                Is.SameAs(view.BackdropGraphic));
            Assert.That(
                view.GetComponentsInChildren<
                    StageOneBlueprintGridGraphic>(true).Length,
                Is.EqualTo(1));
            Assert.That(
                view.TowerPreviewContent.parent
                    .GetComponent<Image>(),
                Is.Null);
            Rect toggleRect = view.GetSlotSubjectToggleButton(0)
                .GetComponent<RectTransform>().rect;
            Assert.That(
                toggleRect.width,
                Is.EqualTo(toggleRect.height).Within(0.01f));
            Assert.That(
                view.SettingsTintImage.color,
                Is.EqualTo(Color.clear));
            Assert.That(
                view.GetSlotLabelText(0).text,
                Is.EqualTo("×2"));
            Assert.That(
                view.GetSlotLabelText(1).text,
                Is.EqualTo("비어 있음"));
            Assert.That(
                ((Image)view.GetSlotButton(0).targetGraphic).color,
                Is.Not.EqualTo(
                    ((Image)view.GetSlotButton(1)
                        .targetGraphic).color));

            yield return new WaitForSecondsRealtime(0.34f);
            Assert.That(view.IsTransitionRunning, Is.False);
            Assert.That(
                view.BlueprintRevealProgress,
                Is.EqualTo(1f).Within(0.001f));

            int requestedSlot = -1;
            int unequippedSlot = -1;
            SubjectType requestedSubject =
                SubjectType.Projectile;
            view.SlotSubjectTypeRequested +=
                (slot, subject) =>
                {
                    requestedSlot = slot;
                    requestedSubject = subject;
                };
            view.SlotUnequipRequested += slot =>
                unequippedSlot = slot;
            Color neutralCardBody =
                view.GetCardView(0).BodyImage.color;
            Assert.That(
                neutralCardBody,
                Is.Not.EqualTo(
                    StageOneCardView.ProjectileBodyColor));
            Assert.That(
                neutralCardBody,
                Is.Not.EqualTo(
                    StageOneCardView.EnemyBodyColor));
            view.GetSlotEnemyButton(2).onClick.Invoke();
            Assert.That(requestedSlot, Is.EqualTo(2));
            Assert.That(
                requestedSubject,
                Is.EqualTo(SubjectType.Enemy));
            Assert.That(
                view.GetSlotSubjectType(2),
                Is.EqualTo(SubjectType.Enemy));
            Assert.That(
                view.GetSlotSubjectType(0),
                Is.EqualTo(SubjectType.Projectile));
            Assert.That(
                view.GetCardView(0).BodyImage.color,
                Is.EqualTo(neutralCardBody));
            Assert.That(
                view.SettingsTintImage.color,
                Is.EqualTo(Color.clear));
            Assert.That(
                view.RequestSlotDoubleClick(0),
                Is.True);
            Assert.That(unequippedSlot, Is.EqualTo(0));
            Assert.That(
                view.RequestSlotDoubleClick(1),
                Is.False);

            StageOneSlotDoubleClickRelay relay =
                view.GetSlotButton(0).GetComponent<
                    StageOneSlotDoubleClickRelay>();
            Assert.That(relay, Is.Not.Null);
            int relaySlot = -1;
            relay.DoubleClicked += slot => relaySlot = slot;
            var pointer = new PointerEventData(
                EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
            relay.OnPointerClick(pointer);
            Assert.That(relaySlot, Is.EqualTo(-1));
            relay.OnPointerClick(pointer);
            Assert.That(
                relaySlot,
                Is.EqualTo(0),
                "WebGL/mobile must recognize two quick taps even when " +
                "the platform reports clickCount=1.");

            unequippedSlot = -1;
            view.GetSlotButton(0).onClick.Invoke();
            Assert.That(unequippedSlot, Is.EqualTo(-1));
            yield return new WaitForSecondsRealtime(0.45f);
            view.GetSlotButton(0).onClick.Invoke();
            Assert.That(
                unequippedSlot,
                Is.EqualTo(0),
                "The slot Button must own a platform-independent " +
                "double-click fallback.");

            view.Hide();
            Assert.That(view.IsTransitionRunning, Is.True);
            yield return new WaitForSecondsRealtime(0.34f);
            Assert.That(view.IsVisible, Is.False);
            Assert.That(
                view.BlueprintRevealProgress,
                Is.EqualTo(0f).Within(0.001f));

            Object.Destroy(host);
            Object.Destroy(asset);
            yield return null;
        }

        private static Rect GetScreenRect(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 minimum =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    corners[0]);
            Vector2 maximum =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    corners[2]);
            return Rect.MinMaxRect(
                Mathf.Min(minimum.x, maximum.x),
                Mathf.Min(minimum.y, maximum.y),
                Mathf.Max(minimum.x, maximum.x),
                Mathf.Max(minimum.y, maximum.y));
        }

        [UnityTest]
        public IEnumerator
            TowerLoadout_CurrentEffectStacksCardsInSlotOrder()
        {
            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);
            var host = new GameObject(
                "Cumulative Effect UI Test Host");
            StageOneTowerLoadoutView view =
                StageOneTowerLoadoutView.CreateRuntime(
                    catalog,
                    null,
                    host.transform);
            var cards = new List<StageOneLoadoutCard>
            {
                new StageOneLoadoutCard(
                    13,
                    catalog.GetCardDisplay(
                        "poison",
                        false,
                        1),
                    true,
                    true),
                new StageOneLoadoutCard(
                    11,
                    catalog.GetCardDisplay(
                        "split",
                        true,
                        1),
                    true,
                    true),
                new StageOneLoadoutCard(
                    12,
                    catalog.GetCardDisplay(
                        "burn",
                        true,
                        1),
                    true,
                    true)
            };
            var subjects = new[]
            {
                SubjectType.Enemy,
                SubjectType.Enemy,
                SubjectType.Projectile
            };

            view.Show(
                "궁수 타워",
                6,
                subjects,
                3,
                new[] { 12, 11, 13 },
                cards,
                1,
                true);

            const string expected =
                "화상 · 지속 화염 피해를 받습니다.\n" +
                "→ 분열 · 적이 두 개체로 나뉩니다.\n" +
                "→ 중독 · 적중 시 중독을 부여합니다.";
            Assert.That(
                view.CurrentEffectText.text,
                Is.EqualTo(expected),
                "The summary must follow slot order, not inventory order.");
            Assert.That(
                view.GetSlotDescriptionText(1).text,
                Is.EqualTo(
                    "분열 · 적이 두 개체로 나뉩니다."),
                "The selected slot row remains its own single-card " +
                "description.");
            Assert.That(
                view.CurrentEffectText.horizontalOverflow,
                Is.EqualTo(HorizontalWrapMode.Wrap));
            Assert.That(
                view.CurrentEffectText.resizeTextForBestFit,
                Is.True);
            Assert.That(
                view.CurrentEffectText.rectTransform.rect.height,
                Is.GreaterThanOrEqualTo(78f));

            view.SetSlotSubjectType(
                2,
                SubjectType.Enemy);
            Assert.That(
                view.CurrentEffectText.text,
                Does.EndWith(
                    "→ 중독 · 지속 독 피해를 받습니다."),
                "Each summary step must use that slot's current " +
                "interpretation.");

            view.Show(
                "궁수 타워",
                4,
                subjects,
                2,
                new[] { 12, 11, 13 },
                cards,
                0,
                true);
            Assert.That(
                view.CurrentEffectText.text,
                Is.EqualTo(
                    "화상 · 지속 화염 피해를 받습니다.\n" +
                    "→ 분열 · 적이 두 개체로 나뉩니다."),
                "Cards placed beyond the unlocked execution range " +
                "must be omitted.");

            view.gameObject.SendMessage(
                "ApplyPortraitLayout",
                SendMessageOptions.RequireReceiver);
            Canvas.ForceUpdateCanvases();
            Assert.That(
                view.CurrentEffectText.rectTransform.rect.height,
                Is.GreaterThanOrEqualTo(108f),
                "Portrait layout must reserve enough wrapped-text " +
                "height for the cumulative rule.");

            Object.Destroy(host);
            Object.Destroy(asset);
            yield return null;
        }
    }
}
