using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// EffectRegistry에 GameSimulation 전체 대신 좁은 효과 실행 포트를 제공한다.
    /// 명시적 구현은 외부 공개 API를 늘리지 않고 기존 내부 도메인 메서드로 위임한다.
    /// </summary>
    public sealed partial class GameSimulation : IEffectExecutionHost
    {
        EntityId IEffectExecutionHost.SplitProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            return SplitProjectile(context, node);
        }

        EntityId IEffectExecutionHost.SplitEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            return SplitEnemy(context, node);
        }

        void IEffectExecutionHost.AddProjectilePierce(
            EntityId projectileId,
            in CompiledEffectNode node)
        {
            AddProjectilePierce(projectileId, node);
        }

        void IEffectExecutionHost.AddProjectileBinding(
            in EffectExecutionContext context,
            BindingTrigger trigger,
            BindingKind kind,
            in CompiledEffectNode node)
        {
            AddProjectileBinding(context, trigger, kind, node);
        }

        void IEffectExecutionHost.AddEnemyDeathBinding(
            in EffectExecutionContext context,
            BindingKind kind,
            in CompiledEffectNode node)
        {
            AddEnemyDeathBinding(context, kind, node);
        }

        void IEffectExecutionHost.ApplyStatus(
            in EffectExecutionContext context,
            StatusType statusType,
            in CompiledEffectNode node)
        {
            ApplyStatus(context, statusType, node);
        }

        void IEffectExecutionHost.ModifyProjectile(
            EntityId projectileId,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            ModifyProjectile(projectileId, operation, node);
        }

        void IEffectExecutionHost.ApplyDirectEnemyEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            ApplyDirectEnemyEffect(context, operation, node);
        }

        void IEffectExecutionHost.ConfigureProjectileRicochet(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ConfigureProjectileRicochet(context, node);
        }

        void IEffectExecutionHost.ApplyEnemyRicochet(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ApplyEnemyRicochet(context, node);
        }

        void IEffectExecutionHost.ApplyBleed(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ApplyBleed(context, node);
        }

        void IEffectExecutionHost.AccelerateProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            AccelerateProjectile(context, node);
        }

        void IEffectExecutionHost.AccelerateEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            AccelerateEnemy(context, node);
        }

        void IEffectExecutionHost.EnableProjectileHoming(
            in EffectExecutionContext context)
        {
            EnableProjectileHoming(context);
        }

        void IEffectExecutionHost.ApplyHomingPriority(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ApplyHomingPriority(context, node);
        }

        void IEffectExecutionHost.DelayProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            DelayProjectile(context, node);
        }

        void IEffectExecutionHost.ApplyDelay(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ApplyDelay(context, node);
        }

        void IEffectExecutionHost.ExecuteUncommonEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            ExecuteUncommonEffect(context, operation, node);
        }

        EntityId IEffectExecutionHost.DuplicateRareProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            return DuplicateRareProjectile(context, node);
        }

        EntityId IEffectExecutionHost.DuplicateRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            return DuplicateRareEnemy(context, node);
        }

        void IEffectExecutionHost.SacrificeRareProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            SacrificeRareProjectile(context, node);
        }

        void IEffectExecutionHost.SacrificeRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            SacrificeRareEnemy(context, node);
        }

        void IEffectExecutionHost.ConfigureRareProjectileReturn(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ConfigureRareProjectileReturn(context, node);
        }

        void IEffectExecutionHost.RewindRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            RewindRareEnemy(context, node);
        }

        void IEffectExecutionHost.ConfigureRareProjectileRetrograde(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ConfigureRareProjectileRetrograde(context, node);
        }

        void IEffectExecutionHost.ApplyRareEnemyRetrograde(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ApplyRareEnemyRetrograde(context, node);
        }

        void IEffectExecutionHost.ExecuteRareResonanceAbsorbTimeMutation(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            ExecuteRareResonanceAbsorbTimeMutation(
                context,
                operation,
                node);
        }

        void IEffectExecutionHost.ExecuteRareDeathChainEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            ExecuteRareDeathChainEffect(context, operation, node);
        }

        EffectExecutionOutcome
            IEffectExecutionHost.ExecuteLegendaryEffect(
                in EffectExecutionContext context,
                EffectOperation operation,
                in CompiledEffectNode node)
        {
            return ExecuteLegendaryEffect(
                context,
                operation,
                node);
        }

        EffectExecutionOutcome
            IEffectExecutionHost.ExecuteMythicEffect(
                in EffectExecutionContext context,
                EffectOperation operation,
                in CompiledEffectNode node)
        {
            return ExecuteMythicEffect(
                context,
                operation,
                node);
        }
    }
}
