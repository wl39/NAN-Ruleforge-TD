using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UnityView.TestLab
{
    public sealed partial class TestLabControlPanel
    {
        public TestLabCommandResult SpawnSelectedOnce()
        {
            return SpawnSelectedImmediate(1);
        }

        public TestLabCommandResult SpawnSelectedBatch()
        {
            return SpawnSelectedImmediate(
                ReadInt(
                    quantityInput,
                    DefaultSpawnQuantity,
                    1,
                    MaximumSpawnBatchSize));
        }

        public void BeginContinuousSpawn()
        {
            if (!ApplyActiveEnemyLimit(false).Succeeded)
            {
                return;
            }

            int quantity = ReadInt(
                quantityInput,
                DefaultSpawnQuantity,
                1,
                MaximumSpawnBatchSize);
            automaticSpawnInfinite = false;
            automaticSpawnRemaining = quantity;
            automaticSpawnTimer = 0f;
            automaticSpawnState = AutomaticSpawnState.Running;
            RefreshAutomaticSpawnLabel();
            SetStatus(
                "연속 소환 시작 · " + quantity + "마리",
                true);
        }

        public void BeginInfiniteSpawn()
        {
            if (!ApplyActiveEnemyLimit(false).Succeeded)
            {
                return;
            }

            automaticSpawnInfinite = true;
            automaticSpawnRemaining = 0;
            automaticSpawnTimer = 0f;
            automaticSpawnState = AutomaticSpawnState.Running;
            RefreshAutomaticSpawnLabel();
            SetStatus(
                "무한 소환 시작 · 활성 적 상한 " +
                ActiveEnemyCap,
                true);
        }

        public void ToggleAutomaticSpawnPause()
        {
            if (automaticSpawnState == AutomaticSpawnState.Stopped)
            {
                SetStatus("먼저 연속 또는 무한 소환을 시작하세요.", false);
                return;
            }

            automaticSpawnState =
                automaticSpawnState == AutomaticSpawnState.Paused
                    ? AutomaticSpawnState.Running
                    : AutomaticSpawnState.Paused;
            RefreshAutomaticSpawnLabel();
            SetStatus(
                automaticSpawnState == AutomaticSpawnState.Paused
                    ? "자동 소환 일시정지"
                    : "자동 소환 재개",
                true);
        }

        public void StopAutomaticSpawn()
        {
            StopAutomaticSpawn(true);
        }

        public TestLabCommandResult ClearCurrentEnemies()
        {
            TestLabCommandResult result = target.RemoveAllEnemies();
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        public TestLabCommandResult SpawnEveryEnemyOnce()
        {
            TestLabCommandResult limit =
                ApplyActiveEnemyLimit(false);
            if (!limit.Succeeded)
            {
                return limit;
            }

            TestLabCommandResult result =
                target.SpawnEveryEnemyOnce(
                    ReadMultiplierBps(
                        healthMultiplierInput,
                        1f),
                    ReadHealthOverride(),
                    ReadSpeedMultiplierBps(
                        speedMultiplierInput,
                        1f),
                    ActiveEnemyCap);
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        private TestLabCommandResult SpawnSelectedImmediate(int count)
        {
            TestLabCommandResult limit =
                ApplyActiveEnemyLimit(false);
            if (!limit.Succeeded)
            {
                return limit;
            }

            TestLabEnemyOption option = GetSelectedEnemy();
            if (string.IsNullOrWhiteSpace(option.StableId))
            {
                return FailLocally("선택 가능한 적이 없습니다.");
            }

            int available = Math.Max(
                0,
                ActiveEnemyCap -
                target.ReadState().ActiveEnemyCount);
            int spawnCount = Math.Min(
                Math.Max(1, count),
                available);
            if (spawnCount <= 0)
            {
                return FailLocally(
                    "활성 적 상한에 도달했습니다. 적을 제거하거나 상한을 올리세요.");
            }

            TestLabCommandResult result =
                target.SpawnEnemies(
                    CreateSpawnSpec(
                        option.StableId,
                        spawnCount));
            ApplyResult(result);
            RefreshRuntimeState();
            return result;
        }

        private TestLabCommandResult ApplyActiveEnemyLimit(
            bool showStatus)
        {
            int requestedLimit = ActiveEnemyCap;
            TestLabCommandResult result =
                target.SetActiveEnemyLimit(
                    requestedLimit);
            if (result.Succeeded)
            {
                appliedActiveEnemyCap = requestedLimit;
            }

            if (activeEnemyCapInput != null)
            {
                activeEnemyCapInput.SetTextWithoutNotify(
                    (result.Succeeded
                        ? requestedLimit
                        : appliedActiveEnemyCap)
                    .ToString(CultureInfo.InvariantCulture));
            }

            if (showStatus || !result.Succeeded)
            {
                ApplyResult(result);
            }

            return result;
        }

        private void AdvanceAutomaticSpawner(float deltaTime)
        {
            if (automaticSpawnState !=
                AutomaticSpawnState.Running)
            {
                return;
            }

            automaticSpawnTimer -= Mathf.Max(0f, deltaTime);
            float interval = ReadFloat(
                intervalInput,
                0.5f,
                0.02f,
                60f);
            int calls = 0;
            while (automaticSpawnTimer <= 0f &&
                   calls < MaximumSpawnCallsPerFrame)
            {
                if (!automaticSpawnInfinite &&
                    automaticSpawnRemaining <= 0)
                {
                    StopAutomaticSpawn(false);
                    SetStatus("연속 소환 완료", true);
                    return;
                }

                if (target.ReadState().ActiveEnemyCount >=
                    ActiveEnemyCap)
                {
                    automaticSpawnTimer = interval;
                    RefreshAutomaticSpawnLabel();
                    return;
                }

                TestLabCommandResult result =
                    SpawnSelectedImmediate(1);
                if (!result.Succeeded)
                {
                    StopAutomaticSpawn(false);
                    return;
                }

                if (!automaticSpawnInfinite)
                {
                    automaticSpawnRemaining--;
                }

                automaticSpawnTimer += interval;
                calls++;
            }

            RefreshAutomaticSpawnLabel();
        }

        private TestLabEnemySpawnSpec CreateSpawnSpec(
            string enemyId,
            int count)
        {
            return new TestLabEnemySpawnSpec(
                enemyId,
                count,
                ReadMultiplierBps(
                    healthMultiplierInput,
                    1f),
                ReadHealthOverride(),
                ReadSpeedMultiplierBps(
                    speedMultiplierInput,
                    1f));
        }

        private void StopAutomaticSpawn(bool showStatus)
        {
            automaticSpawnState = AutomaticSpawnState.Stopped;
            automaticSpawnInfinite = false;
            automaticSpawnRemaining = 0;
            automaticSpawnTimer = 0f;
            RefreshAutomaticSpawnLabel();
            if (showStatus && statusText != null)
            {
                SetStatus(
                    "자동 소환 정지 · 현재 적은 유지됩니다.",
                    true);
            }
        }

        private void RefreshAutomaticSpawnLabel()
        {
            if (automaticSpawnText == null)
            {
                return;
            }

            switch (automaticSpawnState)
            {
                case AutomaticSpawnState.Running:
                    automaticSpawnText.text =
                        automaticSpawnInfinite
                            ? "자동 소환: 무한 실행 중 · 상한 " +
                              ActiveEnemyCap
                            : "자동 소환: 연속 실행 중 · 남은 수량 " +
                              automaticSpawnRemaining;
                    break;
                case AutomaticSpawnState.Paused:
                    automaticSpawnText.text =
                        "자동 소환: 일시정지";
                    break;
                default:
                    automaticSpawnText.text =
                        "자동 소환: 정지";
                    break;
            }

            if (pauseSpawnLabel != null)
            {
                pauseSpawnLabel.text =
                    automaticSpawnState ==
                    AutomaticSpawnState.Paused
                        ? "재개"
                        : "일시정지";
            }
        }

        private void RefreshEnemyBaseHealth()
        {
            if (enemyBaseHealthText == null)
            {
                return;
            }

            TestLabEnemyOption enemy = GetSelectedEnemy();
            enemyBaseHealthText.text =
                string.IsNullOrWhiteSpace(enemy.StableId)
                    ? "기본 HP: -"
                    : "기본 HP: " +
                      FormatMilliHealth(enemy.BaseHealthMilli) +
                      " · 절대 HP가 0이면 기본 HP × 체력 배율";
        }

        private int ReadHealthOverride()
        {
            return ReadInt(
                absoluteHealthInput,
                0,
                0,
                1000000000);
        }

        private static int ReadMultiplierBps(
            InputField input,
            float fallback)
        {
            float multiplier = ReadFloat(
                input,
                fallback,
                0.01f,
                100f);
            return Mathf.Max(
                1,
                Mathf.RoundToInt(multiplier * 10000f));
        }

        private static int ReadSpeedMultiplierBps(
            InputField input,
            float fallback)
        {
            float multiplier = ReadFloat(
                input,
                fallback,
                0f,
                100f);
            return Mathf.Max(
                0,
                Mathf.RoundToInt(multiplier * 10000f));
        }

        private static int ReadInt(
            InputField input,
            int fallback,
            int minimum,
            int maximum)
        {
            if (input != null &&
                int.TryParse(
                    input.text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                return Math.Max(
                    minimum,
                    Math.Min(maximum, value));
            }

            return Math.Max(
                minimum,
                Math.Min(maximum, fallback));
        }

        private static float ReadFloat(
            InputField input,
            float fallback,
            float minimum,
            float maximum)
        {
            if (input != null &&
                (float.TryParse(
                     input.text,
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out float value) ||
                 float.TryParse(
                     input.text,
                     NumberStyles.Float,
                     CultureInfo.CurrentCulture,
                     out value)))
            {
                return Mathf.Clamp(value, minimum, maximum);
            }

            return Mathf.Clamp(fallback, minimum, maximum);
        }

        private static string FormatMilliHealth(long healthMilli)
        {
            if (healthMilli % 1000L == 0L)
            {
                return (healthMilli / 1000L)
                    .ToString(CultureInfo.InvariantCulture);
            }

            return (healthMilli / 1000d)
                .ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
