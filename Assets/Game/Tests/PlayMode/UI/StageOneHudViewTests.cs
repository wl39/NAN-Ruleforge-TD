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
    { ""key"": ""hud.summary_format"", ""value"": ""스테이지 {5} · 웨이브 {0}/{1} · 본진 {2} · 골드 {3} · {4}"" },
    { ""key"": ""hud.play_format"", ""value"": ""웨이브 {0} 시작"" },
    { ""key"": ""hud.pause"", ""value"": ""일시정지"" },
    { ""key"": ""hud.resume"", ""value"": ""계속"" },
    { ""key"": ""hud.continue_stage"", ""value"": ""이어하기"" },
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
    { ""key"": ""tower_panel.subject_projectile_title"", ""value"": ""탄환 효과 적용"" },
    { ""key"": ""tower_panel.subject_enemy_title"", ""value"": ""적 효과 적용"" },
    { ""key"": ""tower_panel.subject_projectile_tooltip_format"", ""value"": ""{0} 카드의 효과가 탄환에 부여됩니다.\n{1}"" },
    { ""key"": ""tower_panel.subject_enemy_tooltip_format"", ""value"": ""{0} 카드의 효과가 적에게 부여됩니다.\n{1}"" },
    { ""key"": ""tower_panel.subject_projectile_empty_tooltip"", ""value"": ""장착 카드의 탄환 효과가 탄환에 부여됩니다."" },
    { ""key"": ""tower_panel.subject_enemy_empty_tooltip"", ""value"": ""장착 카드의 적 효과가 적에게 부여됩니다."" },
    { ""key"": ""tower_panel.usage_map_title_format"", ""value"": ""{0} · 사용 중"" },
    { ""key"": ""tower_panel.usage_map_body"", ""value"": ""청록색 타워가 적용 위치입니다."" },
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
            Assert.That(
                catalog.Format("hud.play_format", 1),
                Is.EqualTo("웨이브 1 시작"));
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
            Assert.That(
                display.ProjectileDescription,
                Does.Contain("중독"));
            Assert.That(
                display.EnemyDescription,
                Does.Contain("독 피해"));

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

            hud.SetHud(1, 9, 20, 35, "Planning", 2);
            Assert.That(hud.HudText.text, Does.Contain("스테이지 2"));
            Assert.That(hud.HudText.text, Does.Contain("웨이브 1/9"));
            Assert.That(hud.HudText.text, Does.Contain("계획"));

            hud.SetPlayState(StageOnePlayState.Ready);
            Assert.That(hud.PlayButton.interactable, Is.True);
            Assert.That(
                hud.PlayButtonLabel.text,
                Is.EqualTo("웨이브 1 시작"));
            Assert.That(hud.IsPlayButtonPulsing, Is.True);
            hud.SetHud(2, 9, 20, 35, "Planning");
            Assert.That(
                hud.PlayButtonLabel.text,
                Is.EqualTo("웨이브 2 시작"));
            hud.SetHud(1, 9, 20, 35, "Planning");
            hud.PlayButton.onClick.Invoke();
            Assert.That(playRequests, Is.EqualTo(1));

            hud.SetPlayState(StageOnePlayState.Playing);
            Assert.That(hud.IsPlayButtonPulsing, Is.False);
            Assert.That(
                hud.PlayButtonLabel.text,
                Is.EqualTo("일시정지"));
            hud.SetPlayState(StageOnePlayState.Paused);
            Assert.That(hud.PlayButtonLabel.text, Is.EqualTo("계속"));
            hud.SetPlayState(StageOnePlayState.Continue);
            Assert.That(
                hud.PlayButtonLabel.text,
                Is.EqualTo("이어하기"));
            Assert.That(hud.IsPlayButtonPulsing, Is.True);

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
                hud.GetRewardChoiceCard(1).IsExpandedPresentation,
                Is.True);
            Assert.That(
                hud.GetRewardChoiceCard(1).DescriptionText.fontSize,
                Is.GreaterThanOrEqualTo(15));
            Assert.That(
                hud.GetRewardChoiceCard(1)
                    .DescriptionBackplateImage.rectTransform.anchorMin.x,
                Is.GreaterThanOrEqualTo(0.14f));
            Assert.That(
                hud.GetRewardChoiceCard(1)
                    .DescriptionBackplateImage.rectTransform.anchorMax.y,
                Is.LessThanOrEqualTo(0.38f),
                "Expanded descriptions must clear the dark center divider.");
            Assert.That(
                hud.GetRewardChoiceCard(1).TierBadgeText.text,
                Is.EqualTo("T1"));
            Assert.That(
                hud.GetRewardChoiceCard(1).BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.ProjectileBodyColor));
            hud.GetRewardChoiceCard(1).OnPointerDown(
                new PointerEventData(EventSystem.current)
                {
                    button =
                        PointerEventData.InputButton.Right
                });
            Assert.That(
                hud.GetRewardChoiceCard(1).SubjectType,
                Is.EqualTo(SubjectType.Enemy));
            Assert.That(
                hud.GetRewardChoiceCard(1).DescriptionText.text,
                Does.Contain("지속 화염 피해"));
            Assert.That(
                hud.GetRewardChoiceCard(1).BodyImage.color,
                Is.EqualTo(StageOneCardView.EnemyBodyColor));
            hud.ShowRewardChoices(new[]
            {
                catalog.GetCardDisplay("split"),
                catalog.GetCardDisplay("burn"),
                catalog.GetCardDisplay("poison")
            });
            Assert.That(
                hud.GetRewardChoiceCard(1).SubjectType,
                Is.EqualTo(SubjectType.Enemy),
                "A presentation refresh must preserve the " +
                "right-click interpretation preview.");
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
            EventSystem eventSystem =
                Object.FindObjectOfType<EventSystem>();
            bool createdEventSystem = eventSystem == null;
            if (createdEventSystem)
            {
                eventSystem = new GameObject(
                    "Card View Test Event System",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule))
                    .GetComponent<EventSystem>();
            }

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
                    "split",
                    "시험 카드",
                    "탄환에 강한 효과를 적용합니다.",
                    "적에게 강한 효과를 적용합니다.",
                    true,
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
            Assert.That(card.EquippedBadgeSprite, Is.Not.Null);
            Assert.That(
                card.EquippedHighlightImage.gameObject.activeSelf,
                Is.True);
            Assert.That(card.ArtworkSprite, Is.Not.Null);
            Assert.That(
                card.ArtworkSymbolText.gameObject.activeSelf,
                Is.False);

            card.OnPointerDown(
                new PointerEventData(eventSystem)
                {
                    button =
                        PointerEventData.InputButton.Left
                });
            Assert.That(
                card.SubjectType,
                Is.EqualTo(SubjectType.Enemy));
            card.OnPointerDown(
                new PointerEventData(eventSystem)
                {
                    button =
                        PointerEventData.InputButton.Right
                });
            Assert.That(
                card.SubjectType,
                Is.EqualTo(SubjectType.Projectile));
            Assert.That(
                card.DescriptionText.text,
                Does.Contain("탄환에"));
            Assert.That(
                card.BodyImage.color,
                Is.EqualTo(StageOneCardView.ProjectileBodyColor));

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
            if (createdEventSystem)
            {
                Object.Destroy(eventSystem.gameObject);
            }

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
            TowerLoadout_LandscapeUsesLeftLoadoutAndRightInventory()
        {
            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);
            var host = new GameObject(
                "Horizontal Loadout UI Test Host");
            StageOneTowerLoadoutView view =
                StageOneTowerLoadoutView.CreateRuntime(
                    catalog,
                    null,
                    host.transform);
            var cards = new List<StageOneLoadoutCard>();
            for (int index = 0; index < 4; index++)
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
                3,
                new[] { -1, -1, -1 },
                cards,
                0,
                true);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.That(view.IsPortraitLayout, Is.False);
            Assert.That(view.WorkbenchBackdropImage, Is.Not.Null);
            Assert.That(
                view.WorkbenchBackdropImage.sprite,
                Is.Not.Null,
                "The loadout must use the parchment drafting backdrop.");
            Assert.That(
                view.WorkbenchBackdropImage.sprite.texture.width,
                Is.EqualTo(1672));
            Assert.That(
                view.WorkbenchBackdropImage.sprite.texture.height,
                Is.EqualTo(941));
            AspectRatioFitter backdropAspect =
                view.WorkbenchBackdropImage.GetComponent<
                    AspectRatioFitter>();
            Assert.That(backdropAspect, Is.Not.Null);
            Assert.That(
                backdropAspect.aspectMode,
                Is.EqualTo(
                    AspectRatioFitter.AspectMode.EnvelopeParent));
            Rect towerRect = GetScreenRect(
                view.TowerPreviewContent.parent as RectTransform);
            Rect effectRect = GetScreenRect(
                view.EffectBackplate.rectTransform);
            Rect inventoryRect = GetScreenRect(view.CardViewport);
            Assert.That(
                towerRect.xMax,
                Is.LessThan(effectRect.xMin),
                "The tower preview must occupy the left column.");
            Assert.That(
                effectRect.xMax,
                Is.LessThan(inventoryRect.xMin),
                "The equipment surface must stay left of the card list.");
            Assert.That(
                inventoryRect.height,
                Is.GreaterThan(inventoryRect.width),
                "The desktop inventory must be a vertical side column.");
            Assert.That(
                view.CardScrollRect.content
                    .GetComponent<GridLayoutGroup>()
                    .constraintCount,
                Is.EqualTo(3));

            Image inventoryImage =
                view.CardViewport.GetComponent<Image>();
            Assert.That(inventoryImage.type, Is.EqualTo(Image.Type.Simple));
            Assert.That(inventoryImage.preserveAspect, Is.True);
            Assert.That(inventoryImage.sprite.texture.width, Is.EqualTo(1140));
            Assert.That(inventoryImage.sprite.texture.height, Is.EqualTo(1980));
            Assert.That(
                view.TowerPreviewContent.parent.gameObject.activeSelf,
                Is.True,
                "The desktop tower preview must remain visible.");
            Assert.That(view.TowerPreviewBackplate.enabled, Is.True);
            Assert.That(
                view.TowerPreviewBackplate.sprite.texture.width,
                Is.EqualTo(840));
            Assert.That(
                view.TowerPreviewBackplate.sprite.texture.height,
                Is.EqualTo(1980));

            Object.Destroy(host);
            Object.Destroy(asset);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            TowerLoadout_WholeSlotRowAcceptsCardDrops()
        {
            EventSystem eventSystem =
                Object.FindObjectOfType<EventSystem>();
            bool createdEventSystem = eventSystem == null;
            if (createdEventSystem)
            {
                eventSystem = new GameObject(
                    "Slot Row Drop Test Event System",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule))
                    .GetComponent<EventSystem>();
            }

            var asset = new TextAsset(TestCatalogJson);
            StageOneUiTextCatalog catalog =
                StageOneUiTextCatalog.Load(asset);
            var host = new GameObject("Slot Row Drop Test Host");
            StageOneTowerLoadoutView view =
                StageOneTowerLoadoutView.CreateRuntime(
                    catalog,
                    null,
                    host.transform);
            var cards = new List<StageOneLoadoutCard>
            {
                new StageOneLoadoutCard(
                    31,
                    catalog.GetCardDisplay(
                        "split",
                        true,
                        1),
                    false,
                    false)
            };
            view.Show(
                "궁수 타워",
                7,
                SubjectType.Projectile,
                3,
                new[] { -1, -1, -1 },
                cards,
                0,
                true);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();

            StageOneCardDropSlot firstRow =
                view.GetSlotDropTarget(0);
            Assert.That(
                view.GetSlotButton(0).transform.IsChildOf(
                    firstRow.transform),
                Is.True);
            Assert.That(
                view.GetSlotDescriptionText(0).transform.IsChildOf(
                    firstRow.transform),
                Is.True);
            Assert.That(
                view.GetSlotSubjectToggleButton(0).transform.IsChildOf(
                    firstRow.transform),
                Is.True);
            Image rowRaycastSurface = view.GetSlotDropSurface(0);
            Assert.That(rowRaycastSurface, Is.Not.Null);
            Assert.That(
                rowRaycastSurface.transform.IsChildOf(
                    firstRow.transform),
                Is.True);
            Assert.That(rowRaycastSurface.raycastTarget, Is.True);
            Assert.That(rowRaycastSurface.color.a, Is.Zero);

            Rect rowRect = GetScreenRect(
                rowRaycastSurface.rectTransform);
            Rect slotRect = GetScreenRect(
                view.GetSlotButton(0)
                    .GetComponent<RectTransform>());
            Rect descriptionRect = GetScreenRect(
                view.GetSlotDescriptionText(0)
                    .transform.parent as RectTransform);
            Rect subjectRect = GetScreenRect(
                view.GetSlotSubjectToggleButton(0)
                    .GetComponent<RectTransform>());
            Assert.That(rowRect.xMin, Is.EqualTo(slotRect.xMin)
                .Within(0.01f));
            Assert.That(rowRect.xMax, Is.EqualTo(subjectRect.xMax)
                .Within(0.01f));
            Assert.That(
                rowRect.width,
                Is.GreaterThan(
                    slotRect.width +
                    descriptionRect.width +
                    subjectRect.width),
                "The row target must also cover the visual gaps.");

            int droppedCard = -1;
            int droppedSlot = -1;
            view.CardDropped += (cardId, slotIndex) =>
            {
                droppedCard = cardId;
                droppedSlot = slotIndex;
            };
            StageOneCardDragSource source =
                view.GetCardView(0).GetComponent<
                    StageOneCardDragSource>();
            var eventData = new PointerEventData(eventSystem)
            {
                pointerDrag = source.gameObject
            };

            GameObject descriptionHandler =
                ExecuteEvents.ExecuteHierarchy(
                    view.GetSlotDescriptionText(1).gameObject,
                    eventData,
                    ExecuteEvents.dropHandler);
            Assert.That(
                descriptionHandler,
                Is.SameAs(view.GetSlotDropTarget(1).gameObject));
            Assert.That(droppedCard, Is.EqualTo(31));
            Assert.That(droppedSlot, Is.EqualTo(1));

            GameObject subjectHandler =
                ExecuteEvents.ExecuteHierarchy(
                    view.GetSlotSubjectToggleButton(2).gameObject,
                    eventData,
                    ExecuteEvents.dropHandler);
            Assert.That(
                subjectHandler,
                Is.SameAs(view.GetSlotDropTarget(2).gameObject));
            Assert.That(droppedSlot, Is.EqualTo(2));

            droppedSlot = -1;
            ExecuteEvents.ExecuteHierarchy(
                rowRaycastSurface.gameObject,
                eventData,
                ExecuteEvents.dropHandler);
            Assert.That(
                droppedSlot,
                Is.EqualTo(0),
                "Transparent gaps in the row must accept the same drop.");

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
            const int syntheticCardCount = 59;
            for (int index = 0;
                 index < syntheticCardCount;
                 index++)
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

            Assert.That(
                view.VisibleCardCount,
                Is.EqualTo(syntheticCardCount),
                "The inventory view count must follow content instead " +
                "of a fixed UI pool size.");

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
                    view.GetCardView(
                        syntheticCardCount - 1)
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
                    view.GetCardView(
                        syntheticCardCount - 1)
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
            CardDragSource_ImmediatelyTracksPointerInsideInventory()
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

            source.OnBeginDrag(eventData);
            Assert.That(
                source.IsDragging,
                Is.True,
                "A card drag must start immediately even when the " +
                "pointer is inside the inventory viewport.");
            Assert.That(source.DragGhost, Is.Not.Null);
            Assert.That(source.IsForwardingScrollDrag, Is.False);

            Vector2 pointerDelta = new Vector2(87f, 64f);
            eventData.position = start + pointerDelta;
            eventData.delta = pointerDelta;
            source.OnDrag(eventData);
            Vector2 ghostPosition =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    source.DragGhost.position);
            Assert.That(
                Vector2.Distance(
                    start + pointerDelta,
                    ghostPosition),
                Is.LessThan(1f),
                "The drag ghost must follow the pointer immediately.");
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
                    true,
                    41),
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

            view.SetMapOverview(
                new[]
                {
                    new Vector2(-4f, 2f),
                    new Vector2(0f, 2f),
                    new Vector2(0f, -3f),
                    new Vector2(5f, -3f)
                },
                new[]
                {
                    new StageOneLoadoutMapSite(
                        41,
                        new Vector2(-2f, 4f),
                        true),
                    new StageOneLoadoutMapSite(
                        -1,
                        new Vector2(2f, 0f),
                        false)
                });

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
                Is.SameAs(view.TowerPreviewBackplate));
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
                view.GetSlotCardArtwork(0).sprite,
                Is.Not.Null);
            Assert.That(
                view.GetSlotCardArtwork(0).gameObject.activeSelf,
                Is.True,
                "An equipped slot must show the card artwork instead of " +
                "the old text token.");
            Assert.That(
                view.GetSlotLabelText(0).gameObject.activeSelf,
                Is.False);
            Assert.That(
                view.ProjectileArtwork.sprite,
                Is.Not.Null);
            Assert.That(
                view.GetSlotSubjectHoverRelay(0),
                Is.Not.Null);

            view.GetSlotSubjectHoverRelay(0)
                .OnPointerEnter(null);
            Assert.That(
                view.HoverPopupRoot.gameObject.activeSelf,
                Is.True);
            Assert.That(
                view.HoverPopupBody.text,
                Does.Contain("탄환에 부여"));
            Assert.That(
                view.HoverPopupBody.text,
                Does.Contain("두 발"));
            Assert.That(
                view.UsageMiniMap.gameObject.activeSelf,
                Is.False);
            view.GetSlotSubjectHoverRelay(0)
                .OnPointerExit(null);

            view.GetCardHoverRelay(0).OnPointerEnter(null);
            Assert.That(
                view.HoverCardPreview.gameObject.activeSelf,
                Is.True);
            Assert.That(
                view.HoverCardPreview.IsExpandedPresentation,
                Is.True);
            Assert.That(
                view.HoverCardPreview.NameText.text,
                Is.EqualTo("분열"));
            Assert.That(
                view.HoverCardPreview.DescriptionText.text,
                Does.Contain("두 발"));
            Assert.That(
                view.UsageMiniMap.gameObject.activeSelf,
                Is.True);
            Assert.That(
                view.UsageMiniMap.FocusedTowerId,
                Is.EqualTo(41));
            Assert.That(
                view.UsageMiniMap.PathPointCount,
                Is.EqualTo(4));
            Assert.That(
                view.UsageMiniMap.SiteCount,
                Is.EqualTo(2));
            Assert.That(
                view.HoverPopupTitle.text,
                Does.Contain("사용 중"));
            view.GetCardHoverRelay(0).OnPointerExit(null);
            view.GetCardHoverRelay(1).OnPointerEnter(null);
            Assert.That(
                view.HoverCardPreview.NameText.text,
                Is.EqualTo("화상"));
            Assert.That(
                view.HoverCardPreview.gameObject.activeSelf,
                Is.True);
            Assert.That(
                view.UsageMiniMap.gameObject.activeSelf,
                Is.False,
                "Unequipped owned cards still need a large preview.");
            view.GetCardHoverRelay(1).OnPointerExit(null);
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
            int doubleClickedCard = -1;
            int doubleClickStartedSlot = int.MinValue;
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
            view.CardDoubleClickRequested +=
                (cardId, startedSlot) =>
                {
                    doubleClickedCard = cardId;
                    doubleClickStartedSlot = startedSlot;
                };
            Assert.That(
                view.GetCardView(0).BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.ProjectileBodyColor));
            view.GetCardView(0).ToggleInterpretation();
            Assert.That(
                view.GetCardView(0).BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.EnemyBodyColor));
            Assert.That(
                view.GetCardView(0).DescriptionText.text,
                Does.Contain("적에게 적용"));
            Assert.That(
                view.GetCardView(0).DescriptionText.text,
                Does.Contain("두 개체"));
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
                Is.EqualTo(StageOneCardView.EnemyBodyColor));
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
            view.GetCardButton(1).onClick.Invoke();
            Assert.That(doubleClickedCard, Is.EqualTo(-1));
            view.GetCardButton(1).onClick.Invoke();
            Assert.That(
                doubleClickedCard,
                Is.EqualTo(12),
                "Two quick inventory-card clicks must request a " +
                "replacement instead of a second ordinary equip.");
            Assert.That(
                doubleClickStartedSlot,
                Is.EqualTo(-1),
                "The automatic-equip policy must know the card was " +
                "not on the selected tower before the first click.");

            doubleClickedCard = -1;
            doubleClickStartedSlot = int.MinValue;
            view.GetCardButton(0).onClick.Invoke();
            view.GetCardButton(0).onClick.Invoke();
            Assert.That(doubleClickedCard, Is.EqualTo(11));
            Assert.That(
                doubleClickStartedSlot,
                Is.EqualTo(0),
                "An already-equipped card must remember its original " +
                "slot across the two-click gesture.");

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
            Assert.That(unequippedSlot, Is.EqualTo(0));

            unequippedSlot = -1;
            StageOneSlotDoubleClickRelay rowRelay =
                view.GetSlotDropTarget(0).GetComponent<
                    StageOneSlotDoubleClickRelay>();
            Assert.That(rowRelay, Is.Not.Null);
            rowRelay.RequestDoubleClick();
            Assert.That(
                unequippedSlot,
                Is.EqualTo(0),
                "The description and transparent gaps must unequip " +
                "through the whole-row relay.");

            unequippedSlot = -1;
            Button subjectButton =
                view.GetSlotSubjectToggleButton(0);
            StageOneSlotDoubleClickRelay subjectRelay =
                subjectButton.GetComponent<
                    StageOneSlotDoubleClickRelay>();
            Assert.That(
                subjectRelay,
                Is.Null,
                "The subject toggle owns its clicks and must not " +
                "inherit the row's double-click unequip gesture.");
            SubjectType subjectBeforeDoubleClick =
                view.GetSlotSubjectType(0);
            subjectButton.onClick.Invoke();
            subjectButton.onClick.Invoke();
            Assert.That(
                unequippedSlot,
                Is.EqualTo(-1),
                "Double-clicking the subject toggle must not " +
                "unequip the card.");
            Assert.That(
                view.GetSlotSubjectType(0),
                Is.EqualTo(subjectBeforeDoubleClick),
                "Two subject-toggle clicks must only toggle the " +
                "subject twice.");

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
                Is.GreaterThanOrEqualTo(46f));
            Assert.That(
                view.CurrentEffectText.alignment,
                Is.EqualTo(TextAnchor.UpperLeft));
            Assert.That(
                view.CurrentEffectText.lineSpacing,
                Is.EqualTo(0.95f).Within(0.001f));
            Assert.That(
                view.CurrentEffectText.rectTransform
                    .anchoredPosition.x -
                view.EffectBackplate.rectTransform
                    .anchoredPosition.x,
                Is.GreaterThanOrEqualTo(28f));
            Assert.That(
                view.CurrentEffectText.rectTransform
                    .anchoredPosition.y -
                view.EffectBackplate.rectTransform
                    .anchoredPosition.y,
                Is.GreaterThanOrEqualTo(24f));

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
                Is.GreaterThanOrEqualTo(76f),
                "Portrait layout must reserve enough wrapped-text " +
                "height for the cumulative rule.");
            Assert.That(
                view.CurrentEffectText.rectTransform
                    .anchoredPosition.x -
                view.EffectBackplate.rectTransform
                    .anchoredPosition.x,
                Is.GreaterThanOrEqualTo(32f));
            Assert.That(
                view.CurrentEffectText.rectTransform
                    .anchoredPosition.y -
                view.EffectBackplate.rectTransform
                    .anchoredPosition.y,
                Is.GreaterThanOrEqualTo(32f));

            Object.Destroy(host);
            Object.Destroy(asset);
            yield return null;
        }
    }
}
