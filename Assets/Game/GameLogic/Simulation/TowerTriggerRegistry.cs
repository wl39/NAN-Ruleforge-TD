using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 타워 핸들러가 고정 틱 순회와 사건 기반 순회 중 어느 쪽에서 호출되는지 구분한다.
    /// </summary>
    internal enum TowerTriggerDispatchKind
    {
        SimulationTick = 0,
        EnemyDied = 1
    }

    /// <summary>
    /// 타워 트리거 실행에 필요한 사건 자료다. 고정 틱 트리거는 사건 대상을 갖지 않고,
    /// 사망 트리거는 사망 적과 원본 GameEvent를 함께 전달한다.
    /// </summary>
    internal readonly struct TowerTriggerContext
    {
        private TowerTriggerContext(
            TowerTriggerDispatchKind dispatchKind,
            EnemyState eventEnemy,
            in GameEvent gameEvent)
        {
            DispatchKind = dispatchKind;
            EventEnemy = eventEnemy;
            GameEvent = gameEvent;
        }

        public TowerTriggerDispatchKind DispatchKind { get; }

        public EnemyState EventEnemy { get; }

        public GameEvent GameEvent { get; }

        public static TowerTriggerContext ForSimulationTick()
        {
            GameEvent emptyEvent = default;
            return new TowerTriggerContext(
                TowerTriggerDispatchKind.SimulationTick,
                null,
                in emptyEvent);
        }

        public static TowerTriggerContext ForEnemyDied(
            EnemyState deadEnemy,
            in GameEvent gameEvent)
        {
            if (deadEnemy == null)
            {
                throw new ArgumentNullException(nameof(deadEnemy));
            }

            return new TowerTriggerContext(
                TowerTriggerDispatchKind.EnemyDied,
                deadEnemy,
                in gameEvent);
        }
    }

    /// <summary>
    /// 한 TowerTrigger의 실행 규칙을 응집한 모듈 경계다.
    /// 핸들러가 선언한 Subject/Selector는 콘텐츠 계약과 등록 시 교차 검증된다.
    /// </summary>
    internal interface ITowerTriggerHandler
    {
        TowerTrigger Trigger { get; }

        TowerTriggerDispatchKind DispatchKind { get; }

        SubjectTypeMode SubjectMode { get; }

        SubjectSelector Selector { get; }

        void Execute(
            ITowerTriggerRuntime runtime,
            TowerState tower,
            CompiledTowerDefinition definition,
            in TowerTriggerContext context);
    }

    /// <summary>
    /// 트리거 핸들러가 시뮬레이션 전체 대신 사용하는 좁은 실행 포트다.
    /// 레지스트리와 핸들러는 GameSimulation의 나머지 상태나 카드 시스템에 의존하지 않는다.
    /// </summary>
    internal interface ITowerTriggerRuntime
    {
        void ExecuteAttackTrigger(
            TowerState tower,
            CompiledTowerDefinition definition);

        void ExecuteEnemyEnteredRangeTrigger(
            TowerState tower,
            CompiledTowerDefinition definition);

        void ExecuteEnemyDiedTrigger(
            TowerState tower,
            CompiledTowerDefinition definition,
            EnemyState deadEnemy,
            in GameEvent gameEvent);
    }

    /// <summary>
    /// Trigger를 실행 핸들러에 연결하고 공통 문법 검증 및 사건 종류 필터링을 담당한다.
    /// GameSimulation은 개별 Trigger를 switch하지 않고 이 레지스트리만 호출한다.
    /// </summary>
    internal sealed class TowerTriggerRegistry
    {
        private readonly Dictionary<TowerTrigger, ITowerTriggerHandler>
            handlers =
                new Dictionary<TowerTrigger, ITowerTriggerHandler>();

        public TowerTriggerRegistry(
            IReadOnlyList<ITowerTriggerHandler> registeredHandlers)
        {
            if (registeredHandlers == null)
            {
                throw new ArgumentNullException(
                    nameof(registeredHandlers));
            }

            for (int index = 0;
                 index < registeredHandlers.Count;
                 index++)
            {
                ITowerTriggerHandler handler =
                    registeredHandlers[index];
                if (handler == null)
                {
                    throw new ArgumentException(
                        "Tower trigger handlers cannot contain null.",
                        nameof(registeredHandlers));
                }

                if (!TowerExecutionContract.TryValidate(
                        handler.Trigger,
                        handler.SubjectMode,
                        handler.Selector,
                        out string contractError))
                {
                    throw new InvalidOperationException(
                        "Tower trigger handler '" +
                        handler.GetType().Name +
                        "' violates the execution contract: " +
                        contractError);
                }

                if (handlers.ContainsKey(handler.Trigger))
                {
                    throw new InvalidOperationException(
                        "A tower trigger handler is already registered for '" +
                        handler.Trigger + "'.");
                }

                handlers.Add(handler.Trigger, handler);
            }

            Array triggerValues =
                Enum.GetValues(typeof(TowerTrigger));
            for (int index = 0;
                 index < triggerValues.Length;
                 index++)
            {
                var trigger =
                    (TowerTrigger)triggerValues.GetValue(index);
                if (TowerExecutionContract.TryGet(
                        trigger,
                        out _) &&
                    !handlers.ContainsKey(trigger))
                {
                    throw new InvalidOperationException(
                        "No tower trigger handler is registered for '" +
                        trigger + "'.");
                }
            }
        }

        public int Count => handlers.Count;

        public static TowerTriggerRegistry CreateDefault()
        {
            return new TowerTriggerRegistry(
                new ITowerTriggerHandler[]
                {
                    new AttackTowerTriggerHandler(),
                    new EnemyEnteredRangeTowerTriggerHandler(),
                    new EnemyDiedTowerTriggerHandler()
                });
        }

        public ITowerTriggerHandler Get(TowerTrigger trigger)
        {
            if (!handlers.TryGetValue(
                    trigger,
                    out ITowerTriggerHandler handler))
            {
                throw new InvalidOperationException(
                    "No tower trigger handler is registered for '" +
                    trigger + "'.");
            }

            return handler;
        }

        /// <summary>
        /// 현재 사건 종류와 일치하는 핸들러만 실행한다. definition의 Subject/Selector가
        /// 등록 계약과 다르면 조용히 무시하지 않고 즉시 예외로 보고한다.
        /// </summary>
        public bool TryDispatch(
            ITowerTriggerRuntime runtime,
            TowerState tower,
            CompiledTowerDefinition definition,
            in TowerTriggerContext context)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (tower == null)
            {
                throw new ArgumentNullException(nameof(tower));
            }
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ITowerTriggerHandler handler =
                Get(definition.Trigger);
            if (handler.SubjectMode !=
                    definition.SubjectTypeMode ||
                handler.Selector != definition.Selector)
            {
                throw new InvalidOperationException(
                    "Tower '" + definition.StableId +
                    "' uses unsupported trigger grammar '" +
                    definition.Trigger + "/" +
                    definition.SubjectTypeMode + "/" +
                    definition.Selector + "'.");
            }

            if (handler.DispatchKind != context.DispatchKind)
            {
                return false;
            }

            handler.Execute(
                runtime,
                tower,
                definition,
                in context);
            return true;
        }
    }

    internal sealed class AttackTowerTriggerHandler :
        ITowerTriggerHandler
    {
        public TowerTrigger Trigger => TowerTrigger.Attack;

        public TowerTriggerDispatchKind DispatchKind =>
            TowerTriggerDispatchKind.SimulationTick;

        public SubjectTypeMode SubjectMode =>
            SubjectTypeMode.Projectile;

        public SubjectSelector Selector =>
            SubjectSelector.PrimaryProjectile;

        public void Execute(
            ITowerTriggerRuntime runtime,
            TowerState tower,
            CompiledTowerDefinition definition,
            in TowerTriggerContext context)
        {
            runtime.ExecuteAttackTrigger(
                tower,
                definition);
        }
    }

    internal sealed class EnemyEnteredRangeTowerTriggerHandler :
        ITowerTriggerHandler
    {
        public TowerTrigger Trigger =>
            TowerTrigger.EnemyEnteredRange;

        public TowerTriggerDispatchKind DispatchKind =>
            TowerTriggerDispatchKind.SimulationTick;

        public SubjectTypeMode SubjectMode =>
            SubjectTypeMode.Enemy;

        public SubjectSelector Selector =>
            SubjectSelector.EnteringEnemy;

        public void Execute(
            ITowerTriggerRuntime runtime,
            TowerState tower,
            CompiledTowerDefinition definition,
            in TowerTriggerContext context)
        {
            runtime.ExecuteEnemyEnteredRangeTrigger(
                tower,
                definition);
        }
    }

    internal sealed class EnemyDiedTowerTriggerHandler :
        ITowerTriggerHandler
    {
        public TowerTrigger Trigger =>
            TowerTrigger.EnemyDied;

        public TowerTriggerDispatchKind DispatchKind =>
            TowerTriggerDispatchKind.EnemyDied;

        public SubjectTypeMode SubjectMode =>
            SubjectTypeMode.Enemy;

        public SubjectSelector Selector =>
            SubjectSelector.EnemiesNearEvent;

        public void Execute(
            ITowerTriggerRuntime runtime,
            TowerState tower,
            CompiledTowerDefinition definition,
            in TowerTriggerContext context)
        {
            if (context.EventEnemy == null)
            {
                throw new InvalidOperationException(
                    "EnemyDied tower trigger requires the dead enemy context.");
            }

            GameEvent gameEvent =
                context.GameEvent;
            runtime.ExecuteEnemyDiedTrigger(
                tower,
                definition,
                context.EventEnemy,
                in gameEvent);
        }
    }
}
