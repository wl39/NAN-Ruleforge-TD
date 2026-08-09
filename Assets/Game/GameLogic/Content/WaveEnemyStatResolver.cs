using System;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Content
{
    /// <summary>
    /// 웨이브 스폰 데이터와 엘리트 특성을 모두 적용한 최종 생성 능력치다.
    /// 예고 UI와 실제 스폰이 이 값을 함께 사용해 서로 다른 계산식을 갖지 않는다.
    /// </summary>
    public readonly struct ResolvedWaveEnemyStats
    {
        public ResolvedWaveEnemyStats(
            long maxHealthMilli,
            int armor,
            int speedMilliPerTick,
            int rewardBudget,
            long shieldMilli,
            int renderScaleBps)
        {
            MaxHealthMilli = Math.Max(1L, maxHealthMilli);
            Armor = Math.Max(0, armor);
            SpeedMilliPerTick = Math.Max(1, speedMilliPerTick);
            RewardBudget = Math.Max(0, rewardBudget);
            ShieldMilli = Math.Max(0L, shieldMilli);
            RenderScaleBps = Math.Max(1000, renderScaleBps);
        }

        public long MaxHealthMilli { get; }
        public int Armor { get; }
        public int SpeedMilliPerTick { get; }
        public int RewardBudget { get; }
        public long ShieldMilli { get; }
        public int RenderScaleBps { get; }
    }

    /// <summary>
    /// 확정된 웨이브 스폰 한 묶음의 최종 능력치를 결정론적으로 계산한다.
    /// </summary>
    public static class WaveEnemyStatResolver
    {
        /// <summary>
        /// 이어하기 스테이지에서 한 스폰 묶음의 수를 직전 스테이지 대비
        /// 정확히 두 배씩 늘린다. 매우 먼 스테이지의 정수 오버플로는
        /// int 최댓값에서 포화시켜 결정성을 유지한다.
        /// </summary>
        public static int ResolveSpawnCount(
            int authoredCount,
            int stageNumber)
        {
            return (int)MultiplyByStageDoublings(
                Math.Max(0, authoredCount),
                stageNumber,
                int.MaxValue);
        }

        public static ResolvedWaveEnemyStats Resolve(
            CompiledContent content,
            in CompiledWaveSpawn spawn)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return Resolve(
                content,
                content.GetEnemy(spawn.EnemyId),
                spawn.EliteTraitIdsInternal);
        }

        /// <summary>
        /// 기본 스폰/엘리트 조합을 먼저 계산한 뒤 이어하기 스테이지 배율을
        /// 적용한다. 첫 스테이지는 원본 수치를 그대로 사용한다.
        /// </summary>
        public static ResolvedWaveEnemyStats Resolve(
            CompiledContent content,
            in CompiledWaveSpawn spawn,
            int stageNumber)
        {
            return ApplyEndlessStage(
                Resolve(content, spawn),
                stageNumber);
        }

        public static ResolvedWaveEnemyStats Resolve(
            CompiledContent content,
            CompiledEnemyDefinition definition,
            EliteTraitId[] eliteTraitIds)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            long maxHealthMilli = definition.MaxHealthMilli;
            int armor = definition.Armor;
            int speedMilliPerTick = definition.SpeedMilliPerTick;
            int rewardBudget = definition.RewardBudget;
            long shieldMilli = 0L;
            int renderScaleBps = 10000;
            EliteTraitId[] traits =
                eliteTraitIds ?? Array.Empty<EliteTraitId>();

            for (int i = 0; i < traits.Length; i++)
            {
                CompiledEliteTraitDefinition trait =
                    content.GetEliteTrait(traits[i]);
                maxHealthMilli = Math.Max(
                    1L,
                    DeterministicMath.MultiplyBasisPoints(
                        maxHealthMilli,
                        trait.HealthMultiplierBps));
                armor = Math.Max(
                    0,
                    (int)DeterministicMath.MultiplyBasisPoints(
                        armor,
                        trait.ArmorMultiplierBps));
                speedMilliPerTick = Math.Max(
                    1,
                    (int)DeterministicMath.MultiplyBasisPoints(
                        speedMilliPerTick,
                        trait.SpeedMultiplierBps));
                rewardBudget = MultiplyRewardBudget(
                    rewardBudget,
                    trait.RewardMultiplierBps);
                shieldMilli = checked(
                    shieldMilli +
                    Math.Max(
                        0L,
                        DeterministicMath.MultiplyBasisPoints(
                            definition.MaxHealthMilli,
                            trait.ShieldBaseHealthBps)));
                renderScaleBps = Math.Max(
                    1000,
                    (int)DeterministicMath.MultiplyBasisPoints(
                        renderScaleBps,
                        trait.RenderScaleBps));
            }

            return new ResolvedWaveEnemyStats(
                maxHealthMilli,
                armor,
                speedMilliPerTick,
                rewardBudget,
                shieldMilli,
                renderScaleBps);
        }

        /// <summary>
        /// 이어하기 한 단계마다 체력과 방어력은 두 배, 몬스터 기본 드롭은
        /// 직전 단계 값의 정수 제곱근으로 바꾼다. 골드는 정수 재화이므로
        /// floor(sqrt)를 사용하고 양수 보상은 최소 1을 보존한다.
        /// </summary>
        public static ResolvedWaveEnemyStats ApplyEndlessStage(
            in ResolvedWaveEnemyStats baseStats,
            int stageNumber)
        {
            return new ResolvedWaveEnemyStats(
                MultiplyByStageDoublings(
                    baseStats.MaxHealthMilli,
                    stageNumber,
                    long.MaxValue),
                (int)MultiplyByStageDoublings(
                    baseStats.Armor,
                    stageNumber,
                    int.MaxValue),
                baseStats.SpeedMilliPerTick,
                ApplyRewardRoots(
                    baseStats.RewardBudget,
                    stageNumber),
                baseStats.ShieldMilli,
                baseStats.RenderScaleBps);
        }

        public static int ApplyRewardRoots(
            int authoredReward,
            int stageNumber)
        {
            int reward = Math.Max(0, authoredReward);
            int rootsRemaining = Math.Max(0, stageNumber - 1);
            while (rootsRemaining > 0 && reward > 1)
            {
                reward = IntegerSquareRoot(reward);
                rootsRemaining--;
            }

            return reward;
        }

        private static long MultiplyByStageDoublings(
            long value,
            int stageNumber,
            long maximum)
        {
            long result = Math.Max(0L, value);
            int doublingsRemaining = Math.Max(0, stageNumber - 1);
            while (doublingsRemaining > 0 &&
                   result > 0L &&
                   result < maximum)
            {
                result = result > maximum / 2L
                    ? maximum
                    : result * 2L;
                doublingsRemaining--;
            }

            return result;
        }

        private static int IntegerSquareRoot(int value)
        {
            if (value <= 1)
            {
                return Math.Max(0, value);
            }

            // Math.Sqrt는 초기 추정에만 사용하고 정수 보정으로 플랫폼별
            // 부동소수점 경계 차이가 최종 골드에 영향을 주지 않게 한다.
            int root = (int)Math.Sqrt(value);
            while ((long)(root + 1) * (root + 1) <= value)
            {
                root++;
            }
            while ((long)root * root > value)
            {
                root--;
            }

            return Math.Max(1, root);
        }

        /// <summary>
        /// 골드 예산은 정수이므로 양의 소수 결과를 올림한다. 보상 배율이
        /// 100%보다 큰 엘리트가 낮은 기본 보상 때문에 일반형과 같은 골드를
        /// 주는 일을 막는다.
        /// </summary>
        public static int MultiplyRewardBudget(
            int rewardBudget,
            int multiplierBps)
        {
            return checked((int)MultiplyRewardBudgetLong(
                rewardBudget,
                multiplierBps));
        }

        internal static long MultiplyRewardBudgetLong(
            long rewardBudget,
            int multiplierBps)
        {
            if (rewardBudget <= 0L || multiplierBps <= 0)
            {
                return 0L;
            }

            long numerator = checked(
                rewardBudget * multiplierBps);
            return checked(
                (numerator +
                 DeterministicMath.BasisPointScale - 1L) /
                DeterministicMath.BasisPointScale);
        }
    }
}
