using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 타워 Trigger의 공통 dispatch와 사건 대상 선택을 담당한다.
    /// 개별 고정 틱 전투 계산은 Combat 파일에, 사망 사건 자체의 확정은 Effects 파일에
    /// 남기고 Trigger별 연결 규칙만 이 경계에 응집한다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private readonly TowerTriggerRegistry towerTriggerRegistry =
            TowerTriggerRegistry.CreateDefault();
        private TowerTriggerRuntimeAdapter towerTriggerRuntime;

        /// <summary>
        /// 배치된 타워를 안정된 목록 순서대로 순회하고, 고정 틱을 소비하는 Trigger만
        /// 레지스트리를 통해 실행한다. Trigger 종류를 중앙 switch로 분기하지 않는다.
        /// </summary>
        private void ProcessTowers()
        {
            ITowerTriggerRuntime runtime =
                GetTowerTriggerRuntime();
            TowerTriggerContext context =
                TowerTriggerContext.ForSimulationTick();
            for (int towerIndex = 0;
                 towerIndex < towers.Count;
                 towerIndex++)
            {
                TowerState tower = towers[towerIndex];
                CompiledTowerDefinition definition =
                    content.GetTower(tower.DefinitionId);
                towerTriggerRegistry.TryDispatch(
                    runtime,
                    tower,
                    definition,
                    in context);
            }
        }

        /// <summary>
        /// 한 적의 사망 사건을 모든 타워에 같은 안정 순서로 전달한다.
        /// 사망형 타워만 레지스트리의 사건 종류 필터를 통과하므로 특정 타워 ID나
        /// Trigger switch를 사망 처리 파이프라인에 둘 필요가 없다.
        /// </summary>
        private void ProcessEnemyDiedTowerTriggers(
            EnemyState deadEnemy,
            in GameEvent gameEvent)
        {
            ITowerTriggerRuntime runtime =
                GetTowerTriggerRuntime();
            TowerTriggerContext context =
                TowerTriggerContext.ForEnemyDied(
                    deadEnemy,
                    in gameEvent);
            for (int towerIndex = 0;
                 towerIndex < towers.Count;
                 towerIndex++)
            {
                TowerState tower = towers[towerIndex];
                CompiledTowerDefinition definition =
                    content.GetTower(tower.DefinitionId);
                towerTriggerRegistry.TryDispatch(
                    runtime,
                    tower,
                    definition,
                    in context);
            }
        }

        /// <summary>
        /// EnemyDied/EnemiesNearEvent 계약의 대상 선택과 프로그램 예약을 실행한다.
        /// 후보는 기존과 동일하게 우선순위 정렬한 뒤 TargetLimit만큼만 선택한다.
        /// </summary>
        private void ProcessEnemyDiedTower(
            TowerState tower,
            CompiledTowerDefinition definition,
            EnemyState deadEnemy,
            in GameEvent gameEvent)
        {
            CompiledTowerLevelBalance level =
                GetTowerLevelBalance(tower);
            if (tower.Program.Length == 0 ||
                !PathModel.IsWithin(
                    tower.Position,
                    deadEnemy.Position,
                    level.RangeMilli))
            {
                return;
            }

            var candidates = new List<EnemyState>();
            spatialIndex.Query(
                deadEnemy.Position,
                level.SelectorRadiusMilli,
                spatialScratch);
            for (int enemyIndex = 0;
                 enemyIndex < spatialScratch.Count;
                 enemyIndex++)
            {
                EnemyState candidate =
                    FindEnemy(spatialScratch[enemyIndex]);
                if (candidate.Alive &&
                    PathModel.IsWithin(
                        deadEnemy.Position,
                        candidate.Position,
                        level.SelectorRadiusMilli))
                {
                    candidates.Add(candidate);
                }
            }

            candidates.Sort((left, right) =>
                CompareTargetPriority(
                    deadEnemy.Position,
                    left,
                    right));

            int count = Math.Min(
                level.TargetLimit,
                candidates.Count);
            for (int candidateIndex = 0;
                 candidateIndex < count;
                 candidateIndex++)
            {
                // 각 대상 발동은 고유 ActivationId를 가지지만 최초 사망의
                // RootChain과 부모 이벤트는 공유해 기존 연쇄 예산과 순서를 유지한다.
                EnqueueProgram(
                    SubjectType.Enemy,
                    candidates[candidateIndex].Id,
                    tower.Id,
                    0,
                    gameEvent.RootChainId,
                    CreateActivation(),
                    gameEvent.EventId,
                    gameEvent.Depth + 1,
                    EventPhase.Death);
            }
        }

        private ITowerTriggerRuntime GetTowerTriggerRuntime()
        {
            if (towerTriggerRuntime == null)
            {
                towerTriggerRuntime =
                    new TowerTriggerRuntimeAdapter(this);
            }

            return towerTriggerRuntime;
        }

        /// <summary>
        /// 트리거 모듈에 GameSimulation 전체를 노출하지 않고 필요한 세 실행점만 제공한다.
        /// GameSimulation의 공개 API에는 내부 상태형 인터페이스가 새어 나가지 않는다.
        /// </summary>
        private sealed class TowerTriggerRuntimeAdapter :
            ITowerTriggerRuntime
        {
            private readonly GameSimulation simulation;

            public TowerTriggerRuntimeAdapter(
                GameSimulation simulation)
            {
                this.simulation = simulation ??
                    throw new ArgumentNullException(
                        nameof(simulation));
            }

            public void ExecuteAttackTrigger(
                TowerState tower,
                CompiledTowerDefinition definition)
            {
                simulation.ProcessAttackTower(
                    tower,
                    definition);
            }

            public void ExecuteEnemyEnteredRangeTrigger(
                TowerState tower,
                CompiledTowerDefinition definition)
            {
                simulation.ProcessRangeEntryTower(
                    tower,
                    definition);
            }

            public void ExecuteEnemyDiedTrigger(
                TowerState tower,
                CompiledTowerDefinition definition,
                EnemyState deadEnemy,
                in GameEvent gameEvent)
            {
                simulation.ProcessEnemyDiedTower(
                    tower,
                    definition,
                    deadEnemy,
                    in gameEvent);
            }
        }
    }
}
