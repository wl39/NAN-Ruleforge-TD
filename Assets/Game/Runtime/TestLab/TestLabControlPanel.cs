using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UnityView.TestLab
{
    /// <summary>
    /// TestLab 씬 전용 런타임 도구 패널.
    /// 모든 규칙 변경은 <see cref="ITestLabControlTarget"/>으로 보내며,
    /// Stage01 HUD나 구체 GameSimulation 구현에는 의존하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class TestLabControlPanel : MonoBehaviour
    {
        private enum AutomaticSpawnState
        {
            Stopped = 0,
            Running = 1,
            Paused = 2
        }

        private static readonly Color PanelColor =
            new Color32(20, 27, 38, 246);
        private static readonly Color SectionColor =
            new Color32(35, 46, 61, 245);
        private static readonly Color FieldColor =
            new Color32(16, 22, 31, 255);
        private static readonly Color ButtonColor =
            new Color32(57, 104, 132, 255);
        private static readonly Color ImportantButtonColor =
            new Color32(190, 111, 42, 255);
        private static readonly Color DangerButtonColor =
            new Color32(145, 61, 63, 255);
        private static readonly Color TextColor =
            new Color32(239, 244, 248, 255);
        private static readonly Color MutedTextColor =
            new Color32(177, 192, 205, 255);
        private static readonly Color SuccessColor =
            new Color32(135, 224, 164, 255);
        private static readonly Color FailureColor =
            new Color32(255, 143, 135, 255);

        private const int MaximumSpawnCallsPerFrame = 8;
        private const float StateRefreshInterval = 0.2f;

        private ITestLabControlTarget target;
        private Font font;
        private Canvas canvas;
        private RectTransform safeAreaRoot;
        private RectTransform panelRoot;
        private RectTransform debuffToolbarRoot;
        private TestLabResponsiveLayout responsiveLayout;
        private ScrollRect debuffScrollRect;
        private RectTransform debuffContentRoot;
        private readonly List<Button> debuffButtons =
            new List<Button>();
        private readonly List<string> debuffButtonCardIds =
            new List<string>();
        private ScrollRect scrollRect;
        private RectTransform contentRoot;
        private Text statusText;
        private Text stateText;
        private Text enemyBaseHealthText;
        private Text automaticSpawnText;
        private GameObject reopenButton;
        private Dropdown enemyDropdown;
        private Dropdown towerDropdown;
        private Dropdown cardDropdown;
        private Dropdown placedTowerDropdown;
        private Dropdown slotDropdown;
        private Dropdown speedDropdown;
        private InputField quantityInput;
        private InputField intervalInput;
        private InputField healthMultiplierInput;
        private InputField absoluteHealthInput;
        private InputField speedMultiplierInput;
        private InputField activeEnemyCapInput;
        private InputField goldInput;
        private InputField baseHealthInput;
        private InputField cardCountInput;
        private InputField towerLevelInput;
        private InputField buildPointInput;
        private Button pauseSpawnButton;
        private Text pauseSpawnLabel;
        private bool built;
        private bool panelVisible = true;
        private bool hiddenForTowerLoadout;
        private AutomaticSpawnState automaticSpawnState;
        private bool automaticSpawnInfinite;
        private int automaticSpawnRemaining;
        private float automaticSpawnTimer;
        private float stateRefreshTimer;
        private int appliedActiveEnemyCap;
        private int lastPlacedTowerSignature = int.MinValue;
        private TestLabRuntimeState lastState;

        public bool IsBuilt => built;
        public bool IsPanelVisible => panelVisible;
        public bool IsAutomaticSpawnRunning =>
            automaticSpawnState == AutomaticSpawnState.Running;
        public bool IsAutomaticSpawnPaused =>
            automaticSpawnState == AutomaticSpawnState.Paused;
        public bool IsAutomaticSpawnInfinite => automaticSpawnInfinite;
        public int AutomaticSpawnRemaining => automaticSpawnRemaining;
        public int DefaultActiveEnemyLimit =>
            target == null
                ? 1
                : Mathf.Clamp(
                    target.DefaultActiveEnemyLimit,
                    1,
                    target.MaximumActiveEnemyCount);
        public int MaximumSpawnBatchSize =>
            target == null
                ? 1
                : Math.Max(
                    1,
                    target.MaximumSpawnBatchSize);
        public int ActiveEnemyCap => ReadInt(
            activeEnemyCapInput,
            DefaultActiveEnemyLimit,
            1,
            target == null
                ? 1
                : target.MaximumActiveEnemyCount);
        public int EnemyOptionCount =>
            target == null ? 0 : target.EnemyOptions.Count;
        public int TowerOptionCount =>
            target == null ? 0 : target.TowerOptions.Count;
        public int CardOptionCount =>
            target == null ? 0 : target.CardOptions.Count;
        public int DebuffOptionCount =>
            target == null ? 0 : target.DebuffOptions.Count;
        public TestLabRuntimeState CurrentRuntimeState =>
            lastState;
        public RectTransform PanelRoot => panelRoot;
        public RectTransform SafeAreaRoot => safeAreaRoot;
        public TestLabResponsiveLayout ResponsiveLayout =>
            responsiveLayout;
        public RectTransform DebuffToolbarRoot =>
            debuffToolbarRoot;
        public ScrollRect DebuffScrollRect =>
            debuffScrollRect;
        public ScrollRect ScrollRect => scrollRect;
        public Text StatusText => statusText;
        public Text StateText => stateText;
        public Dropdown EnemyDropdown => enemyDropdown;
        public Dropdown TowerDropdown => towerDropdown;
        public Dropdown CardDropdown => cardDropdown;
        public Dropdown PlacedTowerDropdown => placedTowerDropdown;
        public Dropdown SlotDropdown => slotDropdown;
        public InputField SpawnIntervalInput => intervalInput;
        public InputField ActiveEnemyCapInput =>
            activeEnemyCapInput;

        private void Update()
        {
            if (!built || target == null)
            {
                return;
            }

            SynchronizePanelWithTowerLoadout();
            AdvanceAutomaticSpawner(Time.unscaledDeltaTime);
            stateRefreshTimer -= Time.unscaledDeltaTime;
            if (stateRefreshTimer <= 0f)
            {
                stateRefreshTimer = StateRefreshInterval;
                RefreshRuntimeState();
            }
        }

        private void OnDestroy()
        {
            StopAutomaticSpawn(false);
        }

        public static TestLabControlPanel CreateRuntime(
            ITestLabControlTarget controlTarget,
            Font uiFont,
            Transform parent = null)
        {
            var host = new GameObject("TestLab Control Panel");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            TestLabControlPanel panel =
                host.AddComponent<TestLabControlPanel>();
            panel.Configure(controlTarget, uiFont);
            return panel;
        }

        public void Configure(
            ITestLabControlTarget controlTarget,
            Font uiFont)
        {
            if (controlTarget == null)
            {
                throw new ArgumentNullException(nameof(controlTarget));
            }

            target = controlTarget;
            appliedActiveEnemyCap =
                DefaultActiveEnemyLimit;
            font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");

            if (!built)
            {
                BuildInterface();
            }

            activeEnemyCapInput.SetTextWithoutNotify(
                DefaultActiveEnemyLimit.ToString(
                    CultureInfo.InvariantCulture));
            quantityInput.SetTextWithoutNotify(
                DefaultSpawnQuantity.ToString(
                    CultureInfo.InvariantCulture));
            PopulateContentOptions();
            ApplyActiveEnemyLimit(false);
            RefreshRuntimeState();
            SetStatus(
                "TestLab 준비 완료 · 카드 " +
                target.CardOptions.Count +
                "종 · Stage01 권위 시뮬레이션 연결됨",
                true);
            RebuildPanelLayout();
        }

        public void SetVisible(bool visible)
        {
            hiddenForTowerLoadout = false;
            ApplyPanelVisibility(visible, !visible);
        }

        private void SynchronizePanelWithTowerLoadout()
        {
            if (target.IsTowerLoadoutVisible)
            {
                if (panelVisible)
                {
                    hiddenForTowerLoadout = true;
                    ApplyPanelVisibility(false, false);
                }

                return;
            }

            if (hiddenForTowerLoadout)
            {
                hiddenForTowerLoadout = false;
                ApplyPanelVisibility(true, false);
            }
        }

        private void ApplyPanelVisibility(
            bool visible,
            bool showReopenButton)
        {
            panelVisible = visible;
            if (panelRoot != null)
            {
                panelRoot.gameObject.SetActive(visible);
            }
            if (debuffToolbarRoot != null)
            {
                debuffToolbarRoot.gameObject.SetActive(visible);
            }

            if (reopenButton != null)
            {
                reopenButton.SetActive(
                    !visible && showReopenButton);
            }
        }

        public Button GetDebuffButton(string cardId)
        {
            for (int index = 0;
                 index < debuffButtonCardIds.Count;
                 index++)
            {
                if (string.Equals(
                        debuffButtonCardIds[index],
                        cardId,
                        StringComparison.Ordinal))
                {
                    return debuffButtons[index];
                }
            }

            return null;
        }

        public TestLabCommandResult ApplyDebuffToAllEnemies(
            string cardId)
        {
            TestLabCommandResult result =
                target.ApplyDebuffToAllEnemies(cardId);
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        public TestLabCommandResult GrantSelectedCard()
        {
            TestLabContentOption option = GetSelectedCard();
            if (string.IsNullOrWhiteSpace(option.StableId))
            {
                return FailLocally("선택 가능한 카드가 없습니다.");
            }

            TestLabCommandResult result = target.GrantCard(
                option.StableId,
                ReadInt(
                    cardCountInput,
                    1,
                    1,
                    target.MaximumCardGrantCount));
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        public TestLabCommandResult GrantEveryCard()
        {
            TestLabCommandResult result =
                target.GrantEveryCard(
                    ReadInt(
                        cardCountInput,
                        1,
                        1,
                        target.MaximumCardGrantCount));
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        public TestLabCommandResult PlaceSelectedTower()
        {
            TestLabTowerOption option = GetSelectedTower();
            if (string.IsNullOrWhiteSpace(option.StableId))
            {
                return FailLocally("선택 가능한 타워가 없습니다.");
            }

            int level = ReadInt(
                towerLevelInput,
                1,
                1,
                option.MaximumLevel);
            int buildPoint = ReadInt(
                buildPointInput,
                -1,
                -1,
                999);
            TestLabCommandResult result = target.PlaceTower(
                option.StableId,
                buildPoint,
                level);
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        public TestLabCommandResult PlaceEveryTower()
        {
            TestLabCommandResult result =
                target.PlaceEveryTower(
                    ReadInt(towerLevelInput, 1, 1, 99));
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        public TestLabCommandResult EquipSelectedCard()
        {
            if (!TryGetSelectedPlacedTower(
                    out TestLabPlacedTower tower))
            {
                return FailLocally("먼저 배치된 타워를 선택하세요.");
            }

            TestLabContentOption card = GetSelectedCard();
            int slot = slotDropdown == null
                ? -1
                : slotDropdown.value;
            if (string.IsNullOrWhiteSpace(card.StableId) ||
                slot < 0 ||
                slot >= tower.SlotCount)
            {
                return FailLocally("카드와 유효한 슬롯을 선택하세요.");
            }

            TestLabCommandResult result = target.EquipCard(
                card.StableId,
                tower.InstanceId,
                slot);
            ApplyResult(result);
            RefreshRuntimeState(true);
            return result;
        }

        public TestLabCommandResult RemoveSelectedSlotCard()
        {
            if (!TryGetSelectedPlacedTower(
                    out TestLabPlacedTower tower))
            {
                return FailLocally("먼저 배치된 타워를 선택하세요.");
            }

            int slot = slotDropdown == null
                ? -1
                : slotDropdown.value;
            if (slot < 0 || slot >= tower.SlotCount)
            {
                return FailLocally("유효한 슬롯을 선택하세요.");
            }

            TestLabCommandResult result = target.RemoveCard(
                tower.InstanceId,
                slot);
            ApplyResult(result);
            RefreshRuntimeState(true);
            return result;
        }

        public TestLabCommandResult SetSelectedPlacedTowerLevel()
        {
            if (!TryGetSelectedPlacedTower(
                    out TestLabPlacedTower tower))
            {
                return FailLocally("먼저 배치된 타워를 선택하세요.");
            }

            int maximumLevel = 99;
            IReadOnlyList<TestLabTowerOption> definitions =
                target.TowerOptions;
            for (int i = 0; i < definitions.Count; i++)
            {
                if (string.Equals(
                        definitions[i].StableId,
                        tower.DefinitionId,
                        StringComparison.Ordinal))
                {
                    maximumLevel =
                        definitions[i].MaximumLevel;
                    break;
                }
            }

            int level = ReadInt(
                towerLevelInput,
                tower.Level,
                1,
                maximumLevel);
            TestLabCommandResult result =
                target.SetTowerLevel(
                    tower.InstanceId,
                    level);
            ApplyResult(result);
            RefreshRuntimeState(true);
            return result;
        }

        private void RefreshRuntimeState(bool forceSelectors = false)
        {
            if (target == null)
            {
                return;
            }

            lastState = target.ReadState();
            if (stateText != null)
            {
                stateText.text =
                    "활성 적 " + lastState.ActiveEnemyCount +
                    " / " + ActiveEnemyCap +
                    "    골드 " + lastState.Gold +
                    "    기지 HP " + lastState.BaseHealth +
                    "\n배치 타워 " +
                    lastState.PlacedTowers.Length;
            }

            int signature = ComputeTowerSignature(
                lastState.PlacedTowers);
            if (forceSelectors ||
                signature != lastPlacedTowerSignature)
            {
                lastPlacedTowerSignature = signature;
                RefreshPlacedTowerOptions();
            }
        }

        private void RefreshPlacedTowerOptions()
        {
            int preservedTowerId = -1;
            if (TryGetSelectedPlacedTower(
                    out TestLabPlacedTower selected))
            {
                preservedTowerId = selected.InstanceId;
            }

            var options = new List<string>();
            int selectedIndex = 0;
            for (int i = 0;
                 i < lastState.PlacedTowers.Length;
                 i++)
            {
                TestLabPlacedTower tower =
                    lastState.PlacedTowers[i];
                options.Add(tower.ToString());
                if (tower.InstanceId == preservedTowerId)
                {
                    selectedIndex = i;
                }
            }

            if (options.Count == 0)
            {
                options.Add("(배치된 타워 없음)");
            }

            placedTowerDropdown.ClearOptions();
            placedTowerDropdown.AddOptions(options);
            placedTowerDropdown.SetValueWithoutNotify(
                Mathf.Clamp(selectedIndex, 0, options.Count - 1));
            placedTowerDropdown.interactable =
                lastState.PlacedTowers.Length > 0;
            RefreshSlotOptions();
        }

        private void RefreshSlotOptions()
        {
            var options = new List<string>();
            if (TryGetSelectedPlacedTower(
                    out TestLabPlacedTower tower))
            {
                for (int slot = 0;
                     slot < tower.SlotCount;
                     slot++)
                {
                    options.Add(tower.GetSlotLabel(slot));
                }
            }

            if (options.Count == 0)
            {
                options.Add("(슬롯 없음)");
            }

            int previous = slotDropdown.value;
            slotDropdown.ClearOptions();
            slotDropdown.AddOptions(options);
            slotDropdown.SetValueWithoutNotify(
                Mathf.Clamp(previous, 0, options.Count - 1));
            slotDropdown.interactable =
                TryGetSelectedPlacedTower(
                    out TestLabPlacedTower selected) &&
                selected.SlotCount > 0;
        }

        private void ClampTowerLevelInput()
        {
            if (towerLevelInput == null)
            {
                return;
            }

            TestLabTowerOption tower = GetSelectedTower();
            int level = ReadInt(
                towerLevelInput,
                1,
                1,
                Math.Max(1, tower.MaximumLevel));
            towerLevelInput.text =
                level.ToString(CultureInfo.InvariantCulture);
        }

        private void HandleCombatSpeedChanged(int index)
        {
            float[] speeds = { 0.5f, 1f, 2f, 3f };
            float selected = speeds[
                Mathf.Clamp(index, 0, speeds.Length - 1)];
            float applied = target.SetCombatSpeed(selected);
            SetStatus(
                "전투 속도 " +
                applied.ToString("0.##", CultureInfo.InvariantCulture) +
                "x",
                true);
        }

        private void ApplyResult(TestLabCommandResult result)
        {
            SetStatus(
                result.Message,
                result.Succeeded);
        }

        private TestLabCommandResult FailLocally(string message)
        {
            TestLabCommandResult result =
                TestLabCommandResult.Failure(message);
            ApplyResult(result);
            return result;
        }

        private void SetStatus(string message, bool success)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = string.IsNullOrWhiteSpace(message)
                ? (success ? "완료" : "실패")
                : message;
            statusText.color =
                success ? SuccessColor : FailureColor;
        }

        private int DefaultSpawnQuantity =>
            Math.Min(10, MaximumSpawnBatchSize);

        private TestLabEnemyOption GetSelectedEnemy()
        {
            IReadOnlyList<TestLabEnemyOption> options =
                target == null
                    ? null
                    : target.EnemyOptions;
            if (options == null ||
                options.Count == 0 ||
                enemyDropdown == null)
            {
                return default(TestLabEnemyOption);
            }

            return options[Mathf.Clamp(
                enemyDropdown.value,
                0,
                options.Count - 1)];
        }

        private TestLabTowerOption GetSelectedTower()
        {
            IReadOnlyList<TestLabTowerOption> options =
                target == null
                    ? null
                    : target.TowerOptions;
            if (options == null ||
                options.Count == 0 ||
                towerDropdown == null)
            {
                return default(TestLabTowerOption);
            }

            return options[Mathf.Clamp(
                towerDropdown.value,
                0,
                options.Count - 1)];
        }

        private TestLabContentOption GetSelectedCard()
        {
            IReadOnlyList<TestLabContentOption> options =
                target == null
                    ? null
                    : target.CardOptions;
            if (options == null ||
                options.Count == 0 ||
                cardDropdown == null)
            {
                return default(TestLabContentOption);
            }

            return options[Mathf.Clamp(
                cardDropdown.value,
                0,
                options.Count - 1)];
        }

        private bool TryGetSelectedPlacedTower(
            out TestLabPlacedTower tower)
        {
            if (lastState.PlacedTowers != null &&
                lastState.PlacedTowers.Length > 0 &&
                placedTowerDropdown != null)
            {
                int index = Mathf.Clamp(
                    placedTowerDropdown.value,
                    0,
                    lastState.PlacedTowers.Length - 1);
                tower = lastState.PlacedTowers[index];
                return true;
            }

            tower = default(TestLabPlacedTower);
            return false;
        }

        private static int ComputeTowerSignature(
            TestLabPlacedTower[] towers)
        {
            unchecked
            {
                int hash = 17;
                if (towers == null)
                {
                    return hash;
                }

                for (int i = 0; i < towers.Length; i++)
                {
                    TestLabPlacedTower tower = towers[i];
                    hash = hash * 31 + tower.InstanceId;
                    hash = hash * 31 + tower.Level;
                    for (int slot = 0;
                         slot < tower.SlotCount;
                         slot++)
                    {
                        hash = hash * 31 +
                            (tower.SlotCardIds[slot] ?? string.Empty)
                            .GetHashCode();
                    }
                }

                return hash;
            }
        }

    }
}
