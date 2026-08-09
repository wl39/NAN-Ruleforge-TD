using System.Collections.Generic;
using System.Globalization;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UnityView.TestLab
{
    public sealed partial class TestLabControlPanel
    {
        private void BuildInterface()
        {
            EnsureEventSystem();

            GameObject canvasHost = CreateUiObject(
                "TestLab Canvas",
                transform,
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            CanvasScaler scaler = canvasHost.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            safeAreaRoot = CreateUiObject(
                    "TestLab Safe Area",
                    canvasHost.transform,
                    typeof(StageOneSafeAreaFitter))
                .GetComponent<RectTransform>();
            safeAreaRoot.anchorMin = Vector2.zero;
            safeAreaRoot.anchorMax = Vector2.one;
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;
            safeAreaRoot
                .GetComponent<StageOneSafeAreaFitter>()
                .ApplySafeArea();

            panelRoot = CreatePanel(
                "TestLab Panel",
                safeAreaRoot,
                PanelColor);
            panelRoot.anchorMin = new Vector2(1f, 0f);
            panelRoot.anchorMax = new Vector2(1f, 1f);
            panelRoot.pivot = new Vector2(1f, 0.5f);
            // Stage HUD의 시작/속도 버튼과 상태 안내 두 줄을 침범하지 않도록
            // TestLab 세로 패널은 상단 112px을 비워 둔다.
            panelRoot.sizeDelta = new Vector2(500f, -136f);
            panelRoot.anchoredPosition = new Vector2(-12f, -56f);

            RectTransform header = CreatePanel(
                "Header",
                panelRoot,
                new Color32(27, 37, 50, 255));
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 64f);
            header.anchoredPosition = Vector2.zero;

            Text title = CreateText(
                "Title",
                header,
                "RULEFORGE TEST LAB",
                24,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, 16f, 0f, -74f, 0f);

            Button closeButton = CreateButton(
                "Close Panel Button",
                header,
                "숨김",
                ButtonColor,
                delegate { SetVisible(false); });
            RectTransform closeRect =
                closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(64f, 38f);
            closeRect.anchoredPosition = new Vector2(-10f, 0f);

            CreateScrollArea();
            BuildSummarySection();
            BuildEnemySection();
            BuildResourceSection();
            BuildCardSection();
            BuildTowerSection();
            BuildLoadoutSection();
            BuildStatusSection();

            CreateDebuffToolbar(safeAreaRoot);
            CreateReopenButton(safeAreaRoot);
            responsiveLayout =
                canvasHost.AddComponent<
                    TestLabResponsiveLayout>();
            responsiveLayout.Configure(
                panelRoot,
                debuffToolbarRoot,
                reopenButton.GetComponent<RectTransform>());
            built = true;
        }

        private void CreateDebuffToolbar(Transform parent)
        {
            debuffToolbarRoot = CreatePanel(
                "Enemy Debuff Toolbar",
                parent,
                PanelColor);
            debuffToolbarRoot.anchorMin =
                new Vector2(0f, 1f);
            debuffToolbarRoot.anchorMax =
                new Vector2(1f, 1f);
            debuffToolbarRoot.pivot =
                new Vector2(0.5f, 1f);
            // HUD의 두 상단 행 아래, 우측 TestLab 패널 왼쪽에만 배치한다.
            // 따라서 시작 버튼·속도 버튼·TestLab 패널 어느 쪽과도 겹치지 않는다.
            debuffToolbarRoot.offsetMin =
                new Vector2(12f, -180f);
            debuffToolbarRoot.offsetMax =
                new Vector2(-524f, -112f);

            Text title = CreateText(
                "Debuff Toolbar Title",
                debuffToolbarRoot,
                "모든 적 디버프",
                16,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            title.rectTransform.anchorMin =
                new Vector2(0f, 0f);
            title.rectTransform.anchorMax =
                new Vector2(0f, 1f);
            title.rectTransform.pivot =
                new Vector2(0f, 0.5f);
            title.rectTransform.sizeDelta =
                new Vector2(146f, 0f);
            title.rectTransform.anchoredPosition =
                new Vector2(12f, 0f);
            title.rectTransform.offsetMin =
                new Vector2(12f, 7f);
            title.rectTransform.offsetMax =
                new Vector2(158f, -7f);

            Text hint = CreateText(
                "Debuff Toolbar Hint",
                title.transform,
                "클릭하면 현재 적 전체 적용",
                11,
                FontStyle.Normal,
                TextAnchor.LowerLeft,
                MutedTextColor);
            SetRect(
                hint.rectTransform,
                0f,
                -2f,
                0f,
                -28f);

            Button previousButton = CreateButton(
                "Previous Debuffs Button",
                debuffToolbarRoot,
                "<",
                ButtonColor,
                delegate { ScrollDebuffToolbar(-0.2f); });
            RectTransform previousRect =
                previousButton.GetComponent<RectTransform>();
            previousRect.anchorMin =
                new Vector2(0f, 0.5f);
            previousRect.anchorMax =
                new Vector2(0f, 0.5f);
            previousRect.pivot =
                new Vector2(0f, 0.5f);
            previousRect.sizeDelta =
                new Vector2(38f, 48f);
            previousRect.anchoredPosition =
                new Vector2(166f, 0f);

            Button nextButton = CreateButton(
                "Next Debuffs Button",
                debuffToolbarRoot,
                ">",
                ButtonColor,
                delegate { ScrollDebuffToolbar(0.2f); });
            RectTransform nextRect =
                nextButton.GetComponent<RectTransform>();
            nextRect.anchorMin =
                new Vector2(1f, 0.5f);
            nextRect.anchorMax =
                new Vector2(1f, 0.5f);
            nextRect.pivot =
                new Vector2(1f, 0.5f);
            nextRect.sizeDelta =
                new Vector2(38f, 48f);
            nextRect.anchoredPosition =
                new Vector2(-8f, 0f);

            RectTransform viewport = CreatePanel(
                "Debuff Scroll Viewport",
                debuffToolbarRoot,
                Color.white);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin =
                new Vector2(210f, 10f);
            viewport.offsetMax =
                new Vector2(-52f, -10f);
            viewport.gameObject.AddComponent<Mask>()
                .showMaskGraphic = false;

            debuffScrollRect =
                viewport.gameObject.AddComponent<ScrollRect>();
            debuffScrollRect.viewport = viewport;
            debuffScrollRect.horizontal = true;
            debuffScrollRect.vertical = false;
            debuffScrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            debuffScrollRect.scrollSensitivity = 36f;
            debuffScrollRect.inertia = true;

            GameObject contentHost = CreateUiObject(
                "Debuff Buttons",
                viewport,
                typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            debuffContentRoot =
                contentHost.GetComponent<RectTransform>();
            debuffContentRoot.anchorMin =
                new Vector2(0f, 0f);
            debuffContentRoot.anchorMax =
                new Vector2(0f, 1f);
            debuffContentRoot.pivot =
                new Vector2(0f, 0.5f);
            debuffContentRoot.sizeDelta =
                Vector2.zero;
            HorizontalLayoutGroup layout =
                contentHost.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment =
                TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            ContentSizeFitter fitter =
                contentHost.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            debuffScrollRect.content =
                debuffContentRoot;

            debuffButtons.Clear();
            debuffButtonCardIds.Clear();
            IReadOnlyList<TestLabContentOption> options =
                target.DebuffOptions;
            for (int index = 0;
                 index < options.Count;
                 index++)
            {
                TestLabContentOption option = options[index];
                string cardId = option.StableId;
                Button button = CreateButton(
                    "Apply " + cardId + " Debuff Button",
                    debuffContentRoot,
                    option.DisplayName,
                    ImportantButtonColor,
                    delegate
                    {
                        ApplyDebuffToAllEnemies(cardId);
                    });
                LayoutElement buttonLayout =
                    button.gameObject.AddComponent<LayoutElement>();
                buttonLayout.minWidth = 92f;
                buttonLayout.preferredWidth = Mathf.Clamp(
                    64f +
                    option.DisplayName.Length * 14f,
                    104f,
                    168f);
                buttonLayout.minHeight = 48f;
                buttonLayout.preferredHeight = 48f;
                debuffButtons.Add(button);
                debuffButtonCardIds.Add(cardId);
            }
        }

        private void ScrollDebuffToolbar(float delta)
        {
            if (debuffScrollRect == null)
            {
                return;
            }

            debuffScrollRect.horizontalNormalizedPosition =
                Mathf.Clamp01(
                    debuffScrollRect
                        .horizontalNormalizedPosition +
                    delta);
        }

        private void CreateScrollArea()
        {
            RectTransform viewport = CreatePanel(
                "Viewport",
                panelRoot,
                Color.white);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(8f, 8f);
            viewport.offsetMax = new Vector2(-8f, -70f);
            // showMaskGraphic already suppresses the viewport image.
            // Its alpha must remain non-zero so WebGL's alpha-clipped UI
            // shader can still write the stencil used by child controls.
            viewport.gameObject.AddComponent<Mask>()
                .showMaskGraphic = false;

            GameObject scrollHost = viewport.gameObject;
            scrollRect = scrollHost.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 42f;
            scrollRect.inertia = true;

            GameObject contentHost = CreateUiObject(
                "Content",
                viewport,
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            contentRoot =
                contentHost.GetComponent<RectTransform>();
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout =
                contentHost.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter =
                contentHost.GetComponent<ContentSizeFitter>();
            fitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = contentRoot;
        }

        private void RebuildPanelLayout()
        {
            if (contentRoot == null ||
                scrollRect == null ||
                scrollRect.viewport == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                contentRoot);
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                scrollRect.viewport);
            Canvas.ForceUpdateCanvases();

            Vector2 anchoredPosition =
                contentRoot.anchoredPosition;
            anchoredPosition.y = 0f;
            contentRoot.anchoredPosition =
                anchoredPosition;
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }

        private void BuildSummarySection()
        {
            RectTransform section = CreateSection(
                "현재 상태",
                88f);
            stateText = CreateText(
                "State",
                section,
                "상태 불러오는 중...",
                16,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            AddLayoutElement(
                stateText.gameObject,
                52f,
                52f);
        }

        private void BuildEnemySection()
        {
            RectTransform section = CreateSection(
                "적 생성",
                520f);
            enemyDropdown = CreateDropdown(
                "Enemy Dropdown",
                section);
            enemyDropdown.onValueChanged.AddListener(
                delegate { RefreshEnemyBaseHealth(); });

            enemyBaseHealthText = CreateText(
                "Enemy Base Health",
                section,
                "기본 HP: -",
                14,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                MutedTextColor);
            AddLayoutElement(
                enemyBaseHealthText.gameObject,
                28f,
                28f);

            quantityInput = CreateLabeledInput(
                section,
                "수량",
                DefaultSpawnQuantity.ToString(
                    CultureInfo.InvariantCulture),
                InputField.ContentType.IntegerNumber,
                stepSize: 1d,
                minimumValue: 1d,
                maximumValue: MaximumSpawnBatchSize);
            intervalInput = CreateLabeledInput(
                section,
                "간격(초)",
                "0.5",
                InputField.ContentType.DecimalNumber,
                stepSize: 0.1d,
                minimumValue: 0.02d,
                maximumValue: 60d);
            healthMultiplierInput = CreateLabeledInput(
                section,
                "체력 배율",
                "1",
                InputField.ContentType.DecimalNumber,
                stepSize: 0.25d,
                minimumValue: 0.01d,
                maximumValue: 100d);
            absoluteHealthInput = CreateLabeledInput(
                section,
                "절대 HP (0=기본×배율)",
                "0",
                InputField.ContentType.IntegerNumber,
                stepSize: 10d,
                minimumValue: 0d,
                maximumValue: 1000000000d);
            speedMultiplierInput = CreateLabeledInput(
                section,
                "이동속도 배율",
                "1",
                InputField.ContentType.DecimalNumber,
                stepSize: 0.25d,
                minimumValue: 0d,
                maximumValue: 100d);
            activeEnemyCapInput = CreateLabeledInput(
                section,
                "활성 적 상한",
                DefaultActiveEnemyLimit.ToString(
                    CultureInfo.InvariantCulture),
                InputField.ContentType.IntegerNumber,
                stepSize: 10d,
                minimumValue: 1d,
                maximumValue:
                    target.MaximumActiveEnemyCount);
            activeEnemyCapInput.onEndEdit.AddListener(
                delegate(string _)
                {
                    ApplyActiveEnemyLimit(true);
                });

            AddButtonRow(
                section,
                ("1마리", delegate { SpawnSelectedOnce(); }),
                ("배치", delegate { SpawnSelectedBatch(); }),
                ("모든 적 1마리", delegate
                {
                    SpawnEveryEnemyOnce();
                }));
            AddButtonRow(
                section,
                ("연속", BeginContinuousSpawn),
                ("무한", BeginInfiniteSpawn));

            RectTransform controlRow = CreateRow(
                "Automatic Spawn Controls",
                section,
                42f);
            pauseSpawnButton = CreateButton(
                "Pause Spawn",
                controlRow,
                "일시정지",
                ButtonColor,
                ToggleAutomaticSpawnPause);
            pauseSpawnLabel = pauseSpawnButton
                .GetComponentInChildren<Text>();
            AddFlexible(pauseSpawnButton.gameObject, 1f);
            Button stop = CreateButton(
                "Stop Spawn",
                controlRow,
                "정지",
                DangerButtonColor,
                StopAutomaticSpawn);
            AddFlexible(stop.gameObject, 1f);
            Button clear = CreateButton(
                "Clear Enemies",
                controlRow,
                "현재 적 제거",
                DangerButtonColor,
                delegate { ClearCurrentEnemies(); });
            AddFlexible(clear.gameObject, 1.35f);

            automaticSpawnText = CreateText(
                "Automatic Spawn State",
                section,
                "자동 소환: 정지",
                14,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                MutedTextColor);
            AddLayoutElement(
                automaticSpawnText.gameObject,
                30f,
                30f);
        }

        private void BuildResourceSection()
        {
            RectTransform section = CreateSection(
                "런 자원 · 전투 속도",
                184f);
            goldInput = CreateLabeledInput(
                section,
                "골드",
                "100000",
                InputField.ContentType.IntegerNumber,
                delegate
                {
                    ApplyResult(
                        target.SetGold(
                            ReadInt(
                                goldInput,
                                100000,
                                0,
                                1000000000)));
                    RefreshRuntimeState();
                },
                "적용",
                stepSize: 1000d,
                minimumValue: 0d,
                maximumValue: 1000000000d);
            baseHealthInput = CreateLabeledInput(
                section,
                "기지 체력",
                "9999",
                InputField.ContentType.IntegerNumber,
                delegate
                {
                    ApplyResult(
                        target.SetBaseHealth(
                            ReadInt(
                                baseHealthInput,
                                9999,
                                1,
                                1000000000)));
                    RefreshRuntimeState();
                },
                "적용",
                stepSize: 100d,
                minimumValue: 1d,
                maximumValue: 1000000000d);

            RectTransform speedRow = CreateRow(
                "Combat Speed Row",
                section,
                42f);
            Text label = CreateText(
                "Label",
                speedRow,
                "전투 속도",
                15,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            AddLayoutElement(label.gameObject, 148f, 42f);
            speedDropdown = CreateDropdown(
                "Combat Speed Dropdown",
                speedRow,
                false);
            speedDropdown.ClearOptions();
            speedDropdown.AddOptions(
                new List<string>
                {
                    "0.5x",
                    "1x",
                    "2x",
                    "3x"
                });
            speedDropdown.value = 1;
            speedDropdown.onValueChanged.AddListener(
                HandleCombatSpeedChanged);
            AddFlexible(speedDropdown.gameObject, 1f);
        }

        private void BuildCardSection()
        {
            RectTransform section = CreateSection(
                "카드 지급",
                168f);
            cardDropdown = CreateDropdown(
                "Card Dropdown",
                section);
            cardCountInput = CreateLabeledInput(
                section,
                "정의당 지급 수량",
                "1",
                InputField.ContentType.IntegerNumber,
                stepSize: 1d,
                minimumValue: 1d,
                maximumValue:
                    target.MaximumCardGrantCount);
            AddButtonRow(
                section,
                ("선택 카드 지급", delegate
                {
                    GrantSelectedCard();
                }),
                ("모든 카드 지급", delegate
                {
                    GrantEveryCard();
                }));
        }

        private void BuildTowerSection()
        {
            RectTransform section = CreateSection(
                "타워 배치",
                222f);
            towerDropdown = CreateDropdown(
                "Tower Dropdown",
                section);
            towerDropdown.onValueChanged.AddListener(
                delegate { ClampTowerLevelInput(); });
            towerLevelInput = CreateLabeledInput(
                section,
                "목표 레벨",
                "1",
                InputField.ContentType.IntegerNumber,
                stepSize: 1d,
                minimumValue: 1d,
                maximumValue: 99d);
            buildPointInput = CreateLabeledInput(
                section,
                "건설 지점 (-1=자동)",
                "-1",
                InputField.ContentType.IntegerNumber,
                stepSize: 1d,
                minimumValue: -1d,
                maximumValue: 999d);
            AddButtonRow(
                section,
                ("선택 타워 배치", delegate
                {
                    PlaceSelectedTower();
                }),
                ("모든 타워 배치", delegate
                {
                    PlaceEveryTower();
                }));
        }

        private void BuildLoadoutSection()
        {
            RectTransform section = CreateSection(
                "타워 카드 슬롯",
                214f);
            placedTowerDropdown = CreateDropdown(
                "Placed Tower Dropdown",
                section);
            placedTowerDropdown.onValueChanged.AddListener(
                delegate { RefreshSlotOptions(); });
            slotDropdown = CreateDropdown(
                "Tower Slot Dropdown",
                section);
            AddButtonRow(
                section,
                ("선택 카드 장착/교체", delegate
                {
                    EquipSelectedCard();
                }),
                ("슬롯 카드 제거", delegate
                {
                    RemoveSelectedSlotCard();
                }));
            AddButtonRow(
                section,
                ("선택 타워 레벨 적용", delegate
                {
                    SetSelectedPlacedTowerLevel();
                }));

            Text hint = CreateText(
                "Loadout Hint",
                section,
                "위 '카드 지급' 목록에서 카드를 고른 뒤 슬롯에 장착합니다.",
                13,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                MutedTextColor);
            AddLayoutElement(hint.gameObject, 34f, 34f);
        }

        private void BuildStatusSection()
        {
            RectTransform section = CreateSection(
                "작업 결과",
                90f);
            statusText = CreateText(
                "Status",
                section,
                string.Empty,
                14,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                MutedTextColor);
            statusText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            statusText.verticalOverflow =
                VerticalWrapMode.Truncate;
            AddLayoutElement(
                statusText.gameObject,
                52f,
                52f);
        }

        private void PopulateContentOptions()
        {
            PopulateDropdown(
                enemyDropdown,
                target.EnemyOptions,
                delegate(TestLabEnemyOption option)
                {
                    return option.Content.ToString();
                });
            PopulateDropdown(
                towerDropdown,
                target.TowerOptions,
                delegate(TestLabTowerOption option)
                {
                    return option.Content.ToString() +
                        " · 슬롯 " + option.SlotCount +
                        " · 최대 Lv." + option.MaximumLevel;
                });
            PopulateDropdown(
                cardDropdown,
                target.CardOptions,
                delegate(TestLabContentOption option)
                {
                    return option.ToString();
                });
            RefreshEnemyBaseHealth();
            ClampTowerLevelInput();
        }
    }
}
