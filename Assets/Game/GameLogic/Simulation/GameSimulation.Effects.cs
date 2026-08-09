using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    // 이 파일은 카드가 요청하는 실제 효과를 논리 상태에 반영한다.
    // 단순 수치 변경은 즉시 적용하지만, 피해·사망·보상처럼 다른 효과를 연쇄시킬 수 있는 결과는
    // EventQueue에 넣는다. 이 구분 덕분에 카드 조합이 강해져도 C# 호출 스택으로 즉시 재귀하지 않는다.
    public sealed partial class GameSimulation
    {
        /// <summary>
        /// 투사체 분열 카드의 핵심 규칙을 적용하고 새 가지의 EntityId를 반환한다.
        /// 원본 투사체를 첫 번째 가지로 유지하고 새 투사체 하나를 추가하며, 두 가지 모두
        /// 분열 카드의 오른쪽에 남은 카드부터 계속 실행할 수 있도록 필요한 예산을 먼저 확보한다.
        /// </summary>
        internal EntityId SplitProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState original = FindProjectile(context.SubjectId);
            if (original == null ||
                !original.Alive ||
                node.Amount < 2 ||
                original.Generation >=
                    content.Safety.MaxProjectileCloneGeneration ||
                projectiles.Count >=
                    content.Safety.MaxActiveProjectiles)
            {
                return EntityId.Invalid;
            }

            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.ProjectileSpawned,
                    context.RootChainId,
                    context.TowerId,
                    context.CardId,
                    context.SubjectId,
                    SubjectType.Projectile),
                context.Depth);
            int continuationCount = context.ContinuationCardCount;
            // 원본 쪽 continuation은 상위 실행이 미리 예약했을 수도 있다. 부족한 원본 몫과
            // 새 자식 몫을 합쳐 한 번에 예약해야 한쪽 가지만 다음 카드를 실행하는 반쪽 분열이 생기지 않는다.
            int missingOriginalContinuations = Math.Max(
                0,
                continuationCount -
                context.ReservedContinuationEvents);
            int newlyReservedContinuations = checked(
                missingOriginalContinuations + continuationCount);
            if (!TryReserveComposite(
                    in diagnosticEvent,
                    chainEventCount: newlyReservedContinuations,
                    queueSlotCount:
                        continuationCount > 0 ? 2 : 0,
                    projectileSpawnCount: 1,
                    cardTriggerCount: newlyReservedContinuations))
            {
                return EntityId.Invalid;
            }

            // 분열 피해 배율은 원본에도 먼저 적용하고, 자식은 그 시점의 물리 수치를 복사한다.
            original.DamageMilli = DeterministicMath.MultiplyBasisPoints(
                original.DamageMilli,
                node.Amount2);
            original.Generation++;
            var child = new ProjectileState
            {
                Id = new EntityId(nextEntityId++),
                SourceTowerId = original.SourceTowerId,
                Generation = original.Generation,
                Position = original.Position,
                TargetId = original.TargetId,
                ApplyEnemyProgramOnHit =
                    original.ApplyEnemyProgramOnHit,
                DirectionXBps = original.DirectionXBps,
                DirectionYBps = original.DirectionYBps,
                Homing = original.Homing,
                VisualFlags = original.VisualFlags,
                DamageMilli = original.DamageMilli,
                SpeedMilliPerTick = original.SpeedMilliPerTick,
                RadiusMilli = original.RadiusMilli,
                LifetimeRemaining = original.LifetimeRemaining,
                PierceRemaining = original.PierceRemaining,
                PiercesUsed = original.PiercesUsed,
                PierceDamageMultiplierBps = original.PierceDamageMultiplierBps,
                CriticalChanceBps = original.CriticalChanceBps,
                RootChainId = original.RootChainId,
                ActivationId = original.ActivationId,
                LastTrailPosition = original.Position
            };
            // 의도적으로 Bindings와 HitEnemies는 복사하지 않는다.
            // 따라서 분열보다 왼쪽에서 얻은 화상·폭발 바인딩은 원본에만 남고,
            // 새 가지는 현재 위치·피해·속도 같은 물리 수치만 이어받는다.
            projectiles.Add(child);
            InheritCommonProjectileRuntime(original, child);
            InheritRareResonanceAbsorbTimeMutationProjectileRuntime(
                original,
                child);
            InheritLegendaryProjectileState(
                original,
                child);
            EnemyState alternate = SelectProjectileTarget(child);
            if (alternate != null && alternate.Id != original.TargetId)
            {
                child.TargetId = alternate.Id;
                SetProjectileDirection(child, alternate.Position);
            }

            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                child.Id.Value,
                original.Id.Value,
                (int)Math.Min(int.MaxValue, child.DamageMilli),
                "split");
            return child.Id;
        }

        /// <summary>
        /// 적 분열 카드의 핵심 규칙을 적용한다.
        /// 두 결과가 모두 최소 체력을 유지하고 남은 카드 실행 예산을 확보한 경우에만 체력·보상·진행도를
        /// 나누고 자식을 만든다. 실패하면 원본 적도 전혀 변경하지 않는 원자적 처리다.
        /// </summary>
        internal EntityId SplitEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState original = FindEnemy(context.SubjectId);
            if (original == null || !original.Alive || node.Amount < 2)
            {
                return EntityId.Invalid;
            }

            // 체력 1은 milli 단위로 1,000이다. 반올림으로 1 미만인 가지를 억지로 살리지 않고,
            // 예산을 예약하기 전에 두 결과를 모두 검사해 실패 경로가 어떤 상태도 소비하지 않게 한다.
            const long MinimumSplitHealthMilli = 1000L;
            long newMax = DeterministicMath.MultiplyBasisPoints(
                original.MaxHealthMilli,
                node.Amount2);
            long newCurrent = DeterministicMath.MultiplyBasisPoints(
                original.HealthMilli,
                node.Amount2);
            if (newMax < MinimumSplitHealthMilli ||
                newCurrent < MinimumSplitHealthMilli)
            {
                return EntityId.Invalid;
            }

            LineageState lineage = lineages[original.LineageId.Value];
            int resultingGeneration = original.Generation + 1;
            if (resultingGeneration >
                    content.Safety.MaxEnemySplitGeneration ||
                enemies.Count >=
                    content.Safety.MaxActiveEnemies)
            {
                return EntityId.Invalid;
            }
            int splitSizeMultiplierBps = MultiplyBps(
                original.SizeMultiplierBps,
                9000);
            SimVector branchOriginOffset =
                original.PathLateralOffset;

            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.EnemySplit,
                    context.RootChainId,
                    context.TowerId,
                    context.CardId,
                    context.SubjectId,
                    SubjectType.Enemy),
                context.Depth);
            // 고정 횟수로 분열을 끊지 않는다. 매 분열마다 최대/현재 체력이 45%로
            // 줄어 1 미만에서 자연 종료하며, 이 개체 수 상한은 비정상 콘텐츠가
            // WebGL 메모리를 고갈시키는 것만 막는 최후의 보호선이다.
            if (lineage.SpawnedEntityCount >=
                    content.Safety.MaxEnemiesPerLineage)
            {
                AddDiagnostic(
                    DiagnosticCode.EnemyLineageLimitReached,
                    diagnosticEvent,
                    (int)BudgetFailure.EnemyLineageEntityLimit);
                return EntityId.Invalid;
            }
            if (!TryPassSandboxEnemyCreationGate(
                    1,
                    in diagnosticEvent))
            {
                return EntityId.Invalid;
            }

            int continuationCount = context.ContinuationCardCount;
            // 원본과 자식 모두 오른쪽 카드들을 끝까지 실행할 수 있을 때만 분열을 허용한다.
            int missingOriginalContinuations = Math.Max(
                0,
                continuationCount -
                context.ReservedContinuationEvents);
            int newlyReservedContinuations = checked(
                missingOriginalContinuations + continuationCount);
            if (!TryReserveComposite(
                    in diagnosticEvent,
                    chainEventCount: newlyReservedContinuations,
                    queueSlotCount:
                        continuationCount > 0 ? 2 : 0,
                    projectileSpawnCount: 0,
                    cardTriggerCount: newlyReservedContinuations,
                    enemySpawnCount: 1))
            {
                return EntityId.Invalid;
            }

            // 체력은 카드 비율로 각 가지에 다시 설정하지만, 보상과 웨이브 진행도는 기존 총량을 반으로 나눈다.
            // 정수 나눗셈의 나머지는 원본에 남아 전체 합계가 절대로 증가하지 않는다.
            int childReward = original.RewardBudget / 2;
            int childProgress = original.WaveProgressBudget / 2;
            int splitCardPackProgress = (int)
                DeterministicMath.MultiplyBasisPoints(
                    original.CardPackProgressBudget,
                    run.SplitCardPackProgressBps);
            original.RewardBudget -= childReward;
            original.WaveProgressBudget -= childProgress;
            original.CardPackProgressBudget =
                splitCardPackProgress;
            original.MaxHealthMilli = newMax;
            original.HealthMilli = Math.Min(newMax, newCurrent);
            original.ShieldMilli =
                DeterministicMath.MultiplyBasisPoints(
                    original.ShieldMilli,
                    node.Amount2);
            original.Generation++;
            original.SizeMultiplierBps = splitSizeMultiplierBps;

            // 현재 경로 진행 방향의 왼쪽 법선으로 원본을, 오른쪽 법선으로
            // 자식을 같은 거리만큼 옮긴다. 결과 적의 피격 반지름과 여백을
            // 함께 사용해 커진 몬스터도 분열 직후 서로 겹치지 않게 한다.
            path.GetDirectionBasisPoints(
                original.PathProgressMilli,
                out int pathDirectionX,
                out int pathDirectionY);
            int branchHalfSeparation = checked(
                GetEnemyHitRadiusMilli(original) +
                Math.Max(100, run.EnemyBaseHitRadiusMilli / 2));
            var leftBranchOffset = SimVector.FromMilliUnits(
                DeterministicMath.MultiplyDivide(
                    -pathDirectionY,
                    branchHalfSeparation,
                    DeterministicMath.BasisPointScale),
                DeterministicMath.MultiplyDivide(
                    pathDirectionX,
                    branchHalfSeparation,
                    DeterministicMath.BasisPointScale));
            original.PathLateralOffset =
                branchOriginOffset + leftBranchOffset;
            RefreshEnemyPosition(original);

            var child = new EnemyState
            {
                Id = new EntityId(nextEntityId++),
                DefinitionId = original.DefinitionId,
                LineageId = original.LineageId,
                Generation = original.Generation,
                SpawnOrigin = EnemySpawnOrigin.Split,
                SummonerId = original.SummonerId,
                EliteTraitIds =
                    (EliteTraitId[])original.EliteTraitIds.Clone(),
                PathProgressMilli = original.PathProgressMilli,
                PathLateralOffset =
                    branchOriginOffset - leftBranchOffset,
                Position =
                    path.GetPosition(original.PathProgressMilli) +
                    (branchOriginOffset - leftBranchOffset),
                HealthMilli = original.HealthMilli,
                MaxHealthMilli = original.MaxHealthMilli,
                Armor = original.Armor,
                BaseSpeedMilliPerTick = original.BaseSpeedMilliPerTick,
                SpeedMultiplierBps = original.SpeedMultiplierBps,
                SizeMultiplierBps = splitSizeMultiplierBps,
                EliteRenderScaleBps =
                    original.EliteRenderScaleBps,
                AreaDamageTakenBps = original.AreaDamageTakenBps,
                SingleDamageTakenBps = original.SingleDamageTakenBps,
                VisualFlags = original.VisualFlags,
                RewardBudget = childReward,
                WaveProgressBudget = childProgress,
                CardPackProgressBudget =
                    splitCardPackProgress,
                IsShimmering = original.IsShimmering,
                ShieldMilli = original.ShieldMilli,
                ControlThreshold = original.ControlThreshold,
                ControlThresholdStep = original.ControlThresholdStep,
                BossAbilityCooldownTicks =
                    original.BossAbilityCooldownTicks,
                BossCastRemainingTicks =
                    original.BossCastRemainingTicks,
                BossEnraged = original.BossEnraged,
                BossPhaseAnnounced =
                    original.BossPhaseAnnounced
            };
            // 분열 시점의 상태이상은 독립된 인스턴스로 완전히 복제한다. InstanceId만 새로 발급해
            // 이후 한 가지의 중첩·만료 변경이 다른 가지에 영향을 주지 않게 한다.
            for (int statusIndex = 0;
                 statusIndex < original.Statuses.Count;
                 statusIndex++)
            {
                StatusInstance status = original.Statuses[statusIndex];
                child.Statuses.Add(new StatusInstance
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
                    Inherited = status.Inherited,
                    Dispellable = status.Dispellable,
                    Limit = status.Limit,
                    RadiusMilli = status.RadiusMilli,
                    ArmorIgnoreBps = status.ArmorIgnoreBps
                });
            }

            // 사망 바인딩은 상태이상이 아니므로 복제하지 않는다. 분열 오른쪽에 있는 사망 카드는
            // continuation을 통해 원본과 자식에 각각 새 바인딩을 만든다.
            enemies.Add(child);
            // 사거리 진입형 타워의 내부/쿨다운 상태는 상속해, 범위 안에서 태어난 자식이
            // 새로 진입한 것처럼 즉시 같은 타워를 재발동시키는 우회를 막는다.
            InheritRangeEntryLocks(original, child);
            InheritLegendaryEnemyState(
                original,
                child);
            lineage.HighestGeneration = Math.Max(
                lineage.HighestGeneration,
                resultingGeneration);
            lineage.SplitCount++;
            lineage.SpawnedEntityCount++;
            lineage.LiveMembers++;
            spatialIndex.Rebuild(enemies);

            AddPresentation(
                PresentationEventType.EnemySpawned,
                child.Id.Value,
                original.Id.Value,
                child.RewardBudget,
                "split");
            return child.Id;
        }

        /// <summary>
        /// 투사체의 남은 관통 횟수와 관통 후 피해 배율을 변경한다.
        /// 카드 수치가 커도 전역 관통 안전 상한을 넘지 못한다.
        /// </summary>
        internal void AddProjectilePierce(
            EntityId projectileId,
            in CompiledEffectNode node)
        {
            ProjectileState projectile = FindProjectile(projectileId);
            if (projectile == null)
            {
                return;
            }

            projectile.PierceRemaining = Math.Min(
                content.Safety.MaxPiercesPerProjectile,
                projectile.PierceRemaining + Math.Max(0, node.Amount));
            if (node.Amount2 > 0)
            {
                projectile.PierceDamageMultiplierBps = node.Amount2;
            }
        }

        /// <summary>
        /// 지금 즉시 상태이상을 적용하는 대신, 투사체의 적중·첫 적중·소멸 시점에 실행할 효과를 부착한다.
        /// 이것이 같은 '화상' 카드가 투사체 문맥에서는 적중 시 화상으로 해석되는 방식이다.
        /// </summary>
        internal void AddProjectileBinding(
            in EffectExecutionContext context,
            BindingTrigger trigger,
            BindingKind kind,
            in CompiledEffectNode node)
        {
            ProjectileState projectile = FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            projectile.Bindings.Add(new EffectBinding
            {
                Trigger = trigger,
                Kind = kind,
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                Node = node,
                TrailStartPosition = projectile.Position
            });
        }

        /// <summary>
        /// 적이 죽는 순간 실행할 효과를 적에게 부착한다.
        /// 폭발 카드의 적 해석처럼 현재가 아니라 미래의 사망 사건에 반응하는 효과에 사용한다.
        /// </summary>
        internal void AddEnemyDeathBinding(
            in EffectExecutionContext context,
            BindingKind kind,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            enemy.DeathBindings.Add(new EffectBinding
            {
                Trigger = BindingTrigger.OnDeath,
                Kind = kind,
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                Node = node
            });
        }

        /// <summary>
        /// 둔화·거대화·축소 카드의 투사체 해석처럼 투사체 자체의 물리 수치를 바꾼다.
        /// 모든 비율은 10,000=100% 정수 값이고, 속도·반지름·수명에는 유효한 최소/최대값을 적용한다.
        /// </summary>
        internal void ModifyProjectile(
            EntityId projectileId,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            ProjectileState projectile = FindProjectile(projectileId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            switch (operation)
            {
                case EffectOperation.ModifyProjectileSlow:
                    projectile.SpeedMilliPerTick = Math.Max(
                        1,
                        (int)DeterministicMath.MultiplyBasisPoints(
                            projectile.SpeedMilliPerTick,
                            node.Amount));
                    projectile.LifetimeRemaining = Math.Min(
                        content.Safety.MaxProjectileLifetimeTicks,
                        (int)DeterministicMath.MultiplyBasisPoints(
                            projectile.LifetimeRemaining,
                            node.Amount2));
                    projectile.RadiusMilli = Math.Max(
                        1,
                        (int)DeterministicMath.MultiplyBasisPoints(
                            projectile.RadiusMilli,
                            node.Amount3));
                    break;
                case EffectOperation.EnlargeProjectile:
                    projectile.DamageMilli = DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        node.Amount2);
                    projectile.RadiusMilli = Math.Max(
                        1,
                        (int)DeterministicMath.MultiplyBasisPoints(
                            projectile.RadiusMilli,
                            node.Amount));
                    projectile.SpeedMilliPerTick = Math.Max(
                        1,
                        (int)DeterministicMath.MultiplyBasisPoints(
                            projectile.SpeedMilliPerTick,
                            node.Amount3));
                    break;
                case EffectOperation.ShrinkProjectile:
                    projectile.DamageMilli = DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        node.Amount);
                    projectile.RadiusMilli = Math.Max(
                        1,
                        (int)DeterministicMath.MultiplyBasisPoints(
                            projectile.RadiusMilli,
                            node.Amount2));
                    projectile.SpeedMilliPerTick = Math.Max(
                        1,
                        (int)DeterministicMath.MultiplyBasisPoints(
                            projectile.SpeedMilliPerTick,
                            node.Amount3));
                    projectile.CriticalChanceBps = Math.Min(
                        10000,
                        projectile.CriticalChanceBps + Math.Max(0, node.ChanceBps));
                    break;
            }
        }

        /// <summary>
        /// 적을 직접 대상으로 삼는 카드의 즉시 효과를 분기한다.
        /// 수치가 아닌 연쇄 결과를 만들 수 있는 밀치기는 전용 함수에서 충돌 이벤트까지 처리한다.
        /// </summary>
        internal void ApplyDirectEnemyEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            switch (operation)
            {
                case EffectOperation.ApplyKnockback:
                    ApplyKnockback(enemy, context, node);
                    break;
                case EffectOperation.IncreaseReward:
                    IncreaseEnemyReward(
                        enemy,
                        context.TowerId,
                        context.CardInstanceId,
                        RewardAugmentKind.GoldBounty,
                        node.Amount,
                        node.Limit);
                    break;
                case EffectOperation.EnlargeEnemy:
                    enemy.SizeMultiplierBps = MultiplyBps(
                        enemy.SizeMultiplierBps,
                        node.Amount);
                    enemy.SpeedMultiplierBps = MultiplyBps(
                        enemy.SpeedMultiplierBps,
                        node.Amount2);
                    enemy.AreaDamageTakenBps = MultiplyBps(
                        enemy.AreaDamageTakenBps,
                        node.Amount3);
                    IncreaseEnemyReward(
                        enemy,
                        context.TowerId,
                        context.CardInstanceId,
                        RewardAugmentKind.Enlarge,
                        node.ChanceBps,
                        10000);
                    break;
                case EffectOperation.ShrinkEnemy:
                    long previousMax = enemy.MaxHealthMilli;
                    enemy.MaxHealthMilli = Math.Max(
                        1,
                        DeterministicMath.MultiplyBasisPoints(previousMax, node.Amount));
                    enemy.HealthMilli = Math.Max(
                        1,
                        DeterministicMath.MultiplyBasisPoints(enemy.HealthMilli, node.Amount));
                    enemy.SizeMultiplierBps = MultiplyBps(
                        enemy.SizeMultiplierBps,
                        node.Amount2);
                    enemy.SpeedMultiplierBps = MultiplyBps(
                        enemy.SpeedMultiplierBps,
                        node.Amount3);
                    enemy.SingleDamageTakenBps = Math.Min(
                        30000,
                        enemy.SingleDamageTakenBps + node.ChanceBps);
                    break;
            }
        }

        /// <summary>
        /// 적 상태이상을 적용하는 공용 진입점이다.
        /// 일반 적의 기절은 그대로 상태가 되지만 정예·보스에게는 제어 저항 게이지로 변환된다.
        /// 강한 적을 완전 면역으로 만들지 않으면서도 기절 연타로 영구 봉쇄하는 것을 막는다.
        /// </summary>
        internal void ApplyStatus(
            in EffectExecutionContext context,
            StatusType statusType,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            if (statusType == StatusType.Stun)
            {
                if (UsesEliteControlRules(enemy))
                {
                    ApplyControlGauge(
                        enemy,
                        context,
                        Math.Max(0, node.Amount));
                    return;
                }
            }

            ApplyStatusCore(enemy, context, statusType, node);
        }

        /// <summary>
        /// 정예·보스의 제어 게이지를 누적하고 임계치 도달 시 짧은 기절로 바꾼다.
        /// 한 번 발동할 때마다 다음 임계치를 올려 반복 제어에 점차 더 많은 투자가 필요하게 한다.
        /// </summary>
        private void ApplyControlGauge(
            EnemyState enemy,
            in EffectExecutionContext context,
            int controlValue)
        {
            controlValue = ResolveLegendaryEnemyStatusValue(
                enemy,
                controlValue);
            enemy.ControlGauge = checked(
                enemy.ControlGauge + Math.Max(0, controlValue));
            if (enemy.ControlGauge < enemy.ControlThreshold)
            {
                return;
            }

            enemy.ControlGauge = 0;
            enemy.ControlThreshold = Math.Min(
                run.MaxControlGaugeThreshold,
                enemy.ControlThreshold + enemy.ControlThresholdStep);
            var interrupt = new CompiledEffectNode(
                EffectOperation.ApplyStun,
                controlValue,
                0,
                0,
                run.ControlInterruptTicks,
                0,
                1,
                0,
                0,
                0,
                null);
            ApplyStatusCore(
                enemy,
                context,
                StatusType.Stun,
                interrupt);
        }

        /// <summary>
        /// 상태 인스턴스를 생성하거나 기존 인스턴스에 중첩·지속시간 갱신을 적용한다.
        /// 같은 상태라도 (상태 종류, 원본 타워, 원본 카드)가 다르면 별도 인스턴스로 보존되어
        /// 어느 빌드가 피해와 효과를 만들었는지 추적할 수 있다.
        /// </summary>
        private void ApplyStatusCore(
            EnemyState enemy,
            in EffectExecutionContext context,
            StatusType statusType,
            in CompiledEffectNode node)
        {
            CompiledEffectNode effectiveNode =
                AdjustStatusNodeForCurse(
                    enemy,
                    statusType,
                    node);
            effectiveNode =
                AdjustStatusNodeForRareResonance(
                    enemy,
                    statusType,
                    effectiveNode);
            effectiveNode = new CompiledEffectNode(
                effectiveNode.Operation,
                ResolveLegendaryEnemyStatusValue(
                    enemy,
                    effectiveNode.Amount),
                effectiveNode.Amount2,
                effectiveNode.Amount3,
                ResolveLegendaryEnemyStatusValue(
                    enemy,
                    effectiveNode.DurationTicks),
                effectiveNode.IntervalTicks,
                effectiveNode.MaxStacks,
                effectiveNode.RadiusMilli,
                effectiveNode.Limit,
                effectiveNode.ChanceBps,
                effectiveNode.ReferenceId);
            StatusInstance existing = null;
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance candidate = enemy.Statuses[i];
                if (candidate.Type == statusType &&
                    candidate.SourceTowerId == context.TowerId &&
                    candidate.SourceCardId == context.CardId)
                {
                    existing = candidate;
                    break;
                }
            }

            int maxStacks = Math.Max(
                1,
                effectiveNode.MaxStacks);
            if (existing == null)
            {
                // +1틱은 같은 시뮬레이션 틱 후반의 상태 처리에서 즉시 1이 감소하는 것을 보정한다.
                // 콘텐츠에 적힌 DurationTicks만큼 실제로 유효한 시간을 보장하기 위한 내부 표현이다.
                existing = new StatusInstance
                {
                    InstanceId = nextStatusInstanceId++,
                    Type = statusType,
                    SourceEntityId = context.SourceEntityId,
                    SourceTowerId = context.TowerId,
                    SourceCardId = context.CardId,
                    SourceCardInstanceId = context.CardInstanceId,
                    Stacks = 1,
                    Intensity = Math.Max(
                        0,
                        effectiveNode.Amount),
                    RemainingTicks = checked(
                        Math.Max(
                            1,
                            effectiveNode.DurationTicks) + 1),
                    MaxStacks = maxStacks,
                    TickInterval = Math.Max(
                        0,
                        effectiveNode.IntervalTicks),
                    NextTick = tick + Math.Max(
                        1,
                        effectiveNode.IntervalTicks),
                    Dispellable = true,
                    Limit = effectiveNode.Limit,
                    RadiusMilli =
                        effectiveNode.RadiusMilli,
                    ArmorIgnoreBps = statusType == StatusType.Poison
                        ? effectiveNode.ChanceBps
                        : statusType == StatusType.Pierced
                            ? effectiveNode.Amount
                            : 0
                };
                enemy.Statuses.Add(existing);
            }
            else
            {
                // 중첩 수는 상한까지 증가하고, 세기는 더 강한 값, 지속시간은 더 긴 값을 유지한다.
                // 약한 재적용이 강한 기존 상태를 덮어써서 약화시키지 않는다.
                existing.Stacks = Math.Min(existing.MaxStacks, existing.Stacks + 1);
                existing.Intensity = Math.Max(
                    existing.Intensity,
                    effectiveNode.Amount);
                existing.SourceEntityId = context.SourceEntityId;
                existing.RemainingTicks = Math.Max(
                    existing.RemainingTicks,
                    checked(
                        Math.Max(
                            1,
                            effectiveNode.DurationTicks) + 1));
                if (statusType == StatusType.Poison)
                {
                    existing.ArmorIgnoreBps = Math.Max(
                        existing.ArmorIgnoreBps,
                        effectiveNode.ChanceBps);
                }
                else if (statusType == StatusType.Pierced)
                {
                    existing.ArmorIgnoreBps = Math.Max(
                        existing.ArmorIgnoreBps,
                        effectiveNode.Amount);
                }
            }

            AddPresentation(
                PresentationEventType.StatusApplied,
                enemy.Id.Value,
                context.TowerId.Value,
                existing.Stacks,
                statusType.ToString());
        }

        /// <summary>
        /// 매 틱 모든 상태의 남은 시간, 주기 피해, 만료를 중앙에서 처리한다.
        /// 화상·중독 틱은 새 RootChain의 피해 이벤트로 보내므로 상태 순회 도중 사망 처리가 재귀하지 않는다.
        /// </summary>
        private void ProcessStatuses()
        {
            for (int enemyIndex = 0; enemyIndex < enemies.Count; enemyIndex++)
            {
                EnemyState enemy = enemies[enemyIndex];
                if (!enemy.Alive)
                {
                    continue;
                }

                int statusIndex = 0;
                while (statusIndex < enemy.Statuses.Count)
                {
                    StatusInstance status = enemy.Statuses[statusIndex];
                    status.RemainingTicks--;
                    if (status.TickInterval > 0 && tick >= status.NextTick)
                    {
                        // 다음 틱 시간을 누적 방식으로 갱신해 프레임 속도나 호출 시점에 따른 오차를 만들지 않는다.
                        status.NextTick += status.TickInterval;
                        if (status.Type == StatusType.Burn || status.Type == StatusType.Poison)
                        {
                            ChainId chainId = CreateRootChain();
                            ActivationId activationId = CreateActivation();
                            DamageKind kind = status.Type == StatusType.Burn
                                ? DamageKind.Fire
                                : DamageKind.Poison;
                            int armorIgnore = status.Type == StatusType.Poison
                                ? status.ArmorIgnoreBps
                                : 0;
                            EnqueueDamage(
                                enemy.Id,
                                status.SourceTowerId,
                                status.SourceCardId,
                                status.SourceEntityId,
                                (long)status.Intensity * status.Stacks,
                                kind,
                                armorIgnore,
                                chainId,
                                activationId,
                                EventId.Invalid,
                                0,
                                EventTags.DamageOverTime);

                        }
                        else
                        {
                            ProcessUncommonStatusTick(
                                enemy,
                                status);
                        }
                    }

                    if (status.RemainingTicks <= 0)
                    {
                        HandleUncommonStatusExpired(
                            enemy,
                            status);
                        // 목록을 순회하면서 제거하므로 이 경우에는 인덱스를 증가시키지 않는다.
                        AddPresentation(
                            PresentationEventType.StatusRemoved,
                            enemy.Id.Value,
                            status.SourceTowerId.Value,
                            0,
                            status.Type.ToString());
                        enemy.Statuses.RemoveAt(statusIndex);
                    }
                    else
                    {
                        statusIndex++;
                    }
                }
            }
        }

        /// <summary>
        /// 적중한 투사체에 저장되어 있던 바인딩 하나를 실제 적 대상 효과로 해석한다.
        /// 새 실행 문맥에는 원본 타워·카드·체인·부모 이벤트를 모두 전달해 진단과 인과관계를 보존한다.
        /// </summary>
        private void ExecuteProjectileBinding(
            ProjectileState projectile,
            EnemyState target,
            EffectBinding binding,
            in GameEvent parentEvent)
        {
            var context = new EffectExecutionContext(
                SubjectType.Enemy,
                target.Id,
                projectile.SourceTowerId,
                binding.CardId,
                binding.CardInstanceId,
                projectile.Id,
                projectile.RootChainId,
                projectile.ActivationId,
                parentEvent.EventId,
                parentEvent.Depth + 1,
                0,
                0);

            // BindingKind는 카드의 투사체 해석을 적중 시 어떤 행동으로 바꿀지를 나타낸다.
            switch (binding.Kind)
            {
                case BindingKind.Burn:
                    ApplyStatus(context, StatusType.Burn, binding.Node);
                    break;
                case BindingKind.Poison:
                    ApplyStatus(context, StatusType.Poison, binding.Node);
                    SpawnHazard(
                        projectile,
                        binding,
                        target.Position,
                        target.Position,
                        BindingKind.Poison);
                    break;
                case BindingKind.Explosion:
                    ExecuteExplosionWithPresentation(
                        GetEnemyHitboxCenter(target),
                        projectile.DamageMilli,
                        projectile.SourceTowerId,
                        binding.CardId,
                        projectile.Id,
                        binding.Node,
                        projectile.RootChainId,
                        projectile.ActivationId,
                        parentEvent.EventId,
                        parentEvent.Depth + 1,
                        int.MaxValue,
                        "explode",
                        target.Id);
                    break;
                case BindingKind.Knockback:
                    ApplyDirectEnemyEffect(
                        context,
                        EffectOperation.ApplyKnockback,
                        binding.Node);
                    break;
                case BindingKind.Mark:
                    ApplyStatus(context, StatusType.Mark, binding.Node);
                    break;
                case BindingKind.Gold:
                    GrantProjectileBounty(projectile, target, binding, parentEvent);
                    break;
                case BindingKind.Stun:
                    ApplyStatus(context, StatusType.Stun, binding.Node);
                    break;
                case BindingKind.Bleed:
                    ApplyBleed(context, binding.Node);
                    break;
            }

            binding.TriggerCount++;
        }

        /// <summary>
        /// 골드 획득 카드의 투사체 해석을 처리한다.
        /// 같은 적을 처음 맞힌 횟수에 대한 투사체 한도와 타워별 웨이브 총량 한도를 모두 확인한 뒤
        /// CardBounty 출처의 보상 이벤트를 만든다.
        /// </summary>
        private void GrantProjectileBounty(
            ProjectileState projectile,
            EnemyState target,
            EffectBinding binding,
            in GameEvent parentEvent)
        {
            int projectileLimit = Math.Max(0, binding.Node.Limit);
            TowerState tower = FindTower(projectile.SourceTowerId);
            int amount = Math.Max(0, binding.Node.Amount);
            int towerLimit = Math.Max(0, binding.Node.Amount2);
            if (tower == null ||
                projectile.UniqueGoldHits >= projectileLimit ||
                amount <= 0 ||
                amount > towerLimit - tower.GoldGeneratedThisWave)
            {
                return;
            }

            // CardBounty 출처는 적 처치 보상과 구별된다. 경제 카드가 만든 골드가 다시 경제 트리거를
            // 일으키는 순환은 상위 시스템에서 이 RewardOrigin을 보고 차단할 수 있다.
            var reward = new GameEvent(
                tick,
                EventPhase.Reward,
                EventType.RewardGranted,
                projectile.RootChainId,
                parentEvent.EventId,
                projectile.ActivationId,
                projectile.SourceTowerId,
                binding.CardId,
                projectile.Id,
                target.Id,
                SubjectType.Enemy,
                parentEvent.Depth + 1,
                target.Generation,
                EventTags.Economic,
                RewardOrigin.CardBounty,
                payloadValue: amount);
            if (TryEnqueue(in reward, out _))
            {
                projectile.UniqueGoldHits++;
                tower.GoldGeneratedThisWave += amount;
            }
        }

        /// <summary>
        /// 화상 바인딩이 있는 투사체가 지나온 경로를 연속 선분 불길로 남긴다.
        /// 현재 작성 중인 선분은 매 이동 틱 끝점까지 늘리고, 설정 간격에 도달하면
        /// 선분을 확정한 뒤 다음 선분을 시작한다. 따라서 발사 직후부터 화면과 판정에
        /// 불길이 나타나면서도 이동 틱마다 새 장판을 만들지는 않는다.
        /// </summary>
        private void SpawnBurnTrailIfNeeded(
            ProjectileState projectile,
            SimPosition trailEndPosition,
            bool finalize,
            EntityId alreadyAffectedEnemyId)
        {
            for (int i = 0; i < projectile.Bindings.Count; i++)
            {
                EffectBinding binding = projectile.Bindings[i];
                if (binding.Kind != BindingKind.Burn)
                {
                    continue;
                }

                long segmentLength = PathModel.DistanceMilli(
                    binding.TrailStartPosition,
                    trailEndPosition);
                if (segmentLength <= 0)
                {
                    continue;
                }

                HazardState activeHazard =
                    FindHazard(binding.ActiveTrailHazardId);
                if (activeHazard == null)
                {
                    int hazardId = SpawnHazard(
                        projectile,
                        binding,
                        binding.TrailStartPosition,
                        trailEndPosition,
                        BindingKind.Burn);
                    if (hazardId < 0)
                    {
                        continue;
                    }

                    binding.ActiveTrailHazardId = hazardId;
                    binding.TrailStarted = true;
                    activeHazard = FindHazard(hazardId);
                }
                else
                {
                    // 아직 날아가는 탄환 바로 뒤까지 불길이 이어져 보이도록
                    // 작성 중 선분의 끝과 수명을 현재 이동 결과로 갱신한다.
                    activeHazard.EndPosition = trailEndPosition;
                    activeHazard.RemainingTicks =
                        activeHazard.DurationTicks;
                }

                // 직격 대상은 같은 카드의 OnHit 화상을 이미 받는다.
                // 적중점 불길이 다음 틱 즉시 같은 화상을 한 번 더 주지 않도록
                // 해당 선분의 접촉 원장에 선등록한다.
                if (alreadyAffectedEnemyId.IsValid)
                {
                    MarkDirectHitOnIntersectingBurnHazards(
                        projectile,
                        binding,
                        alreadyAffectedEnemyId,
                        trailEndPosition);
                }

                int interval = Math.Max(1, binding.Node.Amount2);
                if (finalize || segmentLength >= interval)
                {
                    binding.TrailStartPosition =
                        trailEndPosition;
                    binding.ActiveTrailHazardId = -1;
                }
            }
        }

        /// <summary>
        /// 화염 길 또는 독안개 같은 짧은 수명의 논리 위험 지대를 생성한다.
        /// 활성 위험 지대 수와 체인 작업량을 먼저 검사해 화면을 채우는 조합도 브라우저를 멈추지 않게 한다.
        /// </summary>
        private int SpawnHazard(
            ProjectileState projectile,
            EffectBinding binding,
            SimPosition startPosition,
            SimPosition endPosition,
            BindingKind kind)
        {
            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.StatusApplied,
                    projectile.RootChainId,
                    projectile.SourceTowerId,
                    binding.CardId,
                    projectile.Id,
                    SubjectType.Projectile),
                1);
            if (hazards.Count >= content.Safety.MaxActiveHazards)
            {
                AddDiagnostic(
                    DiagnosticCode.ActiveHazardLimitReached,
                    diagnosticEvent,
                    hazards.Count);
                return -1;
            }

            // 위험 지대 자체는 실행 이벤트가 아니지만 이후 상태 적용 작업을 만들기 때문에 체인 예산을 소비한다.
            if (!TryReserveComposite(
                    in diagnosticEvent,
                    chainEventCount: 1,
                    queueSlotCount: 0,
                    projectileSpawnCount: 0,
                    cardTriggerCount: 0))
            {
                return -1;
            }

            int durationTicks = kind == BindingKind.Poison
                ? Math.Max(1, binding.Node.Amount2)
                : Math.Max(
                    1,
                    binding.Node.Amount3 > 0
                        ? binding.Node.Amount3
                        : binding.Node.DurationTicks / 2);
            int hazardId = nextHazardId++;
            hazards.Add(new HazardState
            {
                Id = hazardId,
                Kind = kind,
                StartPosition = startPosition,
                EndPosition = endPosition,
                RadiusMilli = Math.Max(1, binding.Node.RadiusMilli),
                DurationTicks = durationTicks,
                RemainingTicks = durationTicks,
                SourceTowerId = projectile.SourceTowerId,
                SourceCardId = binding.CardId,
                SourceCardInstanceId = binding.CardInstanceId,
                SourceEntityId = projectile.Id,
                RootChainId = projectile.RootChainId,
                Node = binding.Node
            });
            return hazardId;
        }

        /// <summary>
        /// 활성 위험 지대의 수명을 줄이고 범위 안에 처음 들어온 적에게 상태를 한 번 적용한다.
        /// AppliedEnemies 집합 때문에 한 위험 지대가 같은 적에게 매 틱 중첩을 무한히 쌓지 않는다.
        /// </summary>
        private void ProcessHazards()
        {
            hazardContactsThisTick.Clear();
            for (int hazardIndex = 0; hazardIndex < hazards.Count; hazardIndex++)
            {
                HazardState hazard = hazards[hazardIndex];
                hazard.RemainingTicks--;
                if (hazard.RemainingTicks < 0)
                {
                    continue;
                }

                SimPosition queryCenter =
                    GetSegmentMidpoint(
                        hazard.StartPosition,
                        hazard.EndPosition);
                int halfLength = (int)Math.Min(
                    int.MaxValue,
                    (PathModel.DistanceMilli(
                        hazard.StartPosition,
                        hazard.EndPosition) + 1L) / 2L);
                int queryRadius = checked(
                    hazard.RadiusMilli + halfLength);
                spatialIndex.Query(
                    queryCenter,
                    queryRadius,
                    spatialScratch);
                for (int enemyIndex = 0; enemyIndex < spatialScratch.Count; enemyIndex++)
                {
                    EnemyState enemy = FindEnemy(spatialScratch[enemyIndex]);
                    if (!enemy.Alive ||
                        hazard.AppliedEnemies.Contains(enemy.Id.Value) ||
                        !SegmentIntersectsCircle(
                            hazard.StartPosition,
                            hazard.EndPosition,
                            enemy.Position,
                            hazard.RadiusMilli,
                            out _))
                    {
                        continue;
                    }

                    var contactKey = new HazardContactKey(
                        hazard.SourceEntityId.Value,
                        hazard.SourceCardInstanceId,
                        enemy.Id.Value);
                    // 맞닿은 두 불길 조각의 경계에 선 적은 같은 틱에 화상을
                    // 여러 번 받지 않는다. 먼저 성공한 조각과 같은 출처의
                    // 나머지 조각도 적용 원장에는 기록해 다음 틱 중복을 막는다.
                    if (hazardContactsThisTick.Contains(contactKey))
                    {
                        hazard.AppliedEnemies.Add(enemy.Id.Value);
                        continue;
                    }

                    // 상태를 붙이기 전에 체인 예산을 예약한다. 실패하면 AppliedEnemies에도 기록하지 않으므로
                    // 상태만 빠지고 기록만 남는 불완전한 변경이 없다.
                    GameEvent diagnosticEvent = WithDiagnosticDepth(
                        CreateDiagnosticEvent(
                            EventType.StatusApplied,
                            hazard.RootChainId,
                            hazard.SourceTowerId,
                            hazard.SourceCardId,
                            enemy.Id,
                            SubjectType.Enemy),
                        1);
                    if (!TryReserveComposite(
                            in diagnosticEvent,
                            chainEventCount: 1,
                            queueSlotCount: 0,
                            projectileSpawnCount: 0,
                            cardTriggerCount: 0))
                    {
                        continue;
                    }

                    hazardContactsThisTick.Add(contactKey);
                    hazard.AppliedEnemies.Add(enemy.Id.Value);
                    var context = new EffectExecutionContext(
                        SubjectType.Enemy,
                        enemy.Id,
                        hazard.SourceTowerId,
                        hazard.SourceCardId,
                        hazard.SourceCardInstanceId,
                        hazard.SourceEntityId,
                        hazard.RootChainId,
                        CreateActivation(),
                        EventId.Invalid,
                        1,
                        0,
                        0);
                    ApplyStatus(
                        context,
                        hazard.Kind == BindingKind.Burn
                            ? StatusType.Burn
                            : StatusType.Poison,
                        hazard.Node);
                }
            }
        }

        private HazardState FindHazard(int hazardId)
        {
            if (hazardId < 0)
            {
                return null;
            }

            for (int i = 0; i < hazards.Count; i++)
            {
                if (hazards[i].Id == hazardId)
                {
                    return hazards[i];
                }
            }

            return null;
        }

        private void MarkDirectHitOnIntersectingBurnHazards(
            ProjectileState projectile,
            EffectBinding binding,
            EntityId enemyId,
            SimPosition enemyPosition)
        {
            for (int i = 0; i < hazards.Count; i++)
            {
                HazardState hazard = hazards[i];
                if (hazard.Kind != BindingKind.Burn ||
                    hazard.SourceEntityId != projectile.Id ||
                    hazard.SourceCardInstanceId !=
                    binding.CardInstanceId ||
                    !SegmentIntersectsCircle(
                        hazard.StartPosition,
                        hazard.EndPosition,
                        enemyPosition,
                        hazard.RadiusMilli,
                        out _))
                {
                    continue;
                }

                hazard.AppliedEnemies.Add(enemyId.Value);
            }
        }

        private static SimPosition GetSegmentMidpoint(
            SimPosition start,
            SimPosition end)
        {
            return SimPosition.FromMilliUnits(
                start.X.MilliUnits +
                (end.X.MilliUnits -
                 start.X.MilliUnits) / 2L,
                start.Y.MilliUnits +
                (end.Y.MilliUnits -
                 start.Y.MilliUnits) / 2L);
        }

        /// <summary>
        /// 한 지점을 중심으로 범위 피해를 만든다.
        /// 후보를 모두 수집하고 안정된 우선순위로 정렬한 뒤 피해 이벤트 전체를 원자적 배치로 등록한다.
        /// 예산이 부족하면 일부 적만 맞는 것이 아니라 이번 폭발 전체가 거절된다.
        /// </summary>
        private void ExecuteExplosion(
            SimPosition position,
            long baseDamageMilli,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            in CompiledEffectNode node,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            int targetLimit = int.MaxValue)
        {
            ExecuteExplosionWithPresentation(
                position,
                baseDamageMilli,
                sourceTowerId,
                sourceCardId,
                sourceEntityId,
                node,
                rootChainId,
                activationId,
                parentEventId,
                depth,
                targetLimit,
                "explode",
                sourceEntityId);
        }

        private void ExecuteExplosionWithPresentation(
            SimPosition position,
            long baseDamageMilli,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            in CompiledEffectNode node,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            int targetLimit,
            string presentationId,
            EntityId presentationSubjectId)
        {
            long damage = DeterministicMath.MultiplyBasisPoints(
                baseDamageMilli,
                node.Amount);
            if (node.Amount2 > 0)
            {
                damage = Math.Min(damage, node.Amount2);
            }

            // 적마다 크기에 따른 피격 반지름이 다르므로 넉넉한 반경으로 공간 인덱스를 조회한 뒤
            // 각 후보에 대해 정확한 실제 반지름 검사를 다시 한다.
            int maximumEnemyRadius =
                GetMaximumEnemyHitboxReachMilli();
            spatialIndex.Query(
                position,
                checked(node.RadiusMilli + maximumEnemyRadius),
                spatialScratch);
            var candidates = new List<EnemyState>(spatialScratch.Count);
            for (int i = 0; i < spatialScratch.Count; i++)
            {
                EnemyState enemy = FindEnemy(spatialScratch[i]);
                if (!enemy.Alive ||
                    !DoesAreaCircleOverlapEnemyHitbox(
                        position,
                        node.RadiusMilli,
                        enemy))
                {
                    continue;
                }

                candidates.Add(enemy);
            }

            // 공간 인덱스의 내부 반환 순서에 의존하지 않도록 모든 후보를 공통 규칙으로 정렬한다.
            candidates.Sort((left, right) =>
                CompareTargetPriority(position, left, right));
            int boundedTargetCount =
                targetLimit > 0
                    ? Math.Min(candidates.Count, targetLimit)
                    : candidates.Count;
            var damageEvents =
                new List<GameEvent>(boundedTargetCount);
            for (int i = 0; i < boundedTargetCount; i++)
            {
                if (TryCreateDamageEvent(
                        candidates[i].Id,
                        sourceTowerId,
                        sourceCardId,
                        sourceEntityId,
                        damage,
                        DamageKind.Explosion,
                        0,
                        rootChainId,
                        activationId,
                        parentEventId,
                        depth,
                        EventTags.Area,
                        out GameEvent damageEvent))
                {
                    damageEvents.Add(damageEvent);
                }
            }

            TryEnqueueBatch(damageEvents);
            RecordExplosionTrigger();
            AddPresentation(
                PresentationEventType.AreaEffectTriggered,
                presentationSubjectId.Value,
                sourceEntityId.Value,
                node.RadiusMilli,
                presentationId,
                effectPosition: position,
                hasEffectPosition: true);
        }

        /// <summary>
        /// 적을 경로 진행도 기준으로 밀어내고, 이동 구간에서 다른 적과 충돌하면 양쪽 피해를 예약한다.
        /// 정예·보스는 직접 이동시키지 않고 제어 게이지로 변환한다.
        /// </summary>
        private void ApplyKnockback(
            EnemyState enemy,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (UsesEliteControlRules(enemy))
            {
                ApplyControlGauge(
                    enemy,
                    context,
                    Math.Max(1, node.ChanceBps));
                return;
            }

            long progressDelta = -node.Amount;
            ProjectileState sourceProjectile =
                FindProjectile(context.SourceEntityId);
            if (sourceProjectile != null)
            {
                // 투사체 방향과 해당 지점의 경로 방향을 내적해, 탄환이 진행 방향에서 맞았는지
                // 반대 방향에서 맞았는지를 경로 진행도의 +/− 변화로 투영한다.
                path.GetDirectionBasisPoints(
                    enemy.PathProgressMilli,
                    out int pathDirectionX,
                    out int pathDirectionY);
                long directionDot =
                    ((long)sourceProjectile.DirectionXBps *
                     pathDirectionX) +
                    ((long)sourceProjectile.DirectionYBps *
                     pathDirectionY);
                int projectedDirectionBps = (int)Math.Max(
                    -10000,
                    Math.Min(10000, directionDot / 10000));
                progressDelta =
                    DeterministicMath.MultiplyBasisPoints(
                        node.Amount,
                        projectedDirectionBps);
            }

            long proposedProgress = Math.Max(
                0,
                Math.Min(
                    path.TotalLengthMilli,
                    checked(
                        enemy.PathProgressMilli +
                        progressDelta)));
            if (proposedProgress == enemy.PathProgressMilli)
            {
                return;
            }
            SimPosition proposedPosition =
                path.GetPosition(proposedProgress) +
                enemy.PathLateralOffset;

            // 출발점과 도착점만 검사하면 굽은 경로나 큰 밀치기에서 중간 적을 건너뛸 수 있으므로,
            // 전체 이동 구간을 샘플링해 넓은 후보를 모은 뒤 경로 모델의 정밀 접촉 거리로 첫 충돌을 고른다.
            EnemyState collided = null;
            long collisionTravel = long.MaxValue;
            CollectKnockbackSweepCandidates(
                enemy.PathProgressMilli,
                proposedProgress,
                checked(
                    node.RadiusMilli +
                    GetEnemyHitRadiusMilli(enemy) +
                    run.EnemyBaseHitRadiusMilli * 3));
            for (int i = 0; i < sweepScratch.Count; i++)
            {
                EnemyState candidate = FindEnemy(sweepScratch[i]);
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.Id == enemy.Id)
                {
                    continue;
                }

                int contactRadius = checked(
                    node.RadiusMilli +
                    GetEnemyHitRadiusMilli(enemy) +
                    GetEnemyHitRadiusMilli(candidate));
                if (!path.TryGetSweepContactDistance(
                        candidate.Position,
                        enemy.PathProgressMilli,
                        proposedProgress,
                        contactRadius,
                        out long travel))
                {
                    continue;
                }

                if (collided == null ||
                    travel < collisionTravel ||
                    (travel == collisionTravel &&
                     candidate.Id.Value < collided.Id.Value))
                {
                    collided = candidate;
                    collisionTravel = travel;
                }
            }

            if (collided != null && node.Amount2 > 0)
            {
                // 충돌 피해는 반드시 두 적 모두에게 들어가거나 둘 다 들어가지 않아야 한다.
                // 두 이벤트를 먼저 만들고 한 배치로 예약한 뒤에만 실제 위치를 옮긴다.
                var collisionEvents = new List<GameEvent>(2);
                if (TryCreateDamageEvent(
                        enemy.Id,
                        context.TowerId,
                        context.CardId,
                        context.SubjectId,
                        node.Amount2,
                        DamageKind.Collision,
                        0,
                        context.RootChainId,
                        context.ActivationId,
                        context.ParentEventId,
                        context.Depth + 1,
                        EventTags.Control,
                        out GameEvent selfDamage))
                {
                    collisionEvents.Add(selfDamage);
                }
                if (TryCreateDamageEvent(
                        collided.Id,
                        context.TowerId,
                        context.CardId,
                        context.SubjectId,
                        node.Amount2,
                        DamageKind.Collision,
                        0,
                        context.RootChainId,
                        context.ActivationId,
                        context.ParentEventId,
                        context.Depth + 1,
                        EventTags.Control,
                        out GameEvent otherDamage))
                {
                    collisionEvents.Add(otherDamage);
                }

                if (collisionEvents.Count != 2 ||
                    !TryEnqueueBatch(collisionEvents))
                {
                    return;
                }
            }

            // 모든 필요한 피해 이벤트 예약에 성공한 뒤 마지막으로 위치를 확정한다.
            long previousProgress = enemy.PathProgressMilli;
            enemy.PathProgressMilli = proposedProgress;
            enemy.Position = proposedPosition;
            long movedDistance = Math.Abs(
                proposedProgress - previousProgress);
            TriggerBleedFromMovement(
                enemy,
                movedDistance,
                context);
            TryEnemyRicochetAfterForcedMovement(
                enemy,
                context);
            // 밀치기는 같은 틱 안의 후속 폭발·타워 효과가 새 위치를 봐야 하므로 공간 인덱스도 즉시 갱신한다.
            spatialIndex.Rebuild(enemies);
            AddPresentation(
                PresentationEventType.EffectTriggered,
                enemy.Id.Value,
                enemy.Id.Value,
                (int)Math.Min(int.MaxValue, movedDistance),
                "knockback");
        }

        /// <summary>
        /// 밀치기 경로 주변의 충돌 후보 ID를 공간 인덱스에서 수집한다.
        /// 중복 ID를 제거하고 마지막에 EntityId 순으로 정렬해 해시 자료구조의 순회 순서에 의존하지 않는다.
        /// 실제 충돌 여부와 가장 이른 접촉점 계산은 호출자가 수행한다.
        /// </summary>
        private void CollectKnockbackSweepCandidates(
            long startProgress,
            long endProgress,
            int queryRadius)
        {
            sweepScratch.Clear();
            sweepIds.Clear();
            long distance = Math.Abs(endProgress - startProgress);
            int sampleCount = Math.Max(
                1,
                checked((int)(distance / 1000L) + 1));
            int boundedRadius = checked(queryRadius + 1000);

            for (int sample = 0; sample <= sampleCount; sample++)
            {
                long progress = startProgress +
                    ((endProgress - startProgress) * sample /
                     sampleCount);
                spatialIndex.Query(
                    path.GetPosition(progress),
                    boundedRadius,
                    spatialScratch);
                for (int i = 0; i < spatialScratch.Count; i++)
                {
                    EntityId id = spatialScratch[i];
                    if (sweepIds.Add(id.Value))
                    {
                        sweepScratch.Add(id);
                    }
                }
            }

            sweepScratch.Sort(
                (left, right) =>
                    left.Value.CompareTo(right.Value));
        }

        /// <summary>
        /// 적 보상 증가 효과를 해당 적의 분열 가계 장부에 적용한다.
        /// (타워, 카드 인스턴스, 증가 종류) 키는 lineage 전체에서 한 번만 허용하므로
        /// 같은 카드가 분열한 자식들에게 반복 적용되어 원래 보상 총량을 복제하지 못한다.
        /// </summary>
        private void IncreaseEnemyReward(
            EnemyState enemy,
            TowerId towerId,
            int cardInstanceId,
            RewardAugmentKind augmentKind,
            int increaseBps,
            int maximumBonusBps)
        {
            if (increaseBps <= 0 ||
                !lineages.TryGetValue(
                    enemy.LineageId.Value,
                    out LineageState lineage))
            {
                return;
            }

            var key = new RewardAugmentKey(
                towerId,
                cardInstanceId,
                augmentKind);
            if (!lineage.AppliedRewardAugments.Add(key))
            {
                return;
            }

            // 기본 보상에서 계산한 카드별 요청치와 전체 최대 보너스 중 허용되는 부분만 더한다.
            int boundedIncrease = Math.Max(0, increaseBps);
            int boundedMaximum = Math.Max(0, maximumBonusBps);
            int requestedBonus = (int)DeterministicMath.MultiplyBasisPoints(
                lineage.BaseRewardBudget,
                boundedIncrease);
            int maximumBonus = (int)DeterministicMath.MultiplyBasisPoints(
                lineage.BaseRewardBudget,
                boundedMaximum);
            int alreadyAdded = Math.Max(
                0,
                lineage.MaxRewardBudget - lineage.BaseRewardBudget);
            int allowedBonus = Math.Max(0, maximumBonus - alreadyAdded);
            int grantedBonus = Math.Min(
                Math.Max(0, requestedBonus),
                allowedBonus);
            if (grantedBonus <= 0)
            {
                return;
            }

            lineage.MaxRewardBudget = checked(
                lineage.MaxRewardBudget + grantedBonus);
            enemy.RewardBudget = checked(
                enemy.RewardBudget + grantedBonus);
        }

        /// <summary>
        /// 앞 단계에서 방어력·취약·저항 계산까지 끝낸 최종 피해 이벤트를 체력에 반영한다.
        /// 체력이 0이 된 최초 한 번만 DeathQueued를 켜고 사망 이벤트를 예약한다.
        /// </summary>
        private void ProcessDamageEvent(in GameEvent gameEvent)
        {
            EnemyState enemy = FindEnemy(gameEvent.SubjectEntityId);
            if (enemy == null || !enemy.Alive || gameEvent.PayloadValue <= 0)
            {
                DiscardMythicRelayEvent(
                    gameEvent.EventId);
                return;
            }

            long amount = gameEvent.PayloadValue;
            if (enemy.ShieldMilli > 0)
            {
                long absorbed = Math.Min(
                    enemy.ShieldMilli,
                    amount);
                enemy.ShieldMilli -= absorbed;
                amount -= absorbed;
            }
            long healthBefore = enemy.HealthMilli;
            enemy.HealthMilli = Math.Max(
                0,
                healthBefore - amount);
            long appliedAmount = Math.Min(
                healthBefore,
                amount);
            RecordCardDamage(
                gameEvent.SourceCardId,
                appliedAmount);
            HandleUncommonDamageApplied(
                enemy,
                gameEvent,
                appliedAmount);
            HandleRareGenerationMotionDamageApplied(
                enemy,
                gameEvent,
                appliedAmount);
            HandleRareEnemyDamaged(
                enemy,
                gameEvent);
            HandleLegendaryEnemyDamaged(
                enemy,
                gameEvent,
                appliedAmount);
            HandleMythicEnemyDamageApplied(
                enemy,
                gameEvent,
                appliedAmount);
            AddPresentation(
                PresentationEventType.EnemyDamaged,
                enemy.Id.Value,
                gameEvent.SourceEntityId.Value,
                (int)Math.Min(int.MaxValue, appliedAmount));

            if (enemy.HealthMilli == 0 && !enemy.DeathQueued)
            {
                // 사망을 별도 Death 단계로 미뤄 사망 폭발, 데스 엔진, 보상 순서를 일관되게 유지한다.
                var death = new GameEvent(
                    tick,
                    EventPhase.Death,
                    EventType.EnemyDied,
                    gameEvent.RootChainId,
                    gameEvent.EventId,
                    gameEvent.ActivationId,
                    gameEvent.SourceTowerId,
                    gameEvent.SourceCardId,
                    gameEvent.SourceEntityId,
                    enemy.Id,
                    SubjectType.Enemy,
                    gameEvent.Depth + 1,
                    enemy.Generation,
                    EventTags.Death,
                    gameEvent.RewardOrigin);
                enemy.DeathQueued = true;
                if (!TryEnqueue(in death, out _))
                {
                    // 안전 예산이 꽉 찬 상황에도 체력 0인 적이 살아 남지 않도록 사망의 필수 정리는 직접 수행한다.
                    ProcessEnemyDeathEvent(in death);
                }
            }
        }

        /// <summary>
        /// 적 사망을 단 한 번 확정하고, 가계 진행도·사망 바인딩·데스 엔진·보상을 차례로 처리한다.
        /// 이 함수가 끝난 뒤 개체는 비활성 상태이며 실제 목록 제거는 틱 말 CleanupDeadEntities가 담당한다.
        /// </summary>
        private void ProcessEnemyDeathEvent(in GameEvent gameEvent)
        {
            EnemyState enemy = FindEnemy(gameEvent.SubjectEntityId);
            if (enemy == null || !enemy.Alive || enemy.HealthMilli > 0)
            {
                return;
            }

            if (TryHandleRareEnemyRebirth(
                    enemy,
                    gameEvent))
            {
                return;
            }
            if (TryHandleMythicEnemyLifecycle(
                    enemy,
                    gameEvent))
            {
                return;
            }
            HandleRareEnemyFinalDeath(
                enemy,
                gameEvent);
            HandleLegendaryEnemyDeath(
                enemy,
                gameEvent);
            HandleMythicEnemyFinalDeath(
                enemy,
                gameEvent);
            RecordEnemyKillTelemetry(
                enemy,
                in gameEvent);
            CardEffectVisualFlags deathVisualFlags =
                GetEnemyDeathVisualFlags(enemy);
            HandleUncommonEnemyDeath(
                enemy,
                gameEvent);
            enemy.Alive = false;
            AwardCardPackProgress(enemy);
            DecrementLineage(enemy);
            // 분열된 각 개체가 나눠 가진 웨이브 진행도를 lineage 장부에서 소진한다.
            if (lineages.TryGetValue(
                    enemy.LineageId.Value,
                    out LineageState lineage))
            {
                lineage.ConsumedProgress = checked(
                    lineage.ConsumedProgress +
                    Math.Max(0, enemy.WaveProgressBudget));
                enemy.WaveProgressBudget = 0;
            }
            // 적 해석의 폭발 카드는 적용 즉시 터지지 않고 이 사망 시점까지 바인딩으로 보관된다.
            for (int i = 0; i < enemy.DeathBindings.Count; i++)
            {
                EffectBinding binding = enemy.DeathBindings[i];
                if (binding.Kind == BindingKind.Explosion && !binding.Used)
                {
                    ExecuteExplosion(
                        GetEnemyHitboxCenter(enemy),
                        enemy.MaxHealthMilli,
                        binding.CardId.IsValid
                            ? FindBindingTower(binding, gameEvent.SourceTowerId)
                            : gameEvent.SourceTowerId,
                        binding.CardId,
                        enemy.Id,
                        binding.Node,
                        gameEvent.RootChainId,
                        gameEvent.ActivationId,
                        gameEvent.EventId,
                        gameEvent.Depth + 1);
                    binding.Used = true;
                }
            }

            // 사건 기반 타워는 사망 자체와 같은 RootChain을 이어받고,
            // Trigger 레지스트리가 현재 사건을 처리하는 핸들러만 실행한다.
            ProcessEnemyDiedTowerTriggers(
                enemy,
                in gameEvent);

            if (!enemy.RewardClaimed && enemy.RewardBudget > 0)
            {
                // RewardClaimed를 통해 중복 사망 이벤트가 있더라도 같은 개체 보상이 두 번 지급되지 않는다.
                var reward = new GameEvent(
                    tick,
                    EventPhase.Reward,
                    EventType.RewardGranted,
                    gameEvent.RootChainId,
                    gameEvent.EventId,
                    gameEvent.ActivationId,
                    gameEvent.SourceTowerId,
                    gameEvent.SourceCardId,
                    enemy.Id,
                    enemy.Id,
                    SubjectType.Enemy,
                    gameEvent.Depth,
                    enemy.Generation,
                    EventTags.Economic,
                    RewardOrigin.EnemyDrop,
                    payloadValue: enemy.RewardBudget);
                if (TryEnqueue(in reward, out _))
                {
                    enemy.RewardClaimed = true;
                }
                else
                {
                    // 큐 한도 때문에 보상 이벤트를 넣지 못해도 이미 확정된 정당한 보상은 잃지 않도록 즉시 처리한다.
                    enemy.RewardClaimed = true;
                    ProcessRewardEvent(in reward);
                }
            }

            AddPresentation(
                PresentationEventType.EnemyDied,
                enemy.Id.Value,
                gameEvent.SourceEntityId.Value,
                enemy.RewardBudget,
                content.GetEnemy(enemy.DefinitionId).StableId,
                (ulong)deathVisualFlags);
        }

        /// <summary>
        /// 바인딩을 만든 카드 인스턴스가 어느 타워에 장착되어 있는지 찾아 효과 출처를 복원한다.
        /// 찾을 수 없는 경우 사망을 발생시킨 타워를 안전한 대체 출처로 사용한다.
        /// </summary>
        private TowerId FindBindingTower(EffectBinding binding, TowerId fallback)
        {
            CardInstanceState card = FindCardInstance(binding.CardInstanceId);
            return card != null && card.EquippedTowerId.IsValid
                ? card.EquippedTowerId
                : fallback;
        }

        /// <summary>
        /// 보상 이벤트를 실제 골드에 더하고, 적 드롭인 경우 lineage 지급 장부와 개체 잔여 예산도 갱신한다.
        /// CardBounty 같은 카드 보상은 EnemyDrop이 아니므로 적의 원래 처치 보상 예산을 소비하지 않는다.
        /// </summary>
        private void ProcessRewardEvent(in GameEvent gameEvent)
        {
            int amount = (int)Math.Max(
                0,
                Math.Min(int.MaxValue, gameEvent.PayloadValue));
            if (amount == 0)
            {
                return;
            }

            gold = checked(gold + amount);
            RecordGoldTelemetry(
                amount,
                gameEvent.RewardOrigin);
            if (gameEvent.RewardOrigin == RewardOrigin.EnemyDrop)
            {
                EnemyState enemy = FindEnemy(gameEvent.SubjectEntityId);
                if (enemy != null &&
                    lineages.TryGetValue(
                        enemy.LineageId.Value,
                        out LineageState lineage))
                {
                    lineage.PaidReward = checked(lineage.PaidReward + amount);
                    enemy.RewardBudget = Math.Max(
                        0,
                        enemy.RewardBudget - amount);
                }
            }
            AddPresentation(
                PresentationEventType.RewardGranted,
                gameEvent.SubjectEntityId.Value,
                gameEvent.SourceTowerId.Value,
                amount,
                gameEvent.RewardOrigin.ToString());
        }

        /// <summary>
        /// 아직 남은 시간이 있는 특정 상태가 하나라도 있는지 확인한다.
        /// </summary>
        private bool HasActiveStatus(EnemyState enemy, StatusType type)
        {
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                if (enemy.Statuses[i].Type == type &&
                    enemy.Statuses[i].RemainingTicks > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 여러 둔화 인스턴스와 중첩을 곱연산으로 합성하고 최종 둔화율을 반환한다.
        /// 덧셈이 아니라 남은 속도를 계속 곱하므로 여러 약한 둔화가 자연스럽게 체감 감소하며,
        /// 기본 전역 상한 60%(6,000 bps) 및 카드가 지정한 더 낮은 상한을 지킨다.
        /// </summary>
        private int GetSlowBps(EnemyState enemy)
        {
            int remainingSpeedBps = 10000;
            int globalLimit = 6000;
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type == StatusType.Slow)
                {
                    if (status.Limit > 0)
                    {
                        globalLimit = Math.Min(globalLimit, status.Limit);
                    }

                    int perStackRemaining = Math.Max(
                        0,
                        10000 - status.Intensity);
                    for (int stack = 0; stack < status.Stacks; stack++)
                    {
                        remainingSpeedBps = (int)
                            DeterministicMath.MultiplyBasisPoints(
                                remainingSpeedBps,
                                perStackRemaining);
                    }
                }
            }

            int slow = 10000 - remainingSpeedBps;
            return Math.Min(globalLimit, Math.Max(0, slow));
        }

        /// <summary>
        /// 적이 사망하거나 본진에 유출될 때 분열 가계의 현재 생존 개체 수를 하나 줄인다.
        /// </summary>
        private void DecrementLineage(EnemyState enemy)
        {
            if (lineages.TryGetValue(enemy.LineageId.Value, out LineageState lineage))
            {
                lineage.LiveMembers = Math.Max(0, lineage.LiveMembers - 1);
                lineage.LastResolvedPosition = enemy.Position;
                if (lineage.LiveMembers == 0)
                {
                    ResolveCompletedLineage(lineage);
                }
            }
        }

        /// <summary>
        /// 사거리 안에서 분열된 자식에게 부모의 진입 여부와 최근 발동 틱을 복사한다.
        /// 이 상속이 없으면 분열 자체가 '새로운 사거리 진입'으로 오인되어 오벨리스크가 즉시 재귀 발동한다.
        /// </summary>
        private void InheritRangeEntryLocks(
            EnemyState parent,
            EnemyState child)
        {
            for (int towerIndex = 0;
                 towerIndex < towers.Count;
                 towerIndex++)
            {
                TowerState tower = towers[towerIndex];
                CompiledTowerDefinition definition =
                    content.GetTower(tower.DefinitionId);
                if (definition.Trigger != TowerTrigger.EnemyEnteredRange)
                {
                    continue;
                }

                if (tower.TargetsInside.Contains(parent.Id.Value))
                {
                    tower.TargetsInside.Add(child.Id.Value);
                }

                if (tower.LastTargetTriggerTick.TryGetValue(
                        parent.Id.Value,
                        out long lastTriggerTick))
                {
                    tower.LastTargetTriggerTick[child.Id.Value] =
                        lastTriggerTick;
                }
            }
        }

        /// <summary>
        /// 적 크기·속도·피해 취약 배율을 정수 비율로 곱하고 합리적인 전역 범위(0~300%)로 제한한다.
        /// </summary>
        private static int MultiplyBps(int value, int multiplier)
        {
            return (int)Math.Max(
                0,
                Math.Min(
                    30000,
                    DeterministicMath.MultiplyBasisPoints(value, multiplier)));
        }
    }
}
