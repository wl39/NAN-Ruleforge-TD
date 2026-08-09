using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 공명 탄환이 주변의 같은 태그 탄환 수에 따라 적용한 현재 피해 보너스다.
    /// 원본 피해를 따로 저장하지 않고 직전 배율과 새 배율의 비율만 적용하므로,
    /// 공명 뒤에 실행된 다른 카드의 피해 변경도 보존한다.
    /// </summary>
    internal sealed class RareProjectileResonanceRuntime
    {
        public string Tag;
        public int RadiusMilli;
        public int BonusPerAllyBps;
        public int MaximumBonusBps;
        public int AppliedBonusBps;

        public RareProjectileResonanceRuntime Clone()
        {
            return (RareProjectileResonanceRuntime)MemberwiseClone();
        }
    }

    /// <summary>적 공명의 출처와 연결 반경/증폭 수치를 보존한다.</summary>
    internal sealed class RareEnemyResonanceRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public string StatusFilter;
        public int RadiusMilli;
        public int BonusPerLinkBps;
        public int MaximumBonusBps;
    }

    /// <summary>
    /// 시간 정지 중인 탄환이 저장한 파동 수와 해제 시 피해 문맥이다.
    /// ReleaseTick은 절대 틱이라 여러 시스템에서 조회해도 시간이 중복 차감되지 않는다.
    /// </summary>
    internal sealed class RareProjectileTimeStopRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public EntityId SourceEntityId;
        public ChainId RootChainId;
        public ActivationId ActivationId;
        public EventId ParentEventId;
        public int Depth;
        public long ReleaseTick;
        public long NextStoreTick;
        public int StoreIntervalTicks;
        public int StoredCharges;
        public int MaximumCharges;
        public int DamagePerChargeBps;
        public int RadiusMilli;
        public int TargetLimit;

        public RareProjectileTimeStopRuntime Clone()
        {
            return (RareProjectileTimeStopRuntime)MemberwiseClone();
        }
    }

    /// <summary>적 시간 정지의 절대 해제 틱과 카드 출처다.</summary>
    internal sealed class RareEnemyTimeStopRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public long ReleaseTick;
    }

    /// <summary>
    /// 현재 대상의 특정 장착 카드 한 번을 같은 티어의 다른 정의로 바꾸는 임시 치환이다.
    /// </summary>
    internal sealed class RareProjectileMutationRuntime
    {
        public EntityId SubjectId;
        public int TargetCardInstanceId;
        public CardId OriginalCardId;
        public CardId ReplacementCardId;
        public int RemainingUses;
        public long ExpireTick;

        public RareProjectileMutationRuntime CloneFor(
            EntityId subjectId)
        {
            var clone =
                (RareProjectileMutationRuntime)MemberwiseClone();
            clone.SubjectId = subjectId;
            return clone;
        }
    }

    /// <summary>적 변이로 선택된 원형과 취약/강화 방향을 상태 해시에 남긴다.</summary>
    internal sealed class RareEnemyMutationRuntime
    {
        public EnemyDefinitionId OriginalDefinitionId;
        public EnemyDefinitionId ReplacementDefinitionId;
        public int WeaknessKind;
        public int WeaknessBps;
        public int SpeedBonusBps;
    }

    /// <summary>
    /// 희귀 카드 중 공명·흡수·시간 정지·변이의 권위 전투 규칙이다.
    /// 효과에서 파생되는 피해는 기존 EventQueue/ChainBudget을 거치며, 공간 후보는
    /// 거리와 EntityId로 정렬해 Editor와 WebGL에서 같은 결과를 만든다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private const int RareDefaultRadiusMilli = 1800;
        private const int RareDefaultResonanceBonusBps = 1000;
        private const int RareDefaultResonanceMaximumBps = 5000;
        private const int RareDefaultTimeStopTicks = 45;
        private const int RareDefaultTimeStoreIntervalTicks = 10;
        private const int RareDefaultTimeDamageBps = 2500;
        private const int RareDefaultMutationWeaknessBps = 2000;
        private const int RareDefaultMutationSpeedBonusBps = 1500;
        private const int RareMaximumCopiedStatuses = 16;
        private const int RareMaximumReleaseTargets = 32;

        private readonly Dictionary<int, RareProjectileResonanceRuntime>
            rareProjectileResonances =
                new Dictionary<int, RareProjectileResonanceRuntime>();
        private readonly Dictionary<int, RareEnemyResonanceRuntime>
            rareEnemyResonances =
                new Dictionary<int, RareEnemyResonanceRuntime>();
        private readonly Dictionary<int, RareProjectileTimeStopRuntime>
            rareProjectileTimeStops =
                new Dictionary<int, RareProjectileTimeStopRuntime>();
        private readonly Dictionary<int, RareEnemyTimeStopRuntime>
            rareEnemyTimeStops =
                new Dictionary<int, RareEnemyTimeStopRuntime>();
        private readonly Dictionary<long, RareProjectileMutationRuntime>
            rareProjectileMutations =
                new Dictionary<long, RareProjectileMutationRuntime>();
        private readonly Dictionary<int, RareEnemyMutationRuntime>
            rareEnemyMutations =
                new Dictionary<int, RareEnemyMutationRuntime>();

        private readonly List<ProjectileState> rareProjectileScratch =
            new List<ProjectileState>(32);
        private readonly List<EnemyState> rareEnemyScratch =
            new List<EnemyState>(32);
        private readonly List<GameEvent> rareEventScratch =
            new List<GameEvent>(32);
        private readonly List<int> rareIntKeyScratch =
            new List<int>(64);
        private readonly List<long> rareLongKeyScratch =
            new List<long>(64);
        private readonly List<CompiledCardDefinition> rareCardScratch =
            new List<CompiledCardDefinition>(32);
        private readonly List<CompiledEnemyDefinition> rareDefinitionScratch =
            new List<CompiledEnemyDefinition>(16);
        private readonly List<int> rareProgramIndexScratch =
            new List<int>(8);
        private readonly List<StatusInstance> rareStatusScratch =
            new List<StatusInstance>(16);

        /// <summary>
        /// EffectRegistry의 희귀 카드 executor가 호출하는 단일 진입점이다.
        /// operation 이름이 탄환/적 해석을 구분하므로 StableId 문자열 분기를 사용하지 않는다.
        /// </summary>
        internal void ExecuteRareResonanceAbsorbTimeMutation(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            switch (operation)
            {
                case EffectOperation.ConfigureProjectileResonance:
                    ConfigureRareProjectileResonance(context, node);
                    break;
                case EffectOperation.ApplyEnemyResonance:
                    ApplyRareEnemyResonance(context, node);
                    break;
                case EffectOperation.ConfigureProjectileAbsorb:
                    AbsorbRareProjectile(context, node);
                    break;
                case EffectOperation.ApplyEnemyAbsorb:
                    AbsorbRareEnemy(context, node);
                    break;
                case EffectOperation.ConfigureProjectileTimeStop:
                    ConfigureRareProjectileTimeStop(context, node);
                    break;
                case EffectOperation.ApplyEnemyTimeStop:
                    ApplyRareEnemyTimeStop(context, node);
                    break;
                case EffectOperation.ConfigureProjectileMutation:
                    ConfigureRareProjectileMutation(context, node);
                    break;
                case EffectOperation.ApplyEnemyMutation:
                    ApplyRareEnemyMutation(context, node);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operation),
                        operation,
                        "Unsupported rare resonance/absorb/time/mutation operation.");
            }
        }

        /// <summary>Initialize가 새 런을 준비할 때 호출해 이전 희귀 카드 상태를 비운다.</summary>
        internal void ResetRareResonanceAbsorbTimeMutationState()
        {
            rareProjectileResonances.Clear();
            rareEnemyResonances.Clear();
            rareProjectileTimeStops.Clear();
            rareEnemyTimeStops.Clear();
            rareProjectileMutations.Clear();
            rareEnemyMutations.Clear();
            rareProjectileScratch.Clear();
            rareEnemyScratch.Clear();
            rareEventScratch.Clear();
            rareIntKeyScratch.Clear();
            rareLongKeyScratch.Clear();
            rareCardScratch.Clear();
            rareDefinitionScratch.Clear();
            rareProgramIndexScratch.Clear();
            rareStatusScratch.Clear();
        }

        /// <summary>
        /// MoveProjectiles에서 일반 이동과 고급 카드 틱보다 먼저 호출한다.
        /// true이면 시간 정지가 이번 틱의 이동/파동 처리를 저장했으므로 나머지 이동을 건너뛴다.
        /// </summary>
        internal bool ProcessRareResonanceAbsorbTimeMutationProjectileTick(
            ProjectileState projectile)
        {
            if (projectile == null || !projectile.Alive)
            {
                return false;
            }

            UpdateRareProjectileResonance(projectile);
            if (!rareProjectileTimeStops.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileTimeStopRuntime timeStop))
            {
                return false;
            }

            if (tick < timeStop.ReleaseTick)
            {
                while (tick >= timeStop.NextStoreTick &&
                       timeStop.StoredCharges <
                       timeStop.MaximumCharges)
                {
                    timeStop.StoredCharges++;
                    timeStop.NextStoreTick = checked(
                        timeStop.NextStoreTick +
                        timeStop.StoreIntervalTicks);
                    AddPresentation(
                        PresentationEventType.EffectTriggered,
                        projectile.Id.Value,
                        sourceId: projectile.SourceTowerId.Value,
                        value: timeStop.StoredCharges,
                        contentId: "time_stop_store");
                }
                return true;
            }

            rareProjectileTimeStops.Remove(
                projectile.Id.Value);
            ReleaseRareProjectileTimeStop(
                projectile,
                timeStop);
            return false;
        }

        /// <summary>
        /// MoveEnemies에서 일반 이동 전에 호출한다. 시간 정지 중이면 이동을 완전히 소비한다.
        /// 상태 처리는 MoveEnemies보다 먼저 별도 단계에서 실행되므로 화상·중독·출혈 시간은 흐른다.
        /// </summary>
        internal bool TryProcessRareResonanceAbsorbTimeMutationEnemyMovement(
            EnemyState enemy)
        {
            return IsEnemyRareTimeStopped(enemy);
        }

        /// <summary>보스 특수 능력 처리에서도 동일한 완전 정지 판정을 공유한다.</summary>
        internal bool IsEnemyRareTimeStopped(EnemyState enemy)
        {
            if (enemy == null ||
                !rareEnemyTimeStops.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyTimeStopRuntime runtime))
            {
                return false;
            }

            if (tick < runtime.ReleaseTick)
            {
                return true;
            }

            rareEnemyTimeStops.Remove(enemy.Id.Value);
            AddPresentation(
                PresentationEventType.EffectTriggered,
                enemy.Id.Value,
                runtime.TowerId.Value,
                0,
                "time_stop_enemy_release");
            return false;
        }

        /// <summary>
        /// CalculateDamage에서 저주 증폭 뒤 호출한다. 공명 대상이 같은 활성 디버프를 가진
        /// 주변 적과 연결돼 있으면 상태이상 피해만 연결 수만큼 증폭한다.
        /// </summary>
        internal long ModifyDamageForRareResonance(
            EnemyState enemy,
            long amount,
            EventTags tags)
        {
            if (enemy == null ||
                amount <= 0 ||
                (tags & EventTags.DamageOverTime) == 0 ||
                !rareEnemyResonances.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyResonanceRuntime runtime))
            {
                return amount;
            }

            int linkCount = CountRareResonantEnemies(
                enemy,
                runtime,
                null);
            int bonusBps = Math.Min(
                runtime.MaximumBonusBps,
                checked(linkCount * runtime.BonusPerLinkBps));
            return bonusBps <= 0
                ? amount
                : DeterministicMath.MultiplyBasisPoints(
                    amount,
                    10000 + bonusBps);
        }

        /// <summary>
        /// ApplyStatusCore에서 저주 보정 뒤 호출한다. 같은 상태를 가진 연결 대상이 이미
        /// 존재할 때 새 상태의 최초 지속시간만 늘려 재적용에 의한 지수 증가를 막는다.
        /// </summary>
        internal CompiledEffectNode AdjustStatusNodeForRareResonance(
            EnemyState enemy,
            StatusType statusType,
            in CompiledEffectNode node)
        {
            if (enemy == null ||
                node.DurationTicks <= 0 ||
                IsRareBeneficialStatus(statusType) ||
                !rareEnemyResonances.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyResonanceRuntime runtime) ||
                !MatchesRareStatusFilter(
                    runtime.StatusFilter,
                    statusType))
            {
                return node;
            }

            int linkCount = CountRareResonantEnemies(
                enemy,
                runtime,
                statusType);
            int bonusBps = Math.Min(
                runtime.MaximumBonusBps,
                checked(linkCount * runtime.BonusPerLinkBps));
            if (bonusBps <= 0)
            {
                return node;
            }

            return new CompiledEffectNode(
                node.Operation,
                node.Amount,
                node.Amount2,
                node.Amount3,
                (int)Math.Min(
                    1_000_000L,
                    DeterministicMath.MultiplyBasisPoints(
                        node.DurationTicks,
                        10000 + bonusBps)),
                node.IntervalTicks,
                node.MaxStacks,
                node.RadiusMilli,
                node.Limit,
                node.ChanceBps,
                node.ReferenceId);
        }

        /// <summary>
        /// ProcessProgramEvent가 실제 카드 정의를 읽기 직전에 호출한다.
        /// 현재 탄환에 예약된 변이가 있으면 대상 카드 한 번만 같은 티어 정의로 치환한다.
        /// </summary>
        internal CardId ResolveRareMutatedCard(
            SubjectType subjectType,
            EntityId subjectId,
            int cardInstanceId,
            CardId originalCardId)
        {
            if (subjectType != SubjectType.Projectile)
            {
                return originalCardId;
            }

            long key = MakeRareMutationKey(
                subjectId.Value,
                cardInstanceId);
            if (!rareProjectileMutations.TryGetValue(
                    key,
                    out RareProjectileMutationRuntime runtime))
            {
                return originalCardId;
            }

            if (runtime.ExpireTick < tick ||
                runtime.OriginalCardId != originalCardId ||
                runtime.RemainingUses <= 0)
            {
                rareProjectileMutations.Remove(key);
                return originalCardId;
            }

            runtime.RemainingUses--;
            if (runtime.RemainingUses <= 0)
            {
                rareProjectileMutations.Remove(key);
            }
            AddPresentation(
                PresentationEventType.EffectTriggered,
                subjectId.Value,
                cardInstanceId,
                runtime.ReplacementCardId.Value,
                "mutation_projectile_execute");
            return runtime.ReplacementCardId;
        }

        /// <summary>
        /// 분열 직전에 구성된 공명·시간 정지·변이 상태를 새 가지에 독립 복사한다.
        /// SplitProjectile가 자식을 목록에 넣은 직후 한 번 호출해야 한다.
        /// </summary>
        internal void InheritRareResonanceAbsorbTimeMutationProjectileRuntime(
            ProjectileState original,
            ProjectileState child)
        {
            if (original == null || child == null)
            {
                return;
            }

            if (rareProjectileResonances.TryGetValue(
                    original.Id.Value,
                    out RareProjectileResonanceRuntime resonance))
            {
                rareProjectileResonances[
                    child.Id.Value] = resonance.Clone();
            }
            if (rareProjectileTimeStops.TryGetValue(
                    original.Id.Value,
                    out RareProjectileTimeStopRuntime timeStop))
            {
                rareProjectileTimeStops[
                    child.Id.Value] = timeStop.Clone();
            }

            rareLongKeyScratch.Clear();
            foreach (KeyValuePair<long, RareProjectileMutationRuntime> pair
                     in rareProjectileMutations)
            {
                if (pair.Value.SubjectId == original.Id)
                {
                    rareLongKeyScratch.Add(pair.Key);
                }
            }
            rareLongKeyScratch.Sort();
            for (int i = 0; i < rareLongKeyScratch.Count; i++)
            {
                RareProjectileMutationRuntime clone =
                    rareProjectileMutations[
                        rareLongKeyScratch[i]].CloneFor(child.Id);
                rareProjectileMutations[
                    MakeRareMutationKey(
                        child.Id.Value,
                        clone.TargetCardInstanceId)] = clone;
            }
        }

        /// <summary>틱 말 비활성 개체와 만료된 치환의 희귀 부가 상태를 정리한다.</summary>
        internal void CleanupRareResonanceAbsorbTimeMutationState()
        {
            rareIntKeyScratch.Clear();
            foreach (KeyValuePair<int, RareProjectileResonanceRuntime> pair
                     in rareProjectileResonances)
            {
                ProjectileState projectile =
                    FindProjectile(new EntityId(pair.Key));
                if (projectile == null || !projectile.Alive)
                {
                    rareIntKeyScratch.Add(pair.Key);
                }
            }
            RemoveRareProjectileKeys(
                rareProjectileResonances,
                rareIntKeyScratch);

            rareIntKeyScratch.Clear();
            foreach (KeyValuePair<int, RareProjectileTimeStopRuntime> pair
                     in rareProjectileTimeStops)
            {
                ProjectileState projectile =
                    FindProjectile(new EntityId(pair.Key));
                if (projectile == null || !projectile.Alive)
                {
                    rareIntKeyScratch.Add(pair.Key);
                }
            }
            RemoveRareProjectileKeys(
                rareProjectileTimeStops,
                rareIntKeyScratch);

            rareIntKeyScratch.Clear();
            foreach (KeyValuePair<int, RareEnemyResonanceRuntime> pair
                     in rareEnemyResonances)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(pair.Key));
                if (enemy == null || !enemy.Alive)
                {
                    rareIntKeyScratch.Add(pair.Key);
                }
            }
            RemoveRareProjectileKeys(
                rareEnemyResonances,
                rareIntKeyScratch);

            rareIntKeyScratch.Clear();
            foreach (KeyValuePair<int, RareEnemyTimeStopRuntime> pair
                     in rareEnemyTimeStops)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(pair.Key));
                if (enemy == null || !enemy.Alive)
                {
                    rareIntKeyScratch.Add(pair.Key);
                }
            }
            RemoveRareProjectileKeys(
                rareEnemyTimeStops,
                rareIntKeyScratch);

            rareIntKeyScratch.Clear();
            foreach (KeyValuePair<int, RareEnemyMutationRuntime> pair
                     in rareEnemyMutations)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(pair.Key));
                if (enemy == null || !enemy.Alive)
                {
                    rareIntKeyScratch.Add(pair.Key);
                }
            }
            RemoveRareProjectileKeys(
                rareEnemyMutations,
                rareIntKeyScratch);

            rareLongKeyScratch.Clear();
            foreach (KeyValuePair<long, RareProjectileMutationRuntime> pair
                     in rareProjectileMutations)
            {
                ProjectileState projectile =
                    FindProjectile(pair.Value.SubjectId);
                if (projectile == null ||
                    !projectile.Alive ||
                    pair.Value.ExpireTick < tick)
                {
                    rareLongKeyScratch.Add(pair.Key);
                }
            }
            for (int i = 0; i < rareLongKeyScratch.Count; i++)
            {
                rareProjectileMutations.Remove(
                    rareLongKeyScratch[i]);
            }
        }

        /// <summary>
        /// ComputeStateHash의 Finish 직전에 호출한다. 모든 Dictionary 키를 정렬해
        /// 런타임 버킷 순서가 리플레이 지문에 영향을 주지 않게 한다.
        /// </summary>
        internal void AppendRareResonanceAbsorbTimeMutationStateHash(
            ref StableHashBuilder hash)
        {
            AppendRareProjectileResonanceHash(ref hash);
            AppendRareEnemyResonanceHash(ref hash);
            AppendRareProjectileTimeStopHash(ref hash);
            AppendRareEnemyTimeStopHash(ref hash);
            AppendRareProjectileMutationHash(ref hash);
            AppendRareEnemyMutationHash(ref hash);
        }

        private void ConfigureRareProjectileResonance(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            string tag = ResolveRareProjectileTag(
                projectile,
                node.ReferenceId);
            int radius = node.RadiusMilli > 0
                ? node.RadiusMilli
                : RareDefaultRadiusMilli;
            int bonus = node.Amount > 0
                ? node.Amount
                : RareDefaultResonanceBonusBps;
            int maximum = node.Limit > 0
                ? node.Limit
                : RareDefaultResonanceMaximumBps;

            if (!rareProjectileResonances.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileResonanceRuntime runtime))
            {
                runtime = new RareProjectileResonanceRuntime
                {
                    Tag = tag,
                    RadiusMilli = radius,
                    BonusPerAllyBps = bonus,
                    MaximumBonusBps = maximum
                };
                rareProjectileResonances.Add(
                    projectile.Id.Value,
                    runtime);
            }
            else
            {
                runtime.Tag = tag;
                runtime.RadiusMilli = Math.Max(
                    runtime.RadiusMilli,
                    radius);
                runtime.BonusPerAllyBps = Math.Max(
                    runtime.BonusPerAllyBps,
                    bonus);
                runtime.MaximumBonusBps = Math.Max(
                    runtime.MaximumBonusBps,
                    maximum);
            }

            UpdateRareProjectileResonance(projectile);
            AddPresentation(
                PresentationEventType.EffectTriggered,
                projectile.Id.Value,
                projectile.SourceTowerId.Value,
                runtime.AppliedBonusBps,
                "resonance_projectile");
        }

        private void ApplyRareEnemyResonance(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            rareEnemyResonances[enemy.Id.Value] =
                new RareEnemyResonanceRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    StatusFilter = node.ReferenceId ?? string.Empty,
                    RadiusMilli = node.RadiusMilli > 0
                        ? node.RadiusMilli
                        : RareDefaultRadiusMilli,
                    BonusPerLinkBps = node.Amount > 0
                        ? node.Amount
                        : RareDefaultResonanceBonusBps,
                    MaximumBonusBps = node.Limit > 0
                        ? node.Limit
                        : RareDefaultResonanceMaximumBps
                };
            AddPresentation(
                PresentationEventType.EffectTriggered,
                enemy.Id.Value,
                context.TowerId.Value,
                node.Amount,
                "resonance_enemy");
        }

        private void AbsorbRareProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState absorber =
                FindProjectile(context.SubjectId);
            if (absorber == null || !absorber.Alive)
            {
                return;
            }

            ProjectileState absorbed =
                SelectRareProjectileAbsorbTarget(
                    absorber,
                    node.RadiusMilli > 0
                        ? node.RadiusMilli
                        : RareDefaultRadiusMilli);
            if (absorbed == null)
            {
                return;
            }

            int damageTransferBps =
                node.Amount > 0 ? node.Amount : 10000;
            int radiusTransferBps =
                node.Amount2 > 0 ? node.Amount2 : 5000;
            long damageGain =
                DeterministicMath.MultiplyBasisPoints(
                    absorbed.DamageMilli,
                    Math.Min(20000, damageTransferBps));
            int radiusGain = (int)Math.Min(
                int.MaxValue,
                DeterministicMath.MultiplyBasisPoints(
                    absorbed.RadiusMilli,
                    Math.Min(20000, radiusTransferBps)));
            absorber.DamageMilli = Math.Max(
                1,
                SaturatingAdd(
                    absorber.DamageMilli,
                    Math.Max(0, damageGain)));
            absorber.RadiusMilli = ClampPositiveInt(
                (long)absorber.RadiusMilli +
                Math.Max(0, radiusGain));

            CopyOneRareProjectileEffect(
                absorbed,
                absorber);
            absorbed.Alive = false;
            absorbed.ExpirationQueued = false;

            AddPresentation(
                PresentationEventType.EffectTriggered,
                absorber.Id.Value,
                absorbed.Id.Value,
                (int)Math.Min(int.MaxValue, damageGain),
                "absorb_projectile");
        }

        private void AbsorbRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState absorber = FindEnemy(context.SubjectId);
            if (absorber == null || !absorber.Alive)
            {
                return;
            }

            EnemyState absorbed = SelectRareEnemyAbsorbTarget(
                absorber,
                node.RadiusMilli > 0
                    ? node.RadiusMilli
                    : RareDefaultRadiusMilli);
            if (absorbed == null)
            {
                return;
            }

            int healthTransferBps =
                node.Amount > 0 ? node.Amount : 10000;
            long healthGain =
                DeterministicMath.MultiplyBasisPoints(
                    absorbed.HealthMilli,
                    Math.Min(20000, healthTransferBps));
            absorber.MaxHealthMilli = Math.Max(
                1000,
                Math.Min(
                    int.MaxValue,
                    SaturatingAdd(
                        absorber.MaxHealthMilli,
                        Math.Max(0, healthGain))));
            absorber.HealthMilli = Math.Min(
                absorber.MaxHealthMilli,
                SaturatingAdd(
                    absorber.HealthMilli,
                    Math.Max(0, healthGain)));

            int statusLimit = node.Limit > 0
                ? Math.Min(
                    RareMaximumCopiedStatuses,
                    node.Limit)
                : 4;
            CopyRareEnemyStatuses(
                absorbed,
                absorber,
                statusLimit);
            ConsumeRareAbsorbedEnemy(absorbed);

            AddPresentation(
                PresentationEventType.EffectTriggered,
                absorber.Id.Value,
                absorbed.Id.Value,
                (int)Math.Min(int.MaxValue, healthGain),
                "absorb_enemy");
        }

        private void ConfigureRareProjectileTimeStop(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            int duration = node.DurationTicks > 0
                ? node.DurationTicks
                : RareDefaultTimeStopTicks;
            duration = Math.Min(
                content.Safety.MaxProjectileLifetimeTicks,
                Math.Max(1, duration));
            int interval = node.IntervalTicks > 0
                ? node.IntervalTicks
                : RareDefaultTimeStoreIntervalTicks;
            int maximumCharges = node.Limit > 0
                ? Math.Min(32, node.Limit)
                : Math.Max(1, duration / interval);

            rareProjectileTimeStops[projectile.Id.Value] =
                new RareProjectileTimeStopRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    SourceEntityId = context.SourceEntityId,
                    RootChainId = context.RootChainId,
                    ActivationId = context.ActivationId,
                    ParentEventId = context.ParentEventId,
                    Depth = context.Depth,
                    ReleaseTick = checked(tick + duration),
                    NextStoreTick = checked(tick + interval),
                    StoreIntervalTicks = interval,
                    MaximumCharges = maximumCharges,
                    DamagePerChargeBps = node.Amount > 0
                        ? node.Amount
                        : RareDefaultTimeDamageBps,
                    RadiusMilli = node.RadiusMilli > 0
                        ? node.RadiusMilli
                        : RareDefaultRadiusMilli,
                    TargetLimit = node.Amount2 > 0
                        ? Math.Min(
                            RareMaximumReleaseTargets,
                            node.Amount2)
                        : 8
                };

            // 정지 중 MoveProjectiles가 수명을 계속 1씩 차감하므로 같은 길이만큼
            // 미리 보상해 해제 전에 수명 종료되는 것을 막는다.
            projectile.LifetimeRemaining = Math.Min(
                content.Safety.MaxProjectileLifetimeTicks,
                checked(projectile.LifetimeRemaining + duration));
            AddPresentation(
                PresentationEventType.EffectTriggered,
                projectile.Id.Value,
                context.TowerId.Value,
                duration,
                "time_stop_projectile");
        }

        private void ApplyRareEnemyTimeStop(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            int duration = node.DurationTicks > 0
                ? node.DurationTicks
                : RareDefaultTimeStopTicks;
            long releaseTick = checked(
                tick + Math.Max(1, duration));
            if (rareEnemyTimeStops.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyTimeStopRuntime existing))
            {
                existing.ReleaseTick = Math.Max(
                    existing.ReleaseTick,
                    releaseTick);
            }
            else
            {
                rareEnemyTimeStops.Add(
                    enemy.Id.Value,
                    new RareEnemyTimeStopRuntime
                    {
                        TowerId = context.TowerId,
                        CardId = context.CardId,
                        CardInstanceId = context.CardInstanceId,
                        ReleaseTick = releaseTick
                    });
            }
            AddPresentation(
                PresentationEventType.EffectTriggered,
                enemy.Id.Value,
                context.TowerId.Value,
                duration,
                "time_stop_enemy");
        }

        private void ConfigureRareProjectileMutation(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            TowerState tower = FindTower(context.TowerId);
            if (projectile == null ||
                !projectile.Alive ||
                tower == null)
            {
                return;
            }

            int targetProgramIndex =
                SelectRareMutationProgramIndex(
                    tower,
                    context,
                    SubjectType.Projectile);
            if (targetProgramIndex < 0)
            {
                return;
            }

            CardId originalCardId =
                tower.Program[targetProgramIndex];
            CardId replacementCardId =
                SelectRareMutationReplacementCard(
                    originalCardId,
                    SubjectType.Projectile,
                    context,
                    node.ReferenceId);
            if (!replacementCardId.IsValid)
            {
                return;
            }

            int targetInstanceId =
                tower.ProgramInstances[targetProgramIndex];
            int lifetime = node.DurationTicks > 0
                ? node.DurationTicks
                : content.Safety.MaxProjectileLifetimeTicks;
            var runtime = new RareProjectileMutationRuntime
            {
                SubjectId = projectile.Id,
                TargetCardInstanceId = targetInstanceId,
                OriginalCardId = originalCardId,
                ReplacementCardId = replacementCardId,
                RemainingUses = node.Limit > 0
                    ? Math.Min(4, node.Limit)
                    : 1,
                ExpireTick = checked(tick + Math.Max(1, lifetime))
            };
            rareProjectileMutations[
                MakeRareMutationKey(
                    projectile.Id.Value,
                    targetInstanceId)] = runtime;

            AddPresentation(
                PresentationEventType.EffectTriggered,
                projectile.Id.Value,
                targetInstanceId,
                replacementCardId.Value,
                "mutation_projectile");
        }

        private void ApplyRareEnemyMutation(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            CompiledEnemyDefinition original =
                content.GetEnemy(enemy.DefinitionId);
            CompiledEnemyDefinition replacement =
                SelectRareMutationEnemyDefinition(
                    original,
                    context,
                    node.ReferenceId);
            if (replacement == null)
            {
                return;
            }

            long oldMax = Math.Max(1, enemy.MaxHealthMilli);
            long oldHealth = Math.Max(0, enemy.HealthMilli);
            long replacementMax =
                Math.Max(1000, replacement.MaxHealthMilli);
            enemy.DefinitionId = replacement.Id;
            enemy.MaxHealthMilli = replacementMax;
            enemy.HealthMilli = Math.Max(
                1,
                Math.Min(
                    replacementMax,
                    DeterministicMath.MultiplyDivide(
                        replacementMax,
                        (int)Math.Min(
                            int.MaxValue,
                            oldHealth),
                        (int)Math.Min(
                            int.MaxValue,
                            oldMax))));
            enemy.Armor = replacement.Armor;
            enemy.BaseSpeedMilliPerTick =
                replacement.SpeedMilliPerTick;
            enemy.ControlThreshold =
                replacement.ControlGaugeThreshold;
            enemy.ControlThresholdStep =
                replacement.ControlGaugeStep;

            int weaknessBps = node.Amount > 0
                ? node.Amount
                : RareDefaultMutationWeaknessBps;
            int speedBonusBps = node.Amount2 > 0
                ? node.Amount2
                : RareDefaultMutationSpeedBonusBps;
            int weaknessKind = (int)(
                BuildRareMutationSeed(context, 0x5745414BUL) & 1UL);
            if (weaknessKind == 0)
            {
                enemy.AreaDamageTakenBps = Math.Min(
                    30000,
                    checked(
                        enemy.AreaDamageTakenBps +
                        weaknessBps));
            }
            else
            {
                enemy.SingleDamageTakenBps = Math.Min(
                    30000,
                    checked(
                        enemy.SingleDamageTakenBps +
                        weaknessBps));
            }
            enemy.SpeedMultiplierBps = Math.Min(
                30000,
                checked(
                    enemy.SpeedMultiplierBps +
                    speedBonusBps));

            rareEnemyMutations[enemy.Id.Value] =
                new RareEnemyMutationRuntime
                {
                    OriginalDefinitionId = original.Id,
                    ReplacementDefinitionId = replacement.Id,
                    WeaknessKind = weaknessKind,
                    WeaknessBps = weaknessBps,
                    SpeedBonusBps = speedBonusBps
                };
            AddPresentation(
                PresentationEventType.EffectTriggered,
                enemy.Id.Value,
                context.TowerId.Value,
                replacement.Id.Value,
                "mutation_enemy");
        }

        private void UpdateRareProjectileResonance(
            ProjectileState projectile)
        {
            if (!rareProjectileResonances.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileResonanceRuntime runtime))
            {
                return;
            }

            ulong radiusSquared =
                SquareRareRadius(runtime.RadiusMilli);
            int allyCount = 0;
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState candidate = projectiles[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.ExpirationQueued ||
                    candidate.Id == projectile.Id ||
                    projectile.Position.DistanceSquaredRaw(
                        candidate.Position) > radiusSquared)
                {
                    continue;
                }

                string candidateTag =
                    rareProjectileResonances.TryGetValue(
                        candidate.Id.Value,
                        out RareProjectileResonanceRuntime candidateRuntime)
                        ? candidateRuntime.Tag
                        : ResolveRareProjectileTag(
                            candidate,
                            string.Empty);
                if (string.Equals(
                        runtime.Tag,
                        candidateTag,
                        StringComparison.Ordinal))
                {
                    allyCount++;
                }
            }

            int nextBonus = Math.Min(
                runtime.MaximumBonusBps,
                checked(allyCount * runtime.BonusPerAllyBps));
            if (nextBonus == runtime.AppliedBonusBps)
            {
                return;
            }

            int oldFactor = checked(
                10000 + runtime.AppliedBonusBps);
            int newFactor = checked(10000 + nextBonus);
            projectile.DamageMilli = Math.Max(
                1,
                DeterministicMath.MultiplyDivide(
                    projectile.DamageMilli,
                    newFactor,
                    oldFactor));
            runtime.AppliedBonusBps = nextBonus;
            AddPresentation(
                PresentationEventType.EffectTriggered,
                projectile.Id.Value,
                projectile.SourceTowerId.Value,
                nextBonus,
                "resonance_link");
        }

        private void ReleaseRareProjectileTimeStop(
            ProjectileState projectile,
            RareProjectileTimeStopRuntime runtime)
        {
            if (runtime.StoredCharges <= 0)
            {
                AddPresentation(
                    PresentationEventType.EffectTriggered,
                    projectile.Id.Value,
                    runtime.TowerId.Value,
                    0,
                    "time_stop_release");
                return;
            }

            CollectRareEnemiesInRadius(
                projectile.Position,
                runtime.RadiusMilli,
                runtime.TargetLimit,
                null);
            long perTargetDamage =
                DeterministicMath.MultiplyBasisPoints(
                    projectile.DamageMilli,
                    runtime.DamagePerChargeBps);
            perTargetDamage = SaturatingMultiplyPositive(
                perTargetDamage,
                runtime.StoredCharges);

            rareEventScratch.Clear();
            for (int i = 0; i < rareEnemyScratch.Count; i++)
            {
                if (TryCreateDamageEvent(
                        rareEnemyScratch[i].Id,
                        runtime.TowerId,
                        runtime.CardId,
                        projectile.Id,
                        perTargetDamage,
                        DamageKind.Explosion,
                        0,
                        runtime.RootChainId,
                        runtime.ActivationId,
                        runtime.ParentEventId,
                        runtime.Depth + 1,
                        EventTags.Area |
                        EventTags.DamageOverTime |
                        EventTags.Projectile,
                        out GameEvent damageEvent))
                {
                    rareEventScratch.Add(damageEvent);
                }
            }

            bool enqueued =
                rareEventScratch.Count > 0 &&
                TryEnqueueBatch(rareEventScratch);
            AddPresentation(
                PresentationEventType.EffectTriggered,
                projectile.Id.Value,
                runtime.TowerId.Value,
                enqueued ? runtime.StoredCharges : 0,
                "time_stop_release");
        }

        private ProjectileState SelectRareProjectileAbsorbTarget(
            ProjectileState absorber,
            int radiusMilli)
        {
            ulong radiusSquared =
                SquareRareRadius(radiusMilli);
            ProjectileState best = null;
            ulong bestDistance = ulong.MaxValue;
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState candidate = projectiles[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.ExpirationQueued ||
                    candidate.Id == absorber.Id)
                {
                    continue;
                }

                ulong distance =
                    absorber.Position.DistanceSquaredRaw(
                        candidate.Position);
                if (distance > radiusSquared)
                {
                    continue;
                }
                if (best == null ||
                    distance < bestDistance ||
                    (distance == bestDistance &&
                     candidate.Id.Value < best.Id.Value))
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private EnemyState SelectRareEnemyAbsorbTarget(
            EnemyState absorber,
            int radiusMilli)
        {
            ulong radiusSquared =
                SquareRareRadius(radiusMilli);
            EnemyState best = null;
            ulong bestDistance = ulong.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.DeathQueued ||
                    candidate.Id == absorber.Id ||
                    candidate.HealthMilli >= absorber.HealthMilli ||
                    content.GetEnemy(candidate.DefinitionId).Rank !=
                    EnemyRank.Normal)
                {
                    continue;
                }

                ulong distance =
                    absorber.Position.DistanceSquaredRaw(
                        candidate.Position);
                if (distance > radiusSquared)
                {
                    continue;
                }
                if (best == null ||
                    distance < bestDistance ||
                    (distance == bestDistance &&
                     candidate.Id.Value < best.Id.Value))
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void CopyOneRareProjectileEffect(
            ProjectileState source,
            ProjectileState target)
        {
            EffectBinding selected = null;
            for (int i = 0; i < source.Bindings.Count; i++)
            {
                EffectBinding candidate = source.Bindings[i];
                if (selected == null ||
                    CompareRareBindings(
                        candidate,
                        selected) < 0)
                {
                    selected = candidate;
                }
            }

            if (selected != null)
            {
                EffectBinding clone = selected.Clone();
                clone.Used = false;
                clone.TriggerCount = 0;
                clone.TrailStarted = false;
                clone.TrailStartPosition = target.Position;
                clone.ActiveTrailHazardId = -1;
                target.Bindings.Add(clone);
                return;
            }

            // 바인딩이 없는 물리 카드 탄환도 최소 한 가지 특성을 가져올 수 있게 한다.
            if (source.Homing)
            {
                target.Homing = true;
            }
            else if (source.PierceRemaining > 0)
            {
                target.PierceRemaining = Math.Min(
                    content.Safety.MaxPiercesPerProjectile,
                    target.PierceRemaining + 1);
            }
            target.VisualFlags |= source.VisualFlags;
        }

        private void CopyRareEnemyStatuses(
            EnemyState source,
            EnemyState target,
            int limit)
        {
            rareStatusScratch.Clear();
            for (int i = 0; i < source.Statuses.Count; i++)
            {
                if (source.Statuses[i].RemainingTicks > 0)
                {
                    rareStatusScratch.Add(source.Statuses[i]);
                }
            }
            rareStatusScratch.Sort(CompareRareStatuses);

            int available = Math.Max(
                0,
                RareMaximumCopiedStatuses -
                target.Statuses.Count);
            int copyCount = Math.Min(
                Math.Min(limit, available),
                rareStatusScratch.Count);
            for (int i = 0; i < copyCount; i++)
            {
                StatusInstance status = rareStatusScratch[i];
                target.Statuses.Add(new StatusInstance
                {
                    InstanceId = nextStatusInstanceId++,
                    Type = status.Type,
                    SourceEntityId = status.SourceEntityId,
                    SourceTowerId = status.SourceTowerId,
                    SourceCardId = status.SourceCardId,
                    SourceCardInstanceId = status.SourceCardInstanceId,
                    Stacks = status.Stacks,
                    Intensity = status.Intensity,
                    RemainingTicks = status.RemainingTicks,
                    MaxStacks = status.MaxStacks,
                    TickInterval = status.TickInterval,
                    NextTick = status.NextTick,
                    Inherited = true,
                    Dispellable = status.Dispellable,
                    Limit = status.Limit,
                    RadiusMilli = status.RadiusMilli,
                    ArmorIgnoreBps = status.ArmorIgnoreBps
                });
            }
        }

        private void ConsumeRareAbsorbedEnemy(
            EnemyState enemy)
        {
            enemy.Alive = false;
            if (lineages.TryGetValue(
                    enemy.LineageId.Value,
                    out LineageState lineage))
            {
                lineage.ForfeitedReward = checked(
                    lineage.ForfeitedReward +
                    Math.Max(0, enemy.RewardBudget));
                lineage.ConsumedProgress = checked(
                    lineage.ConsumedProgress +
                    Math.Max(0, enemy.WaveProgressBudget));
                lineage.ForfeitedCardPackProgress = checked(
                    lineage.ForfeitedCardPackProgress +
                    Math.Max(
                        0,
                        enemy.CardPackProgressBudget));
                lineage.LastResolvedPosition = enemy.Position;
                if (lineage.IsShimmering)
                {
                    lineage.ShimmeringFailed = true;
                }
            }
            enemy.RewardBudget = 0;
            enemy.WaveProgressBudget = 0;
            enemy.CardPackProgressBudget = 0;
            DecrementLineage(enemy);
        }

        private int CountRareResonantEnemies(
            EnemyState subject,
            RareEnemyResonanceRuntime runtime,
            StatusType? requiredStatus)
        {
            ulong radiusSquared =
                SquareRareRadius(runtime.RadiusMilli);
            int count = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.DeathQueued ||
                    candidate.Id == subject.Id ||
                    subject.Position.DistanceSquaredRaw(
                        candidate.Position) > radiusSquared)
                {
                    continue;
                }

                bool linked = requiredStatus.HasValue
                    ? HasRareActiveStatus(
                        candidate,
                        requiredStatus.Value)
                    : SharesRareDebuff(
                        subject,
                        candidate,
                        runtime.StatusFilter);
                if (linked)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool SharesRareDebuff(
            EnemyState first,
            EnemyState second,
            string statusFilter)
        {
            for (int i = 0; i < first.Statuses.Count; i++)
            {
                StatusInstance firstStatus =
                    first.Statuses[i];
                if (firstStatus.RemainingTicks <= 0 ||
                    IsRareBeneficialStatus(
                        firstStatus.Type) ||
                    !MatchesRareStatusFilter(
                        statusFilter,
                        firstStatus.Type))
                {
                    continue;
                }
                if (HasRareActiveStatus(
                        second,
                        firstStatus.Type))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasRareActiveStatus(
            EnemyState enemy,
            StatusType statusType)
        {
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                if (enemy.Statuses[i].Type == statusType &&
                    enemy.Statuses[i].RemainingTicks > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsRareBeneficialStatus(
            StatusType statusType)
        {
            return statusType == StatusType.FearHaste ||
                   statusType == StatusType.FreezeImmunity;
        }

        private static bool MatchesRareStatusFilter(
            string filter,
            StatusType statusType)
        {
            return string.IsNullOrEmpty(filter) ||
                   string.Equals(
                       filter,
                       statusType.ToString(),
                       StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveRareProjectileTag(
            ProjectileState projectile,
            string explicitTag)
        {
            if (!string.IsNullOrEmpty(explicitTag))
            {
                return explicitTag;
            }

            TowerState tower =
                FindTower(projectile.SourceTowerId);
            string selected = string.Empty;
            if (tower != null)
            {
                for (int cardIndex = 0;
                     cardIndex < tower.Program.Length;
                     cardIndex++)
                {
                    string[] tags =
                        content.GetCard(
                            tower.Program[cardIndex])
                            .TagsInternal;
                    for (int tagIndex = 0;
                         tagIndex < tags.Length;
                         tagIndex++)
                    {
                        if (string.IsNullOrEmpty(selected) ||
                            string.CompareOrdinal(
                                tags[tagIndex],
                                selected) < 0)
                        {
                            selected = tags[tagIndex];
                        }
                    }
                }
            }
            return selected;
        }

        private int SelectRareMutationProgramIndex(
            TowerState tower,
            in EffectExecutionContext context,
            SubjectType subjectType)
        {
            rareProgramIndexScratch.Clear();
            int currentIndex = -1;
            for (int i = 0; i < tower.Program.Length; i++)
            {
                if (tower.ProgramInstances[i] ==
                    context.CardInstanceId)
                {
                    currentIndex = i;
                    break;
                }
            }

            // 현재 카드 오른쪽을 우선해 같은 실행에서 실제로 변이를 확인할 수 있게 한다.
            for (int pass = 0; pass < 2; pass++)
            {
                rareProgramIndexScratch.Clear();
                for (int i = 0; i < tower.Program.Length; i++)
                {
                    SubjectType configured =
                        i < tower.ProgramSubjectTypes.Length
                            ? tower.ProgramSubjectTypes[i]
                            : tower.SubjectType;
                    if (configured != subjectType ||
                        tower.ProgramInstances[i] ==
                        context.CardInstanceId ||
                        (pass == 0 && i <= currentIndex))
                    {
                        continue;
                    }
                    rareProgramIndexScratch.Add(i);
                }
                if (rareProgramIndexScratch.Count > 0)
                {
                    break;
                }
            }
            if (rareProgramIndexScratch.Count == 0)
            {
                return -1;
            }

            rareProgramIndexScratch.Sort(
                (left, right) =>
                {
                    int cardCompare = string.CompareOrdinal(
                        content.GetCard(tower.Program[left]).StableId,
                        content.GetCard(tower.Program[right]).StableId);
                    return cardCompare != 0
                        ? cardCompare
                        : tower.ProgramInstances[left].CompareTo(
                            tower.ProgramInstances[right]);
                });
            ulong seed = BuildRareMutationSeed(
                context,
                0x50524F4752414DUL);
            return rareProgramIndexScratch[
                (int)(seed %
                (ulong)rareProgramIndexScratch.Count)];
        }

        private CardId SelectRareMutationReplacementCard(
            CardId originalCardId,
            SubjectType subjectType,
            in EffectExecutionContext context,
            string explicitStableId)
        {
            CompiledCardDefinition original =
                content.GetCard(originalCardId);
            rareCardScratch.Clear();
            for (int i = 0; i < content.CardCount; i++)
            {
                CompiledCardDefinition candidate =
                    content.GetCard(new CardId(i));
                if (candidate.Id == originalCardId ||
                    candidate.Tier != original.Tier ||
                    IsRareMutationCard(candidate))
                {
                    continue;
                }
                CompiledEffectNode[] effects =
                    subjectType == SubjectType.Projectile
                        ? candidate.ProjectileEffectsInternal
                        : candidate.EnemyEffectsInternal;
                if (effects.Length > 0)
                {
                    rareCardScratch.Add(candidate);
                }
            }
            rareCardScratch.Sort(
                (left, right) =>
                    string.CompareOrdinal(
                        left.StableId,
                        right.StableId));
            if (rareCardScratch.Count == 0)
            {
                return CardId.Invalid;
            }

            if (!string.IsNullOrEmpty(explicitStableId))
            {
                for (int i = 0; i < rareCardScratch.Count; i++)
                {
                    if (string.Equals(
                            rareCardScratch[i].StableId,
                            explicitStableId,
                            StringComparison.Ordinal))
                    {
                        return rareCardScratch[i].Id;
                    }
                }
            }

            ulong seed = BuildRareMutationSeed(
                context,
                unchecked(
                    (ulong)(uint)originalCardId.Value) ^
                0x43415244UL);
            return rareCardScratch[
                (int)(seed % (ulong)rareCardScratch.Count)].Id;
        }

        private static bool IsRareMutationCard(
            CompiledCardDefinition card)
        {
            CompiledEffectNode[] projectileEffects =
                card.ProjectileEffectsInternal;
            for (int i = 0;
                 i < projectileEffects.Length;
                 i++)
            {
                if (projectileEffects[i].Operation ==
                    EffectOperation.ConfigureProjectileMutation)
                {
                    return true;
                }
            }
            CompiledEffectNode[] enemyEffects =
                card.EnemyEffectsInternal;
            for (int i = 0; i < enemyEffects.Length; i++)
            {
                if (enemyEffects[i].Operation ==
                    EffectOperation.ApplyEnemyMutation)
                {
                    return true;
                }
            }
            return false;
        }

        private CompiledEnemyDefinition
            SelectRareMutationEnemyDefinition(
                CompiledEnemyDefinition original,
                in EffectExecutionContext context,
                string explicitStableId)
        {
            rareDefinitionScratch.Clear();
            for (int i = 0; i < content.EnemyCount; i++)
            {
                CompiledEnemyDefinition candidate =
                    content.GetEnemy(
                        new EnemyDefinitionId(i));
                if (candidate.Id != original.Id &&
                    candidate.Rank == original.Rank)
                {
                    rareDefinitionScratch.Add(candidate);
                }
            }
            rareDefinitionScratch.Sort(
                (left, right) =>
                    string.CompareOrdinal(
                        left.StableId,
                        right.StableId));
            if (rareDefinitionScratch.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(explicitStableId))
            {
                for (int i = 0;
                     i < rareDefinitionScratch.Count;
                     i++)
                {
                    if (string.Equals(
                            rareDefinitionScratch[i].StableId,
                            explicitStableId,
                            StringComparison.Ordinal))
                    {
                        return rareDefinitionScratch[i];
                    }
                }
            }
            ulong seed = BuildRareMutationSeed(
                context,
                0x454E454D59UL);
            return rareDefinitionScratch[
                (int)(seed %
                (ulong)rareDefinitionScratch.Count)];
        }

        private ulong BuildRareMutationSeed(
            in EffectExecutionContext context,
            ulong salt)
        {
            StableHashBuilder hash =
                new StableHashBuilder(
                    combatRandom.State ^ salt);
            hash.Add(content.ContentHash);
            hash.Add(tick);
            hash.Add(context.SubjectId);
            hash.Add(context.TowerId);
            hash.Add(context.CardId);
            hash.Add(context.CardInstanceId);
            hash.Add(context.RootChainId);
            hash.Add(context.ActivationId);
            return hash.Finish();
        }

        private void CollectRareEnemiesInRadius(
            SimPosition origin,
            int radiusMilli,
            int limit,
            EnemyState excluded)
        {
            rareEnemyScratch.Clear();
            ulong radiusSquared =
                SquareRareRadius(radiusMilli);
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.DeathQueued ||
                    candidate == excluded ||
                    origin.DistanceSquaredRaw(
                        candidate.Position) >
                    radiusSquared)
                {
                    continue;
                }
                rareEnemyScratch.Add(candidate);
            }
            rareEnemyScratch.Sort(
                (left, right) =>
                {
                    ulong leftDistance =
                        origin.DistanceSquaredRaw(left.Position);
                    ulong rightDistance =
                        origin.DistanceSquaredRaw(right.Position);
                    int distanceCompare =
                        leftDistance.CompareTo(rightDistance);
                    return distanceCompare != 0
                        ? distanceCompare
                        : left.Id.Value.CompareTo(right.Id.Value);
                });
            if (rareEnemyScratch.Count > limit)
            {
                rareEnemyScratch.RemoveRange(
                    limit,
                    rareEnemyScratch.Count - limit);
            }
        }

        private static int CompareRareBindings(
            EffectBinding left,
            EffectBinding right)
        {
            int card = left.CardId.Value.CompareTo(
                right.CardId.Value);
            if (card != 0)
            {
                return card;
            }
            int instance = left.CardInstanceId.CompareTo(
                right.CardInstanceId);
            if (instance != 0)
            {
                return instance;
            }
            int trigger = ((int)left.Trigger).CompareTo(
                (int)right.Trigger);
            return trigger != 0
                ? trigger
                : ((int)left.Kind).CompareTo((int)right.Kind);
        }

        private static int CompareRareStatuses(
            StatusInstance left,
            StatusInstance right)
        {
            int type = ((int)left.Type).CompareTo(
                (int)right.Type);
            if (type != 0)
            {
                return type;
            }
            int tower = left.SourceTowerId.Value.CompareTo(
                right.SourceTowerId.Value);
            if (tower != 0)
            {
                return tower;
            }
            int card = left.SourceCardId.Value.CompareTo(
                right.SourceCardId.Value);
            return card != 0
                ? card
                : left.InstanceId.CompareTo(right.InstanceId);
        }

        private static ulong SquareRareRadius(int radiusMilli)
        {
            ulong radius = (ulong)Math.Max(1, radiusMilli);
            return radius > uint.MaxValue
                ? ulong.MaxValue
                : radius * radius;
        }

        private static long SaturatingMultiplyPositive(
            long value,
            int multiplier)
        {
            if (value <= 0 || multiplier <= 0)
            {
                return 0;
            }
            return value > long.MaxValue / multiplier
                ? long.MaxValue
                : value * multiplier;
        }

        private static long MakeRareMutationKey(
            int subjectId,
            int cardInstanceId)
        {
            return ((long)subjectId << 32) |
                   (uint)cardInstanceId;
        }

        private static void RemoveRareProjectileKeys<T>(
            Dictionary<int, T> dictionary,
            List<int> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                dictionary.Remove(keys[i]);
            }
        }

        private void AppendRareProjectileResonanceHash(
            ref StableHashBuilder hash)
        {
            CopySortedRareKeys(
                rareProjectileResonances,
                rareIntKeyScratch);
            hash.Add(rareIntKeyScratch.Count);
            for (int i = 0; i < rareIntKeyScratch.Count; i++)
            {
                int key = rareIntKeyScratch[i];
                RareProjectileResonanceRuntime runtime =
                    rareProjectileResonances[key];
                hash.Add(key);
                hash.Add(runtime.Tag);
                hash.Add(runtime.RadiusMilli);
                hash.Add(runtime.BonusPerAllyBps);
                hash.Add(runtime.MaximumBonusBps);
                hash.Add(runtime.AppliedBonusBps);
            }
        }

        private void AppendRareEnemyResonanceHash(
            ref StableHashBuilder hash)
        {
            CopySortedRareKeys(
                rareEnemyResonances,
                rareIntKeyScratch);
            hash.Add(rareIntKeyScratch.Count);
            for (int i = 0; i < rareIntKeyScratch.Count; i++)
            {
                int key = rareIntKeyScratch[i];
                RareEnemyResonanceRuntime runtime =
                    rareEnemyResonances[key];
                hash.Add(key);
                hash.Add(runtime.TowerId);
                hash.Add(runtime.CardId);
                hash.Add(runtime.CardInstanceId);
                hash.Add(runtime.StatusFilter);
                hash.Add(runtime.RadiusMilli);
                hash.Add(runtime.BonusPerLinkBps);
                hash.Add(runtime.MaximumBonusBps);
            }
        }

        private void AppendRareProjectileTimeStopHash(
            ref StableHashBuilder hash)
        {
            CopySortedRareKeys(
                rareProjectileTimeStops,
                rareIntKeyScratch);
            hash.Add(rareIntKeyScratch.Count);
            for (int i = 0; i < rareIntKeyScratch.Count; i++)
            {
                int key = rareIntKeyScratch[i];
                RareProjectileTimeStopRuntime runtime =
                    rareProjectileTimeStops[key];
                hash.Add(key);
                hash.Add(runtime.TowerId);
                hash.Add(runtime.CardId);
                hash.Add(runtime.CardInstanceId);
                hash.Add(runtime.SourceEntityId);
                hash.Add(runtime.RootChainId);
                hash.Add(runtime.ActivationId);
                hash.Add(runtime.ParentEventId);
                hash.Add(runtime.Depth);
                hash.Add(runtime.ReleaseTick);
                hash.Add(runtime.NextStoreTick);
                hash.Add(runtime.StoreIntervalTicks);
                hash.Add(runtime.StoredCharges);
                hash.Add(runtime.MaximumCharges);
                hash.Add(runtime.DamagePerChargeBps);
                hash.Add(runtime.RadiusMilli);
                hash.Add(runtime.TargetLimit);
            }
        }

        private void AppendRareEnemyTimeStopHash(
            ref StableHashBuilder hash)
        {
            CopySortedRareKeys(
                rareEnemyTimeStops,
                rareIntKeyScratch);
            hash.Add(rareIntKeyScratch.Count);
            for (int i = 0; i < rareIntKeyScratch.Count; i++)
            {
                int key = rareIntKeyScratch[i];
                RareEnemyTimeStopRuntime runtime =
                    rareEnemyTimeStops[key];
                hash.Add(key);
                hash.Add(runtime.TowerId);
                hash.Add(runtime.CardId);
                hash.Add(runtime.CardInstanceId);
                hash.Add(runtime.ReleaseTick);
            }
        }

        private void AppendRareProjectileMutationHash(
            ref StableHashBuilder hash)
        {
            rareLongKeyScratch.Clear();
            foreach (KeyValuePair<long, RareProjectileMutationRuntime> pair
                     in rareProjectileMutations)
            {
                rareLongKeyScratch.Add(pair.Key);
            }
            rareLongKeyScratch.Sort();
            hash.Add(rareLongKeyScratch.Count);
            for (int i = 0; i < rareLongKeyScratch.Count; i++)
            {
                long key = rareLongKeyScratch[i];
                RareProjectileMutationRuntime runtime =
                    rareProjectileMutations[key];
                hash.Add(key);
                hash.Add(runtime.SubjectId);
                hash.Add(runtime.TargetCardInstanceId);
                hash.Add(runtime.OriginalCardId);
                hash.Add(runtime.ReplacementCardId);
                hash.Add(runtime.RemainingUses);
                hash.Add(runtime.ExpireTick);
            }
        }

        private void AppendRareEnemyMutationHash(
            ref StableHashBuilder hash)
        {
            CopySortedRareKeys(
                rareEnemyMutations,
                rareIntKeyScratch);
            hash.Add(rareIntKeyScratch.Count);
            for (int i = 0; i < rareIntKeyScratch.Count; i++)
            {
                int key = rareIntKeyScratch[i];
                RareEnemyMutationRuntime runtime =
                    rareEnemyMutations[key];
                hash.Add(key);
                hash.Add(runtime.OriginalDefinitionId);
                hash.Add(runtime.ReplacementDefinitionId);
                hash.Add(runtime.WeaknessKind);
                hash.Add(runtime.WeaknessBps);
                hash.Add(runtime.SpeedBonusBps);
            }
        }

        private static void CopySortedRareKeys<T>(
            Dictionary<int, T> dictionary,
            List<int> destination)
        {
            destination.Clear();
            foreach (KeyValuePair<int, T> pair in dictionary)
            {
                destination.Add(pair.Key);
            }
            destination.Sort();
        }
    }
}
