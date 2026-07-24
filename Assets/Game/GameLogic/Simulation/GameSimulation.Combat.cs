using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    // 이 파일은 한 틱 안에서 실제 전투 공간이 어떻게 변하는지를 담당한다.
    // Unity의 Transform이나 충돌 컴포넌트를 사용하지 않고, 정수 좌표와 경로 진행도만으로
    // 적 이동, 타워 발동, 투사체 비행 및 충돌을 계산한다. 따라서 같은 콘텐츠·시드·명령을
    // 입력하면 Editor와 WebGL에서도 동일한 전투 결과를 재현할 수 있다.
    public sealed partial class GameSimulation
    {
        /// <summary>
        /// 살아 있는 모든 적을 경로 위에서 한 틱만큼 이동시킨다.
        /// 적의 진짜 위치 원본은 화면상의 오브젝트가 아니라 PathProgressMilli(경로 진행 거리)다.
        /// 이 값에서 Position을 다시 계산하므로 밀치기, 역행, 공포 같은 효과도 같은 모델을 쓸 수 있다.
        /// </summary>
        private void MoveEnemies()
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                // 사망 처리가 예약된 적도 더 이동하면 사망 위치와 보상 대상이 달라질 수 있으므로 제외한다.
                if (!enemy.Alive || enemy.DeathQueued)
                {
                    continue;
                }

                int movementMultiplier = enemy.SpeedMultiplierBps;
                // 기절은 이동 배율을 0으로 만들고, 일반 둔화는 여러 효과를 합성한 뒤 상한을 적용한다.
                // Bps는 10,000을 100%로 보는 정수 비율 단위라 부동소수점 오차가 없다.
                if (HasActiveStatus(enemy, StatusType.Stun))
                {
                    movementMultiplier = 0;
                }
                else
                {
                    int slowBps = GetSlowBps(enemy);
                    movementMultiplier = (int)DeterministicMath.MultiplyBasisPoints(
                        movementMultiplier,
                        10000 - slowBps);
                }

                int distance = (int)DeterministicMath.MultiplyBasisPoints(
                    enemy.BaseSpeedMilliPerTick,
                    movementMultiplier);
                if (distance > 0)
                {
                    // 경로 끝을 넘어가지 않도록 진행도를 고정한 뒤, 경로 모델에서 논리 좌표를 얻는다.
                    enemy.PathProgressMilli = Math.Min(
                        path.TotalLengthMilli,
                        enemy.PathProgressMilli + distance);
                    enemy.Position = path.GetPosition(enemy.PathProgressMilli);
                    AddPresentation(
                        PresentationEventType.EnemyMoved,
                        enemy.Id.Value,
                        -1,
                        distance);
                }

                // 경로 끝 도달 판정은 좌표 근사치가 아니라 진행도로 한다.
                if (enemy.PathProgressMilli >= path.TotalLengthMilli)
                {
                    LeakEnemy(enemy);
                }
            }
        }

        /// <summary>
        /// 적이 본진에 도달했을 때 그 적의 보상과 웨이브 기여도를 소진시키고 본진 피해를 적용한다.
        /// 유출된 적은 처치가 아니므로 골드를 주지 않으며, 분열 가계의 장부에는 몰수된 보상으로 남는다.
        /// </summary>
        private void LeakEnemy(EnemyState enemy)
        {
            if (!enemy.Alive)
            {
                return;
            }

            enemy.Alive = false;
            // 분열한 여러 개체가 하나의 원래 적 보상을 나눠 갖기 때문에, 개체가 아니라 lineage 장부에
            // 지급·몰수·진행도 소진을 누적한다. 그래야 분열로 총 골드나 웨이브 진행도가 늘어나지 않는다.
            if (lineages.TryGetValue(
                    enemy.LineageId.Value,
                    out LineageState lineage))
            {
                lineage.ForfeitedReward = checked(
                    lineage.ForfeitedReward + Math.Max(0, enemy.RewardBudget));
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
            CompiledEnemyDefinition definition = content.GetEnemy(enemy.DefinitionId);
            int leakDamage =
                enemy.IsShimmering ? 0 : definition.LeakDamage;
            baseHealth = Math.Max(0, baseHealth - leakDamage);
            DecrementLineage(enemy);
            AddPresentation(
                PresentationEventType.EnemyLeaked,
                enemy.Id.Value,
                -1,
                leakDamage,
                definition.StableId);

            // 본진 체력이 정확히 0이 된 순간 런 상태를 패배로 바꾸고, 앞단용 알림만 별도로 남긴다.
            if (baseHealth == 0)
            {
                phase = RunPhase.Defeat;
                AddPresentation(PresentationEventType.RunLost, currentWaveIndex);
            }
        }

        /// <summary>
        /// 배치된 타워를 안정된 목록 순서대로 순회하며 각 타워 정의의 Trigger에 맞는 로직을 호출한다.
        /// 타워 종류마다 별도 MonoBehaviour를 만들지 않고, 데이터의 Trigger가 실행 문맥을 선택한다.
        /// </summary>
        private void ProcessTowers()
        {
            for (int i = 0; i < towers.Count; i++)
            {
                TowerState tower = towers[i];
                CompiledTowerDefinition definition = content.GetTower(tower.DefinitionId);
                switch (definition.Trigger)
                {
                    case TowerTrigger.Attack:
                        ProcessAttackTower(tower, definition);
                        break;
                    case TowerTrigger.EnemyEnteredRange:
                        ProcessRangeEntryTower(tower, definition);
                        break;
                }
            }
        }

        /// <summary>
        /// 일반 공격형 타워의 쿨다운과 타깃 선정을 처리하고, 투사체 카드 프로그램의 첫 카드를 예약한다.
        /// 카드 효과는 여기서 한꺼번에 실행하지 않고 EventQueue를 통해 순서대로 실행된다.
        /// </summary>
        private void ProcessAttackTower(
            TowerState tower,
            CompiledTowerDefinition definition)
        {
            if (tower.CooldownRemaining > 0)
            {
                tower.CooldownRemaining--;
                return;
            }

            // 선택 규칙은 표식 우선, 거리, 경로 진행도, EntityId 순으로 고정되어 동률도 결정적이다.
            EnemyState target = SelectTowerTarget(tower.Position, definition.RangeMilli);
            if (target == null)
            {
                return;
            }

            // RootChain은 이 한 번의 공격에서 파생되는 분열·폭발 등을 같은 인과관계로 묶어 예산을 센다.
            // ActivationId는 같은 체인 안에서도 각각의 실제 발동을 안정적으로 구별한다.
            ChainId chainId = CreateRootChain();
            ActivationId activationId = CreateActivation();
            ProjectileState projectile = SpawnProjectile(
                tower,
                definition,
                target.Id,
                chainId,
                activationId);
            if (projectile != null && tower.Program.Length > 0)
            {
                // 카드 배열의 0번부터 시작한다. 이후 카드는 앞 카드가 끝날 때 다음 이벤트로 이어진다.
                EnqueueProgram(
                    SubjectType.Projectile,
                    projectile.Id,
                    tower.Id,
                    0,
                    chainId,
                    activationId,
                    EventId.Invalid,
                    0,
                    EventPhase.Tower);
            }

            tower.CooldownRemaining = Math.Max(1, definition.CooldownTicks);
        }

        /// <summary>
        /// 사거리 진입형 타워가 이번 틱에 새로 들어온 적만 감지해 적 해석 카드 프로그램을 실행한다.
        /// 계속 범위 안에 서 있는 적에게 매 틱 반복 발동하지 않도록 이전 틱의 내부 대상 집합을 보관한다.
        /// </summary>
        private void ProcessRangeEntryTower(
            TowerState tower,
            CompiledTowerDefinition definition)
        {
            var currentlyInside = new List<int>();
            spatialIndex.Query(tower.Position, definition.RangeMilli, spatialScratch);
            for (int i = 0; i < spatialScratch.Count; i++)
            {
                EnemyState enemy = FindEnemy(spatialScratch[i]);
                if (!enemy.Alive ||
                    !PathModel.IsWithin(tower.Position, enemy.Position, definition.RangeMilli))
                {
                    continue;
                }

                currentlyInside.Add(enemy.Id.Value);
                bool wasInside = tower.TargetsInside.Contains(enemy.Id.Value);
                // 한 번 나갔다 다시 들어오더라도 개별 적 재발동 대기시간을 만족해야 한다.
                tower.LastTargetTriggerTick.TryGetValue(
                    enemy.Id.Value,
                    out long lastTriggered);
                bool cooldownReady =
                    !tower.LastTargetTriggerTick.ContainsKey(enemy.Id.Value) ||
                    tick - lastTriggered >= definition.PerTargetCooldownTicks;
                if (!wasInside && cooldownReady && tower.Program.Length > 0)
                {
                    ChainId chainId = CreateRootChain();
                    ActivationId activationId = CreateActivation();
                    EnqueueProgram(
                        SubjectType.Enemy,
                        enemy.Id,
                        tower.Id,
                        0,
                        chainId,
                        activationId,
                        EventId.Invalid,
                        0,
                        EventPhase.Tower);
                    tower.LastTargetTriggerTick[enemy.Id.Value] = tick;
                }
            }

            // 판정이 모두 끝난 뒤 한 번에 교체해야 순회 도중의 변경이 다른 적의 진입 판정에 영향을 주지 않는다.
            tower.TargetsInside.Clear();
            for (int i = 0; i < currentlyInside.Count; i++)
            {
                tower.TargetsInside.Add(currentlyInside[i]);
            }
        }

        /// <summary>
        /// 타워의 기본 수치로 논리 투사체를 만든다.
        /// 실제 생성 전에 해당 RootChain의 투사체 생성 예산을 예약하므로, 한도를 넘긴 효과는
        /// 상태를 절반만 바꾸지 않고 투사체 생성 전체가 거절된다.
        /// </summary>
        private ProjectileState SpawnProjectile(
            TowerState tower,
            CompiledTowerDefinition definition,
            EntityId targetId,
            ChainId chainId,
            ActivationId activationId)
        {
            if (!chainBudgets.TryGetValue(chainId.Value, out ChainBudget budget))
            {
                return null;
            }

            // 이 단계에는 아직 카드가 실행되지 않았으므로 깊이·이벤트 수가 아니라 생성 수 1만 예약한다.
            var reservation = new ChainReservation(
                depth: 0,
                eventCount: 0,
                projectileSpawnCount: 1);
            if (!budget.TryReserve(in reservation, out BudgetFailure failure))
            {
                GameEvent diagnosticEvent = CreateDiagnosticEvent(
                    EventType.ProjectileSpawned,
                    chainId,
                    tower.Id,
                    CardId.Invalid,
                    EntityId.Invalid,
                    SubjectType.Projectile);
                AddBudgetDiagnostic(failure, diagnosticEvent);
                return null;
            }

            // 수명은 콘텐츠 값과 전역 안전 상한 중 작은 값을 사용해 영구 투사체가 남지 않게 한다.
            var projectile = new ProjectileState
            {
                Id = new EntityId(nextEntityId++),
                SourceTowerId = tower.Id,
                Generation = 0,
                Position = tower.Position,
                TargetId = targetId,
                DamageMilli = definition.BaseDamageMilli,
                SpeedMilliPerTick = definition.ProjectileSpeedMilliPerTick,
                LifetimeRemaining = Math.Min(
                    definition.ProjectileLifetimeTicks,
                    content.Safety.MaxProjectileLifetimeTicks),
                RootChainId = chainId,
                ActivationId = activationId,
                LastTrailPosition = tower.Position
            };
            EnemyState target = FindEnemy(targetId);
            SetProjectileDirection(
                projectile,
                target == null ? tower.Position : target.Position);
            projectiles.Add(projectile);
            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                projectile.Id.Value,
                tower.Id.Value,
                (int)Math.Min(int.MaxValue, projectile.DamageMilli));
            return projectile;
        }

        /// <summary>
        /// 살아 있는 모든 투사체를 한 틱 이동시키고, 이동 선분과 처음 부딪힌 적의 충돌 이벤트를 예약한다.
        /// 한 프레임의 시작점과 끝점 사이를 선분으로 검사하므로 빠른 투사체가 적을 건너뛰는 현상을 막는다.
        /// </summary>
        private void MoveProjectiles()
        {
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState projectile = projectiles[i];
                // 소멸 이벤트가 이미 예약된 투사체는 그 사이에 추가 이동하거나 중복 충돌하지 않는다.
                if (!projectile.Alive || projectile.ExpirationQueued)
                {
                    continue;
                }

                projectile.LifetimeRemaining--;
                if (projectile.LifetimeRemaining <= 0)
                {
                    // 소멸 시 폭발 같은 바인딩도 이벤트 순서 안에서 처리되어야 하므로 즉시 삭제하지 않는다.
                    ScheduleProjectileExpiration(projectile, EventId.Invalid);
                    continue;
                }

                if (projectile.Homing)
                {
                    // 기존 표적이 죽었거나 이미 맞은 대상이면 같은 결정적 우선순위로 새 표적을 고른다.
                    EnemyState homingTarget =
                        FindEnemy(projectile.TargetId);
                    if (homingTarget == null ||
                        !homingTarget.Alive ||
                        projectile.HitEnemies.Contains(
                            homingTarget.Id.Value))
                    {
                        homingTarget =
                            SelectProjectileTarget(projectile);
                    }

                    if (homingTarget != null)
                    {
                        projectile.TargetId = homingTarget.Id;
                        SetProjectileDirection(
                            projectile,
                            homingTarget.Position);
                    }
                }

                // 방향과 속도는 모두 정수 단위다. DirectionXBps/YBps는 정규화된 방향을 10,000 기준으로 저장한다.
                SimPosition previous = projectile.Position;
                long moveX = DeterministicMath.MultiplyDivide(
                    projectile.DirectionXBps,
                    projectile.SpeedMilliPerTick,
                    10000);
                long moveY = DeterministicMath.MultiplyDivide(
                    projectile.DirectionYBps,
                    projectile.SpeedMilliPerTick,
                    10000);
                projectile.Position = SimPosition.FromMilliUnits(
                    projectile.Position.X.MilliUnits + moveX,
                    projectile.Position.Y.MilliUnits + moveY);
                SpawnBurnTrailIfNeeded(projectile, previous);
                AddPresentation(
                    PresentationEventType.ProjectileMoved,
                    projectile.Id.Value,
                    projectile.SourceTowerId.Value,
                    projectile.SpeedMilliPerTick);

                EnemyState target = FindFirstProjectileCollision(
                    projectile,
                    previous,
                    projectile.Position);
                if (target != null)
                {
                    // 충돌 즉시 체력을 깎지 않는다. Projectile 단계의 이벤트로 넣어 카드 바인딩과
                    // 피해·사망 단계의 전체 순서를 한 곳에서 통제하고 재귀 호출을 피한다.
                    var hitEvent = new GameEvent(
                        tick,
                        EventPhase.Projectile,
                        EventType.ProjectileHit,
                        projectile.RootChainId,
                        EventId.Invalid,
                        projectile.ActivationId,
                        projectile.SourceTowerId,
                        CardId.Invalid,
                        projectile.Id,
                        target.Id,
                        SubjectType.Enemy,
                        0,
                        projectile.Generation,
                        EventTags.Projectile | EventTags.SingleTarget,
                        RewardOrigin.EnemyDrop);
                    TryEnqueue(in hitEvent, out _);
                }
            }
        }

        /// <summary>
        /// 예약된 투사체 적중 하나를 확정한다.
        /// 기본 피해 요청, 적중 바인딩, 관통 또는 소멸 예약의 순서로 처리한다.
        /// </summary>
        private void ProcessProjectileHitEvent(in GameEvent gameEvent)
        {
            ProjectileState projectile = FindProjectile(gameEvent.SourceEntityId);
            EnemyState target = FindEnemy(gameEvent.SubjectEntityId);
            // 같은 적을 이미 맞았거나 어느 한쪽이 사라졌다면 오래된 예약 이벤트이므로 무시한다.
            if (projectile == null ||
                target == null ||
                !projectile.Alive ||
                !target.Alive ||
                projectile.HitEnemies.Contains(target.Id.Value))
            {
                return;
            }

            projectile.HitEnemies.Add(target.Id.Value);
            // 치명타 난수는 전투 전용 PCG 스트림에서 뽑으며, 결과 피해는 이후 방어 계산 이벤트로 넘긴다.
            bool critical = projectile.CriticalChanceBps > 0 &&
                            combatRandom.NextBasisPoints() < projectile.CriticalChanceBps;
            long damage = critical
                ? DeterministicMath.MultiplyBasisPoints(
                    projectile.DamageMilli,
                    run.CriticalDamageBps)
                : projectile.DamageMilli;
            int piercedArmorIgnoreBps =
                GetPiercedArmorIgnoreBps(target);
            EnqueueDamage(
                target.Id,
                projectile.SourceTowerId,
                CardId.Invalid,
                projectile.Id,
                damage,
                DamageKind.Physical,
                piercedArmorIgnoreBps,
                projectile.RootChainId,
                projectile.ActivationId,
                gameEvent.EventId,
                0,
                EventTags.Projectile |
                EventTags.SingleTarget |
                (critical ? EventTags.Critical : EventTags.None));

            // 화상·중독·폭발 등은 카드를 읽을 때 투사체에 '적중 시 바인딩'으로 붙여 두었다가 여기서 발동한다.
            // FirstHit 계열은 Used를 기록해 관통 투사체라도 한 번만 발동한다.
            for (int i = 0; i < projectile.Bindings.Count; i++)
            {
                EffectBinding binding = projectile.Bindings[i];
                if (binding.Trigger == BindingTrigger.OnHit ||
                    ((binding.Trigger == BindingTrigger.OnFirstHitOrExpire ||
                      binding.Trigger == BindingTrigger.OnFirstHit) &&
                     !binding.Used))
                {
                    ExecuteProjectileBinding(projectile, target, binding, gameEvent);
                    if (binding.Trigger != BindingTrigger.OnHit)
                    {
                        binding.Used = true;
                    }
                }
            }

            AddPresentation(
                PresentationEventType.ProjectileHit,
                target.Id.Value,
                projectile.Id.Value,
                (int)Math.Min(int.MaxValue, damage));

            // 천공 상태인 적은 투사체의 개인 관통 횟수를 소비하지 않고 통과시킨다.
            // 두 경우 모두 전체 안전 상한(MaxPiercesPerProjectile)은 지켜 실제 무한 관통을 막는다.
            bool forcedPierce = HasActiveStatus(target, StatusType.Pierced);
            if ((forcedPierce || projectile.PierceRemaining > 0) &&
                projectile.PiercesUsed <
                content.Safety.MaxPiercesPerProjectile)
            {
                if (!forcedPierce)
                {
                    projectile.PierceRemaining--;
                }

                projectile.PiercesUsed++;
                // 관통할 때마다 이후 적에게 줄 기본 피해를 감쇠한다.
                projectile.DamageMilli = DeterministicMath.MultiplyBasisPoints(
                    projectile.DamageMilli,
                    projectile.PierceDamageMultiplierBps);
                EnemyState nextTarget = SelectProjectileTarget(projectile);
                if (projectile.Homing && nextTarget != null)
                {
                    projectile.TargetId = nextTarget.Id;
                    SetProjectileDirection(
                        projectile,
                        nextTarget.Position);
                    return;
                }

                return;
            }

            ScheduleProjectileExpiration(projectile, gameEvent.EventId);
        }

        /// <summary>
        /// 투사체 소멸을 중복 없이 이벤트로 예약한다.
        /// 안전 예산 때문에 큐 등록이 실패하면 최소한 Alive를 꺼서 유령 투사체가 계속 남는 것은 방지한다.
        /// </summary>
        private void ScheduleProjectileExpiration(
            ProjectileState projectile,
            EventId parentEventId)
        {
            if (!projectile.Alive || projectile.ExpirationQueued)
            {
                return;
            }

            projectile.ExpirationQueued = true;
            var expiration = new GameEvent(
                tick,
                EventPhase.Projectile,
                EventType.ProjectileExpired,
                projectile.RootChainId,
                parentEventId,
                projectile.ActivationId,
                projectile.SourceTowerId,
                CardId.Invalid,
                projectile.Id,
                projectile.Id,
                SubjectType.Projectile,
                0,
                projectile.Generation,
                EventTags.Projectile,
                RewardOrigin.EnemyDrop);
            if (!TryEnqueue(in expiration, out _))
            {
                projectile.Alive = false;
            }
        }

        /// <summary>
        /// 투사체가 실제로 소멸할 때 아직 쓰이지 않은 '첫 적중 또는 소멸 시' 효과를 실행한다.
        /// 대표적으로 아무 적도 맞히지 못한 폭발 탄환이 수명 종료 지점에서 폭발할 수 있다.
        /// </summary>
        private void ProcessProjectileExpiredEvent(in GameEvent gameEvent)
        {
            ProjectileState projectile = FindProjectile(gameEvent.SourceEntityId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            for (int i = 0; i < projectile.Bindings.Count; i++)
            {
                EffectBinding binding = projectile.Bindings[i];
                if (binding.Trigger == BindingTrigger.OnFirstHitOrExpire && !binding.Used)
                {
                    ExecuteExplosion(
                        projectile.Position,
                        projectile.DamageMilli,
                        projectile.SourceTowerId,
                        binding.CardId,
                        projectile.Id,
                        binding.Node,
                        projectile.RootChainId,
                        projectile.ActivationId,
                        gameEvent.EventId,
                        1);
                    binding.Used = true;
                }
            }

            projectile.Alive = false;
            AddPresentation(
                PresentationEventType.ProjectileExpired,
                projectile.Id.Value,
                projectile.SourceTowerId.Value);
        }

        /// <summary>
        /// 타워의 사거리 안에서 공격 대상을 고른다.
        /// 표식이 있는 적을 먼저 보고, 같은 표식 상태끼리는 공통 결정적 우선순위를 사용한다.
        /// </summary>
        private EnemyState SelectTowerTarget(SimPosition origin, int rangeMilli)
        {
            EnemyState selected = null;
            bool selectedMarked = false;
            spatialIndex.Query(origin, rangeMilli, spatialScratch);
            for (int i = 0; i < spatialScratch.Count; i++)
            {
                EnemyState enemy = FindEnemy(spatialScratch[i]);
                if (!enemy.Alive || !PathModel.IsWithin(origin, enemy.Position, rangeMilli))
                {
                    continue;
                }

                bool marked = HasActiveStatus(enemy, StatusType.Mark);
                if (selected == null ||
                    (marked && !selectedMarked) ||
                    (marked == selectedMarked &&
                     CompareTargetPriority(
                         origin,
                         enemy,
                         selected) < 0))
                {
                    selected = enemy;
                    selectedMarked = marked;
                }
            }

            return selected;
        }

        /// <summary>
        /// 유도 또는 관통 투사체의 다음 표적을 고른다. 이미 맞힌 적은 다시 선택하지 않는다.
        /// </summary>
        private EnemyState SelectProjectileTarget(ProjectileState projectile)
        {
            EnemyState selected = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (!enemy.Alive || projectile.HitEnemies.Contains(enemy.Id.Value))
                {
                    continue;
                }

                if (selected == null ||
                    CompareTargetPriority(
                        projectile.Position,
                        enemy,
                        selected) < 0)
                {
                    selected = enemy;
                }
            }

            return selected;
        }

        /// <summary>
        /// 모든 자동 타깃 선택이 공유하는 동률 해소 규칙이다.
        /// 1) 기준점과 가까운 적, 2) 본진에 더 가까이 진행한 적, 3) 더 작은 EntityId 순이다.
        /// 마지막 ID 비교까지 고정해야 플랫폼이나 컬렉션 순서가 달라도 결과가 같다.
        /// 반환값이 음수이면 left가 더 우선이다.
        /// </summary>
        private static int CompareTargetPriority(
            SimPosition origin,
            EnemyState left,
            EnemyState right)
        {
            ulong leftDistance =
                origin.DistanceSquaredRaw(left.Position);
            ulong rightDistance =
                origin.DistanceSquaredRaw(right.Position);
            int distanceComparison =
                leftDistance.CompareTo(rightDistance);
            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            int progressComparison =
                right.PathProgressMilli.CompareTo(
                    left.PathProgressMilli);
            return progressComparison != 0
                ? progressComparison
                : left.Id.Value.CompareTo(right.Id.Value);
        }

        /// <summary>
        /// 투사체가 이번 틱 이동한 선분 위에서 가장 먼저 접촉하는 적을 찾는다.
        /// 동시에 닿는 경우 경로 진행도와 EntityId로 순서를 고정한다.
        /// </summary>
        private EnemyState FindFirstProjectileCollision(
            ProjectileState projectile,
            SimPosition start,
            SimPosition end)
        {
            EnemyState selected = null;
            long selectedProjection = long.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (!enemy.Alive ||
                    enemy.DeathQueued ||
                    projectile.HitEnemies.Contains(enemy.Id.Value))
                {
                    continue;
                }

                int collisionRadius = checked(
                    projectile.RadiusMilli +
                    GetEnemyHitRadiusMilli(enemy));
                if (!SegmentIntersectsCircle(
                        start,
                        end,
                        enemy.Position,
                        collisionRadius,
                        out long projection))
                {
                    continue;
                }

                if (selected == null ||
                    // projection은 이동 선분 시작점에서 접촉 지점까지의 상대 순서를 나타낸다.
                    projection < selectedProjection ||
                    (projection == selectedProjection &&
                     (enemy.PathProgressMilli >
                      selected.PathProgressMilli ||
                      (enemy.PathProgressMilli ==
                       selected.PathProgressMilli &&
                       enemy.Id.Value < selected.Id.Value))))
                {
                    selected = enemy;
                    selectedProjection = projection;
                }
            }

            return selected;
        }

        /// <summary>
        /// 적의 크기 배율을 논리 충돌 반지름으로 바꾼다. 최소 1을 보장해 축소된 적도 충돌 가능하다.
        /// </summary>
        private int GetEnemyHitRadiusMilli(EnemyState enemy)
        {
            return Math.Max(
                1,
                (int)DeterministicMath.MultiplyBasisPoints(
                    run.EnemyBaseHitRadiusMilli,
                    enemy.SizeMultiplierBps));
        }

        /// <summary>
        /// 선분에서 원의 중심에 가장 가까운 점을 정수 연산으로 구해 충돌 여부를 판정한다.
        /// Unity 물리엔진 대신 사용하는 순수 논리 충돌 검사이며, projection은 선분 위 충돌 우선순위에 쓰인다.
        /// </summary>
        private static bool SegmentIntersectsCircle(
            SimPosition start,
            SimPosition end,
            SimPosition center,
            int radiusMilli,
            out long projection)
        {
            long startX = start.X.MilliUnits;
            long startY = start.Y.MilliUnits;
            long vx = end.X.MilliUnits - startX;
            long vy = end.Y.MilliUnits - startY;
            long wx = center.X.MilliUnits - startX;
            long wy = center.Y.MilliUnits - startY;
            long lengthSquared = checked(vx * vx + vy * vy);
            if (lengthSquared <= 0)
            {
                projection = 0;
                return PathModel.IsWithin(
                    start,
                    center,
                    radiusMilli);
            }

            long dot = checked(wx * vx + wy * vy);
            projection = Math.Max(0, Math.Min(lengthSquared, dot));
            long closestX = startX + checked(vx * projection) /
                            lengthSquared;
            long closestY = startY + checked(vy * projection) /
                            lengthSquared;
            long dx = center.X.MilliUnits - closestX;
            long dy = center.Y.MilliUnits - closestY;
            long radiusSquared = checked(
                (long)radiusMilli * radiusMilli);
            return checked(dx * dx + dy * dy) <= radiusSquared;
        }

        /// <summary>
        /// 현재 위치에서 목표 위치를 향하는 방향을 basis point 벡터로 저장한다.
        /// 목표와 같은 위치일 때는 정의되지 않은 0벡터 대신 오른쪽 방향을 기본값으로 사용한다.
        /// </summary>
        private static void SetProjectileDirection(
            ProjectileState projectile,
            SimPosition target)
        {
            long dx =
                target.X.MilliUnits - projectile.Position.X.MilliUnits;
            long dy =
                target.Y.MilliUnits - projectile.Position.Y.MilliUnits;
            long distance = PathModel.DistanceMilli(
                projectile.Position,
                target);
            if (distance <= 0)
            {
                projectile.DirectionXBps = 10000;
                projectile.DirectionYBps = 0;
                return;
            }

            projectile.DirectionXBps = (int)
                DeterministicMath.MultiplyDivide(
                    dx,
                    10000,
                    (int)Math.Min(int.MaxValue, distance));
            projectile.DirectionYBps = (int)
                DeterministicMath.MultiplyDivide(
                    dy,
                    10000,
                    (int)Math.Min(int.MaxValue, distance));
        }

        /// <summary>
        /// 한 적에게 여러 천공 상태가 있으면 가장 높은 방어 무시 비율만 사용하고 100%로 제한한다.
        /// </summary>
        private static int GetPiercedArmorIgnoreBps(EnemyState enemy)
        {
            int result = 0;
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type == StatusType.Pierced &&
                    status.RemainingTicks > 0)
                {
                    result = Math.Max(result, status.ArmorIgnoreBps);
                }
            }

            return Math.Min(10000, Math.Max(0, result));
        }

        /// <summary>
        /// 틱의 모든 사망·소멸 이벤트가 끝난 뒤 비활성 개체를 목록에서 제거한다.
        /// 뒤에서 앞으로 지워야 인덱스가 당겨져 다음 항목을 건너뛰지 않는다.
        /// EntityId는 재사용하지 않으므로 목록 위치가 바뀌어도 리플레이의 개체 정체성은 유지된다.
        /// </summary>
        private void CleanupDeadEntities()
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                if (!projectiles[i].Alive)
                {
                    projectiles.RemoveAt(i);
                }
            }

            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (!enemies[i].Alive)
                {
                    enemies.RemoveAt(i);
                }
            }

            for (int i = hazards.Count - 1; i >= 0; i--)
            {
                if (hazards[i].RemainingTicks <= 0)
                {
                    hazards.RemoveAt(i);
                }
            }
        }

        // 다음 조회 함수들은 Unity 오브젝트 참조 대신 안정적인 정수 ID로 논리 개체를 찾는다.
        private TowerState FindTower(TowerId id)
        {
            if (id.Value < 0 || id.Value >= towers.Count)
            {
                return null;
            }

            TowerState tower = towers[id.Value];
            return tower.Id == id ? tower : null;
        }

        private EnemyState FindEnemy(EntityId id)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].Id == id)
                {
                    return enemies[i];
                }
            }

            return null;
        }

        private ProjectileState FindProjectile(EntityId id)
        {
            for (int i = 0; i < projectiles.Count; i++)
            {
                if (projectiles[i].Id == id)
                {
                    return projectiles[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 안전 한도 초과 기록에 필요한 최소 문맥을 GameEvent 모양으로 만든다.
        /// 실제 실행 큐에 넣는 이벤트가 아니라, 어떤 체인·타워·카드·대상에서 거절됐는지 남기기 위한 자료다.
        /// </summary>
        private GameEvent CreateDiagnosticEvent(
            EventType eventType,
            ChainId chainId,
            TowerId towerId,
            CardId cardId,
            EntityId subjectId,
            SubjectType subjectType)
        {
            return new GameEvent(
                tick,
                EventPhase.Presentation,
                eventType,
                chainId,
                EventId.Invalid,
                ActivationId.Invalid,
                towerId,
                cardId,
                EntityId.Invalid,
                subjectId,
                subjectType,
                0,
                0,
                EventTags.None,
                RewardOrigin.Debug);
        }
    }
}
