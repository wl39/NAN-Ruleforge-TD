using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    // 이 파일은 전투에서 생긴 모든 파생 결과를 '이벤트'라는 작업 단위로 순서화한다.
    // 카드가 다른 카드를 즉시 호출하는 재귀 구조 대신 큐에 등록하면, 폭발→사망→데스 엔진처럼
    // 긴 연쇄도 정해진 단계와 등록 순서로 처리할 수 있고 안전 한도를 일관되게 적용할 수 있다.
    public sealed partial class GameSimulation
    {
        /// <summary>
        /// 하나의 최초 원인(타워 공격, 상태 틱 등)에서 시작되는 새 연쇄 ID와 전용 안전 예산을 만든다.
        /// 이후 분열·폭발·사망 효과는 이 RootChain을 이어받아 연쇄 전체 사용량을 공유한다.
        /// </summary>
        private ChainId CreateRootChain()
        {
            var id = new ChainId(nextChainId++);
            chainBudgets.Add(id.Value, new ChainBudget(id, content.Safety));
            return id;
        }

        /// <summary>
        /// 같은 RootChain 안에서 개별 카드 프로그램 발동을 구별하는 안정적인 순번을 만든다.
        /// </summary>
        private ActivationId CreateActivation()
        {
            return new ActivationId(nextActivationId++);
        }

        /// <summary>
        /// 이벤트 하나에 필요한 틱·체인·큐·카드 실행 예산을 먼저 예약하고 큐에 넣는다.
        /// 예약에 실패하면 큐나 게임 상태 어느 쪽에도 일부만 반영하지 않는다.
        /// </summary>
        private bool TryEnqueue(in GameEvent gameEvent, out GameEvent scheduled)
        {
            int cardTriggerCount =
                gameEvent.EventType == EventType.CardExecute ? 1 : 0;
            if (!TryReserveComposite(
                    in gameEvent,
                    chainEventCount: 1,
                    queueSlotCount: 1,
                    projectileSpawnCount: 0,
                    cardTriggerCount: cardTriggerCount))
            {
                scheduled = default(GameEvent);
                return false;
            }

            return EnqueueReserved(in gameEvent, out scheduled);
        }

        /// <summary>
        /// 폭발의 모든 대상이나 충돌한 두 적처럼 함께 성립해야 하는 이벤트 묶음을 원자적으로 등록한다.
        /// 전체 묶음이 같은 RootChain인지 확인하고 총 필요량을 한 번에 예약한 뒤에만 개별 이벤트를 넣는다.
        /// </summary>
        private bool TryEnqueueBatch(IReadOnlyList<GameEvent> gameEvents)
        {
            if (gameEvents == null || gameEvents.Count == 0)
            {
                return true;
            }

            GameEvent diagnosticEvent = gameEvents[0];
            int cardTriggerCount = 0;
            int maximumDepth = diagnosticEvent.Depth;
            for (int i = 0; i < gameEvents.Count; i++)
            {
                GameEvent gameEvent = gameEvents[i];
                // 서로 다른 인과 연쇄의 예산을 한 배치로 섞으면 어느 체인에 비용을 부과할지 모호해진다.
                if (gameEvent.RootChainId != diagnosticEvent.RootChainId)
                {
                    throw new InvalidOperationException(
                        "An atomic event batch must share one root chain.");
                }

                maximumDepth = Math.Max(maximumDepth, gameEvent.Depth);
                if (gameEvent.EventType == EventType.CardExecute)
                {
                    cardTriggerCount++;
                }
            }

            // 배치 중 가장 깊은 이벤트를 기준으로 체인 깊이 제한도 함께 검사한다.
            diagnosticEvent = WithDiagnosticDepth(
                in diagnosticEvent,
                maximumDepth);
            if (!TryReserveComposite(
                    in diagnosticEvent,
                    chainEventCount: gameEvents.Count,
                    queueSlotCount: gameEvents.Count,
                    projectileSpawnCount: 0,
                    cardTriggerCount: cardTriggerCount))
            {
                return false;
            }

            // 앞에서 전체 슬롯을 검증했으므로 여기서 일부만 실패하는 것은 내부 불변식 위반이다.
            for (int i = 0; i < gameEvents.Count; i++)
            {
                GameEvent gameEvent = gameEvents[i];
                if (!EnqueueReserved(in gameEvent, out _))
                {
                    throw new InvalidOperationException(
                        "Atomic event batch lost a reserved queue slot.");
                }
            }

            return true;
        }

        /// <summary>
        /// 하나의 효과가 앞으로 만들 모든 작업 비용을 실제 상태 변경 전에 한꺼번에 검사·예약한다.
        /// 검사 순서는 음수 요청, 틱 전체 한도, 큐 용량, RootChain 한도이며 전부 통과해야 사용량이 증가한다.
        /// 단일 스레드 시뮬레이션 안에서 검사와 등록 사이에 다른 작업이 끼지 않으므로 이 예약은 원자적이다.
        /// </summary>
        private bool TryReserveComposite(
            in GameEvent diagnosticEvent,
            int chainEventCount,
            int queueSlotCount,
            int projectileSpawnCount,
            int cardTriggerCount)
        {
            if (chainEventCount < 0 ||
                queueSlotCount < 0 ||
                projectileSpawnCount < 0 ||
                cardTriggerCount < 0)
            {
                AddDiagnostic(
                    DiagnosticCode.InvalidEvent,
                    diagnosticEvent,
                    chainEventCount);
                return false;
            }

            // 한 틱 전체 이벤트 예산은 여러 RootChain이 동시에 폭주하는 경우까지 막는 최종 방어선이다.
            if (chainEventCount >
                content.Safety.MaxEventsPerTick -
                eventsEnqueuedThisTick)
            {
                AddDiagnostic(
                    DiagnosticCode.TickEventBudgetExceeded,
                    diagnosticEvent,
                    eventsEnqueuedThisTick);
                return false;
            }

            // 물리 큐 슬롯도 미리 확인해 체인 예산만 소비하고 이벤트는 못 넣는 상황을 막는다.
            if (!eventQueue.CanEnqueueCount(queueSlotCount))
            {
                AddDiagnostic(
                    DiagnosticCode.EventQueueCapacityExceeded,
                    diagnosticEvent,
                    eventQueue.Count);
                return false;
            }

            // 진단 또는 복구 경로에서 처음 본 체인도 동일한 안전 설정으로 예산 장부를 만든다.
            if (!chainBudgets.TryGetValue(
                    diagnosticEvent.RootChainId.Value,
                    out ChainBudget budget))
            {
                budget = new ChainBudget(
                    diagnosticEvent.RootChainId,
                    content.Safety);
                chainBudgets.Add(
                    diagnosticEvent.RootChainId.Value,
                    budget);
            }

            var reservation = new ChainReservation(
                diagnosticEvent.Depth,
                chainEventCount,
                projectileSpawnCount,
                cardTriggerCount);
            if (!budget.TryReserve(
                    in reservation,
                    out BudgetFailure failure))
            {
                AddBudgetDiagnostic(failure, diagnosticEvent);
                return false;
            }

            // 모든 검사가 통과한 마지막 시점에만 틱 사용량을 확정한다.
            eventsEnqueuedThisTick += chainEventCount;
            return true;
        }

        /// <summary>
        /// 이미 예산을 확보한 이벤트를 중복 예약 없이 큐에 넣는다.
        /// 분열 continuation이나 원자적 배치처럼 사전 예약된 경로에서만 사용한다.
        /// </summary>
        private bool EnqueueReserved(
            in GameEvent gameEvent,
            out GameEvent scheduled)
        {
            if (!eventQueue.TryEnqueue(in gameEvent, out scheduled))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 실제 실행 내용은 그대로 두고 진단에 기록할 체인 깊이만 교체한 복사본을 만든다.
        /// </summary>
        private static GameEvent WithDiagnosticDepth(
            in GameEvent gameEvent,
            int depth)
        {
            return new GameEvent(
                gameEvent.SimulationTick,
                gameEvent.Phase,
                gameEvent.EventType,
                gameEvent.RootChainId,
                gameEvent.ParentEventId,
                gameEvent.ActivationId,
                gameEvent.SourceTowerId,
                gameEvent.SourceCardId,
                gameEvent.SourceEntityId,
                gameEvent.SubjectEntityId,
                gameEvent.SubjectType,
                depth,
                gameEvent.Generation,
                gameEvent.Tags,
                gameEvent.RewardOrigin,
                gameEvent.PayloadA,
                gameEvent.PayloadB,
                gameEvent.PayloadC,
                gameEvent.PayloadValue);
        }

        /// <summary>
        /// 현재 틱에서 지정한 단계까지 도달한 이벤트만 순서대로 꺼내 처리한다.
        /// EventQueue의 정렬 기준은 SimulationTick → EventPhase → EnqueueSequence이므로
        /// 같은 틱의 카드·피해·사망·보상 순서와 같은 단계 안의 선후가 항상 고정된다.
        /// </summary>
        private void DrainEventsThrough(EventPhase maximumPhase)
        {
            while (eventQueue.TryPeek(out GameEvent next) &&
                   next.SimulationTick <= tick &&
                   (int)next.Phase <= (int)maximumPhase)
            {
                if (eventsProcessedThisTick >= content.Safety.MaxEventsPerTick)
                {
                    // 처리 중 새 이벤트가 계속 생겨도 한 틱의 상한에서 멈춰 브라우저 프레임을 보호한다.
                    AddDiagnostic(
                        DiagnosticCode.TickEventBudgetExceeded,
                        next,
                        eventsProcessedThisTick);
                    break;
                }

                eventQueue.TryDequeue(out GameEvent gameEvent);
                eventsProcessedThisTick++;
                ProcessEvent(gameEvent);
            }
        }

        /// <summary>
        /// 큐에서 꺼낸 이벤트 종류를 실제 처리 함수로 전달하는 중앙 분배기다.
        /// 새 이벤트 종류를 추가할 때 실행 진입점을 한곳에서 확인할 수 있다.
        /// </summary>
        private void ProcessEvent(in GameEvent gameEvent)
        {
            switch (gameEvent.EventType)
            {
                case EventType.CardExecute:
                    ProcessProgramEvent(gameEvent);
                    break;
                case EventType.ProjectileHit:
                    ProcessProjectileHitEvent(gameEvent);
                    break;
                case EventType.ProjectileExpired:
                    ProcessProjectileExpiredEvent(gameEvent);
                    break;
                case EventType.DamageRequested:
                    ProcessDamageEvent(gameEvent);
                    break;
                case EventType.EnemyDied:
                    ProcessEnemyDeathEvent(gameEvent);
                    break;
                case EventType.RewardGranted:
                    ProcessRewardEvent(gameEvent);
                    break;
            }
        }

        /// <summary>
        /// 타워에 장착된 카드 배열의 특정 위치를 실행할 CardExecute 이벤트를 새 예산으로 등록한다.
        /// 일반적인 첫 카드 또는 예약되지 않은 다음 카드 진행에 사용한다.
        /// </summary>
        private bool EnqueueProgram(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            int cardIndex,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            EventPhase phase)
        {
            TowerState tower = FindTower(towerId);
            if (tower == null || cardIndex < 0 || cardIndex >= tower.Program.Length)
            {
                return false;
            }

            if (!TryCreateProgramEvent(
                    subjectType,
                    subjectId,
                    towerId,
                    cardIndex,
                    rootChainId,
                    activationId,
                    parentEventId,
                    depth,
                    phase,
                    0,
                    out GameEvent gameEvent,
                    out int frameIndex))
            {
                return false;
            }

            if (!TryEnqueue(in gameEvent, out _))
            {
                ReleaseProgramFrame(frameIndex);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 분열 효과가 미리 확보해 둔 continuation 예산을 사용해 다음 카드 실행을 등록한다.
        /// 여기서는 예산을 다시 소비하지 않으며 남은 예약 수를 ProgramFrame에 전달한다.
        /// </summary>
        private bool EnqueueProgramReserved(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            int cardIndex,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            EventPhase phase,
            int reservedContinuationEvents)
        {
            if (!TryCreateProgramEvent(
                    subjectType,
                    subjectId,
                    towerId,
                    cardIndex,
                    rootChainId,
                    activationId,
                    parentEventId,
                    depth,
                    phase,
                    reservedContinuationEvents,
                    out GameEvent gameEvent,
                    out int frameIndex))
            {
                return false;
            }

            if (!EnqueueReserved(in gameEvent, out _))
            {
                ReleaseProgramFrame(frameIndex);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 카드 실행에 필요한 ProgramFrame을 별도 저장소에 만들고, 그 인덱스를 이벤트 payload에 넣는다.
        /// 이벤트 자체는 작고 고정된 구조를 유지하면서 대상·카드 위치·남은 예약 같은 실행 문맥을 보존한다.
        /// </summary>
        private bool TryCreateProgramEvent(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            int cardIndex,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            EventPhase phase,
            int reservedContinuationEvents,
            out GameEvent gameEvent,
            out int frameIndex)
        {
            TowerState tower = FindTower(towerId);
            if (tower == null ||
                cardIndex < 0 ||
                cardIndex >= tower.Program.Length)
            {
                gameEvent = default(GameEvent);
                frameIndex = -1;
                return false;
            }

            var frame = new ProgramFrame(
                subjectType,
                subjectId,
                towerId,
                cardIndex,
                rootChainId,
                activationId,
                parentEventId,
                depth,
                reservedContinuationEvents);
            frameIndex = AllocateProgramFrame(frame);
            CardId cardId = tower.Program[cardIndex];
            int generation = GetGeneration(subjectType, subjectId);
            gameEvent = new GameEvent(
                tick,
                phase,
                EventType.CardExecute,
                rootChainId,
                parentEventId,
                activationId,
                towerId,
                cardId,
                subjectId,
                subjectId,
                subjectType,
                depth,
                Math.Max(0, generation),
                subjectType == SubjectType.Projectile
                    ? EventTags.Projectile
                    : EventTags.Enemy,
                RewardOrigin.EnemyDrop,
                payloadA: frameIndex);

            return true;
        }

        /// <summary>
        /// CardExecute 이벤트 하나를 실행한다.
        /// 현재 타워와 대상을 다시 검증하고, 대상 타입에 따라 같은 카드의 ProjectileEffects 또는
        /// EnemyEffects를 선택한 뒤 노드를 정의된 순서대로 실행하고 오른쪽 카드로 이어 간다.
        /// </summary>
        private void ProcessProgramEvent(in GameEvent gameEvent)
        {
            int frameIndex = gameEvent.PayloadA;
            if (frameIndex < 0 || frameIndex >= programFrames.Count)
            {
                return;
            }

            // 이벤트에서 사용한 프레임 슬롯은 즉시 반환해 장시간 전투에서도 임시 객체가 계속 늘지 않게 한다.
            ProgramFrame frame = programFrames[frameIndex];
            ReleaseProgramFrame(frameIndex);
            TowerState tower = FindTower(frame.TowerId);
            if (tower == null ||
                frame.CardIndex < 0 ||
                frame.CardIndex >= tower.Program.Length ||
                !SubjectExists(frame.SubjectType, frame.SubjectId))
            {
                return;
            }

            CardId cardId = tower.Program[frame.CardIndex];
            int cardInstanceId = tower.ProgramInstances[frame.CardIndex];
            CompiledCardDefinition card = content.GetCard(cardId);
            // 타워가 정한 SubjectType이 동일 카드의 두 해석 중 어느 프로그램을 실행할지 결정한다.
            CompiledEffectNode[] nodes = frame.SubjectType == SubjectType.Projectile
                ? card.ProjectileEffectsInternal
                : card.EnemyEffectsInternal;
            var context = new EffectExecutionContext(
                frame.SubjectType,
                frame.SubjectId,
                frame.TowerId,
                cardId,
                cardInstanceId,
                frame.SubjectId,
                frame.RootChainId,
                frame.ActivationId,
                gameEvent.EventId,
                frame.Depth,
                tower.Program.Length - frame.CardIndex - 1,
                frame.ReservedContinuationEvents);

            EffectExecutionOutcome outcome = EffectExecutionOutcome.Continue();
            // 한 카드 안의 효과 노드는 데이터에 컴파일된 순서대로 실행된다.
            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                IEffectExecutor executor = effectRegistry.Get(nodes[nodeIndex].Operation);
                outcome = executor.Execute(this, in context, in nodes[nodeIndex]);
                if (outcome.SubjectReplaced)
                {
                    // 분열처럼 대상을 두 갈래로 만든 executor가 이후 continuation을 직접 설명하므로
                    // 같은 카드의 남은 노드를 중복 실행하지 않는다.
                    break;
                }
            }

            AddPresentation(
                PresentationEventType.CardExecuted,
                frame.SubjectId.Value,
                frame.TowerId.Value,
                frame.CardIndex,
                card.StableId);

            int nextCardIndex = frame.CardIndex + 1;
            if (nextCardIndex >= tower.Program.Length)
            {
                return;
            }

            if (outcome.SubjectReplaced &&
                outcome.AdditionalSubject.IsValid &&
                outcome.OriginalContinuationReservations > 0)
            {
                // 분열 전에 원본·자식 전체의 남은 카드 예산과 큐 슬롯을 이미 예약했다.
                // 두 가지를 함께 큐에 넣어 [분열 → 화상]이라면 양쪽 모두 반드시 화상까지 진행한다.
                bool originalQueued = EnqueueProgramReserved(
                    frame.SubjectType,
                    frame.SubjectId,
                    frame.TowerId,
                    nextCardIndex,
                    frame.RootChainId,
                    frame.ActivationId,
                    gameEvent.EventId,
                    frame.Depth,
                    gameEvent.Phase,
                    outcome.OriginalContinuationReservations - 1);
                bool childQueued = EnqueueProgramReserved(
                    frame.SubjectType,
                    outcome.AdditionalSubject,
                    frame.TowerId,
                    nextCardIndex,
                    frame.RootChainId,
                    frame.ActivationId,
                    gameEvent.EventId,
                    frame.Depth,
                    gameEvent.Phase,
                    outcome.AdditionalContinuationReservations - 1);
                if (!originalQueued || !childQueued)
                {
                    // 여기서 한쪽이라도 실패하면 사전 예약 계약이 깨진 프로그래밍 오류다.
                    throw new InvalidOperationException(
                        "A split continuation lost its atomic reservation.");
                }
                return;
            }

            if (frame.ReservedContinuationEvents > 0)
            {
                // 분열 뒤 각 가지가 다음 카드를 실행할 때마다 자기 몫의 예약 카운트를 하나씩 넘긴다.
                if (!EnqueueProgramReserved(
                        frame.SubjectType,
                        frame.SubjectId,
                        frame.TowerId,
                        nextCardIndex,
                        frame.RootChainId,
                        frame.ActivationId,
                        gameEvent.EventId,
                        frame.Depth,
                        gameEvent.Phase,
                        frame.ReservedContinuationEvents - 1))
                {
                    throw new InvalidOperationException(
                        "A card continuation lost its atomic reservation.");
                }
                return;
            }

            // 특별한 사전 예약이 없는 일반 카드 흐름은 다음 카드 하나의 예산을 새로 확보한다.
            EnqueueProgram(
                frame.SubjectType,
                frame.SubjectId,
                frame.TowerId,
                nextCardIndex,
                frame.RootChainId,
                frame.ActivationId,
                gameEvent.EventId,
                frame.Depth,
                gameEvent.Phase);
        }

        /// <summary>
        /// 피해 계산 요청을 만들고 이벤트 큐에 등록하는 편의 함수다.
        /// 체력은 이 자리에서 변경하지 않고 Damage 단계에서만 변경한다.
        /// </summary>
        private bool EnqueueDamage(
            EntityId targetId,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            long amountMilli,
            DamageKind damageKind,
            int armorIgnoreBps,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            EventTags tags)
        {
            if (!TryCreateDamageEvent(
                    targetId,
                    sourceTowerId,
                    sourceCardId,
                    sourceEntityId,
                    amountMilli,
                    damageKind,
                    armorIgnoreBps,
                    rootChainId,
                    activationId,
                    parentEventId,
                    depth,
                    tags,
                    out GameEvent request))
            {
                return false;
            }

            return TryEnqueue(in request, out _);
        }

        /// <summary>
        /// 대상 유효성을 확인하고 현재 상태·저항·방어력에 따른 최종 피해량을 확정해 GameEvent로 만든다.
        /// 폭발처럼 여러 피해를 한꺼번에 예약해야 하는 곳에서는 생성과 등록을 분리해 원자적 배치를 구성한다.
        /// </summary>
        private bool TryCreateDamageEvent(
            EntityId targetId,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            long amountMilli,
            DamageKind damageKind,
            int armorIgnoreBps,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            EventTags tags,
            out GameEvent request)
        {
            EnemyState target = FindEnemy(targetId);
            if (target == null || !target.Alive || amountMilli <= 0)
            {
                request = default(GameEvent);
                return false;
            }

            long resolvedAmount = CalculateDamage(
                target,
                amountMilli,
                damageKind,
                armorIgnoreBps,
                tags);
            request = new GameEvent(
                tick,
                EventPhase.Damage,
                EventType.DamageRequested,
                rootChainId,
                parentEventId,
                activationId,
                sourceTowerId,
                sourceCardId,
                sourceEntityId,
                targetId,
                SubjectType.Enemy,
                depth,
                Math.Max(0, GetGeneration(SubjectType.Enemy, targetId)),
                tags,
                RewardOrigin.EnemyDrop,
                payloadA: (int)damageKind,
                payloadB: armorIgnoreBps,
                payloadValue: resolvedAmount);
            return true;
        }

        /// <summary>
        /// Phase 1의 최종 피해량을 정수 연산으로 계산한다.
        /// 호출자가 전달한 기본 피해(투사체 치명타 등 반영 완료)
        /// → 표식 취약 → 단일/범위 취약 → 화염·독 저항 → 방어력과 방어 무시 순서다.
        /// 마지막에는 유효한 공격이 0 피해로 사라지지 않도록 최소 1을 보장한다.
        /// </summary>
        private long CalculateDamage(
            EnemyState enemy,
            long amount,
            DamageKind kind,
            int armorIgnoreBps,
            EventTags tags)
        {
            int markVulnerability = 0;
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type != StatusType.Mark)
                {
                    continue;
                }

                markVulnerability += status.Intensity * status.Stacks;
                if (status.Limit > 0)
                {
                    markVulnerability = Math.Min(
                        markVulnerability,
                        status.Limit);
                }
            }

            // 같은 적에게 적용된 표식 인스턴스들의 세기와 중첩을 합치되 각 상태의 Limit을 존중한다.
            amount = DeterministicMath.MultiplyBasisPoints(
                amount,
                10000 + Math.Max(0, markVulnerability));
            if ((tags & EventTags.Area) != 0)
            {
                amount = DeterministicMath.MultiplyBasisPoints(
                    amount,
                    enemy.AreaDamageTakenBps);
            }
            else if ((tags & EventTags.SingleTarget) != 0)
            {
                amount = DeterministicMath.MultiplyBasisPoints(
                    amount,
                    enemy.SingleDamageTakenBps);
            }

            CompiledEnemyDefinition definition =
                content.GetEnemy(enemy.DefinitionId);
            // 현재 Phase 1에서 속성 저항은 화염과 독을 명시적으로 처리한다.
            if (kind == DamageKind.Fire)
            {
                amount = DeterministicMath.MultiplyBasisPoints(
                    amount,
                    10000 - definition.FireResistanceBps);
            }
            else if (kind == DamageKind.Poison)
            {
                amount = DeterministicMath.MultiplyBasisPoints(
                    amount,
                    10000 - definition.PoisonResistanceBps);
            }

            int boundedArmorIgnore =
                Math.Max(0, Math.Min(10000, armorIgnoreBps));
            // 방어 무시는 방어력 자체를 줄인 뒤 100 / (100 + 유효 방어력) 공식을 적용한다.
            int effectiveArmor = (int)
                DeterministicMath.MultiplyBasisPoints(
                    Math.Max(0, enemy.Armor),
                    10000 - boundedArmorIgnore);
            amount = DeterministicMath.MultiplyDivide(
                amount,
                100,
                100 + effectiveArmor);
            return Math.Max(1, amount);
        }

        /// <summary>
        /// ProgramFrame 슬롯을 재사용하는 작은 풀이다.
        /// 카드 이벤트가 많아도 매번 새 관리 객체를 할당하지 않아 WebGL의 가비지 컬렉션 부담을 줄인다.
        /// </summary>
        private int AllocateProgramFrame(in ProgramFrame frame)
        {
            if (freeProgramFrames.Count > 0)
            {
                int reused = freeProgramFrames.Pop();
                programFrames[reused] = frame;
                return reused;
            }

            int index = programFrames.Count;
            programFrames.Add(frame);
            return index;
        }

        /// <summary>
        /// 사용이 끝난 ProgramFrame 인덱스를 재사용 목록으로 돌려놓는다.
        /// </summary>
        private void ReleaseProgramFrame(int frameIndex)
        {
            freeProgramFrames.Push(frameIndex);
        }

        /// <summary>
        /// 이벤트 기록과 안전 진단에 넣을 대상의 분열 세대를 가져온다.
        /// 대상이 이미 사라졌으면 안전한 기본값 0을 사용한다.
        /// </summary>
        private int GetGeneration(SubjectType subjectType, EntityId subjectId)
        {
            if (subjectType == SubjectType.Projectile)
            {
                ProjectileState projectile = FindProjectile(subjectId);
                return projectile == null ? 0 : projectile.Generation;
            }

            EnemyState enemy = FindEnemy(subjectId);
            return enemy == null ? 0 : enemy.Generation;
        }

        /// <summary>
        /// 예약된 카드 이벤트가 실행될 시점에도 대상이 살아 있는지 재확인한다.
        /// 큐에 기다리는 동안 다른 피해로 대상이 사라질 수 있기 때문이다.
        /// </summary>
        private bool SubjectExists(SubjectType subjectType, EntityId subjectId)
        {
            if (subjectType == SubjectType.Projectile)
            {
                ProjectileState projectile = FindProjectile(subjectId);
                return projectile != null && projectile.Alive;
            }

            EnemyState enemy = FindEnemy(subjectId);
            return enemy != null && enemy.Alive;
        }

        /// <summary>
        /// ChainBudget이 알려 준 실패 종류를 게임에서 읽을 수 있는 진단 코드로 변환한다.
        /// </summary>
        private void AddBudgetDiagnostic(BudgetFailure failure, in GameEvent gameEvent)
        {
            DiagnosticCode code;
            switch (failure)
            {
                case BudgetFailure.ChainDepthLimit:
                    code = DiagnosticCode.ChainDepthLimitReached;
                    break;
                case BudgetFailure.ProjectileSpawnLimit:
                    code = DiagnosticCode.ProjectileSpawnBudgetExceeded;
                    break;
                case BudgetFailure.CardTriggerLimit:
                    code = DiagnosticCode.CardTriggerLimitReached;
                    break;
                default:
                    code = DiagnosticCode.ChainEventBudgetExceeded;
                    break;
            }

            AddDiagnostic(code, gameEvent, (int)failure);
        }

        /// <summary>
        /// 안전장치로 거절된 효과의 틱·체인·타워·카드·대상을 진단 버퍼와 표현 이벤트에 남긴다.
        /// 게임 진행은 계속되며, 디버그 UI는 SafetyLimitReached를 구독해 어떤 조합이 상한에 닿았는지 보여줄 수 있다.
        /// </summary>
        private void AddDiagnostic(
            DiagnosticCode code,
            in GameEvent gameEvent,
            int detail)
        {
            diagnostics.Add(new DiagnosticRecord(
                tick,
                DiagnosticSeverity.Warning,
                code,
                gameEvent.EventType,
                gameEvent.RootChainId,
                gameEvent.SourceTowerId,
                gameEvent.SourceCardId,
                gameEvent.SubjectEntityId,
                detail));
            AddPresentation(
                PresentationEventType.SafetyLimitReached,
                gameEvent.SubjectEntityId.Value,
                gameEvent.SourceTowerId.Value,
                detail,
                code.ToString());
        }
    }
}
