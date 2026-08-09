using System;
using RuleforgeTD.GameLogic.Content;

namespace RuleforgeTD.GameLogic.Effects
{
    /// <summary>
    /// 공통 카드 operation을 효과 실행 포트의 전용 규칙 메서드로 연결한다.
    /// Simulation 구현 타입을 참조하지 않는 무상태 adapter다.
    /// </summary>
    internal sealed class CommonCardEffectExecutor :
        IEffectExecutor
    {
        private readonly EffectOperation operation;

        public CommonCardEffectExecutor(
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
                case EffectOperation.ConfigureProjectileRicochet:
                    simulation.ConfigureProjectileRicochet(
                        context,
                        node);
                    break;
                case EffectOperation.ApplyEnemyRicochet:
                    simulation.ApplyEnemyRicochet(
                        context,
                        node);
                    break;
                case EffectOperation.BindBleed:
                    simulation.AddProjectileBinding(
                        context,
                        BindingTrigger.OnHit,
                        BindingKind.Bleed,
                        node);
                    break;
                case EffectOperation.ApplyBleed:
                    simulation.ApplyBleed(context, node);
                    break;
                case EffectOperation.AccelerateProjectile:
                    simulation.AccelerateProjectile(
                        context,
                        node);
                    break;
                case EffectOperation.AccelerateEnemy:
                    simulation.AccelerateEnemy(
                        context,
                        node);
                    break;
                case EffectOperation.EnableProjectileHoming:
                    simulation.EnableProjectileHoming(context);
                    break;
                case EffectOperation.ApplyHomingPriority:
                    simulation.ApplyHomingPriority(
                        context,
                        node);
                    break;
                case EffectOperation.DelayProjectile:
                    simulation.DelayProjectile(context, node);
                    break;
                case EffectOperation.ApplyDelay:
                    simulation.ApplyDelay(context, node);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported common card operation " +
                        operation + ".");
            }

            return EffectExecutionOutcome.Continue();
        }
    }
}
