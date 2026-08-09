using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Effects
{
    /// <summary>
    /// 복제·희생·환원·역행의 데이터 연산을 시뮬레이션 전용 API로 연결한다.
    /// 복제만 대상 수를 늘리므로 기존 Split continuation 계약을 재사용한다.
    /// </summary>
    internal sealed class RareGenerationMotionEffectExecutor :
        IEffectExecutor
    {
        private readonly EffectOperation operation;

        public RareGenerationMotionEffectExecutor(
            EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            switch (operation)
            {
                case EffectOperation.DuplicateProjectile:
                {
                    EntityId child =
                        simulation.DuplicateRareProjectile(
                            context,
                            node);
                    return DuplicateOutcome(child, context);
                }
                case EffectOperation.DuplicateEnemy:
                {
                    EntityId child =
                        simulation.DuplicateRareEnemy(
                            context,
                            node);
                    return DuplicateOutcome(child, context);
                }
                case EffectOperation.SacrificeProjectile:
                    simulation.SacrificeRareProjectile(
                        context,
                        node);
                    break;
                case EffectOperation.SacrificeEnemy:
                    simulation.SacrificeRareEnemy(
                        context,
                        node);
                    break;
                case EffectOperation.ConfigureProjectileReturn:
                    simulation.ConfigureRareProjectileReturn(
                        context,
                        node);
                    break;
                case EffectOperation.RewindEnemy:
                    simulation.RewindRareEnemy(
                        context,
                        node);
                    break;
                case EffectOperation.ConfigureProjectileRetrograde:
                    simulation.ConfigureRareProjectileRetrograde(
                        context,
                        node);
                    break;
                case EffectOperation.ApplyEnemyRetrograde:
                    simulation.ApplyRareEnemyRetrograde(
                        context,
                        node);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operation),
                        operation,
                        "Unsupported Rare generation/motion operation.");
            }

            return EffectExecutionOutcome.Continue();
        }

        private static EffectExecutionOutcome DuplicateOutcome(
            EntityId child,
            in EffectExecutionContext context)
        {
            return child.IsValid
                ? EffectExecutionOutcome.Split(
                    child,
                    context.ContinuationCardCount,
                    context.ContinuationCardCount)
                : EffectExecutionOutcome.Continue();
        }
    }

    /// <summary>
    /// 공명·흡수·시간 정지·변이 연산을 공통 시뮬레이션 진입점에 연결한다.
    /// </summary>
    internal sealed class RareResonanceTimeEffectExecutor :
        IEffectExecutor
    {
        private readonly EffectOperation operation;

        public RareResonanceTimeEffectExecutor(
            EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ExecuteRareResonanceAbsorbTimeMutation(
                context,
                operation,
                node);
            return EffectExecutionOutcome.Continue();
        }
    }

    /// <summary>
    /// 처형·기생·환생·연쇄 연산을 지연 실행 상태에 연결한다.
    /// </summary>
    internal sealed class RareDeathChainEffectExecutor :
        IEffectExecutor
    {
        private readonly EffectOperation operation;

        public RareDeathChainEffectExecutor(
            EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ExecuteRareDeathChainEffect(
                context,
                operation,
                node);
            return EffectExecutionOutcome.Continue();
        }
    }
}
