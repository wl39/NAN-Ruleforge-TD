using RuleforgeTD.GameLogic.Content;

namespace RuleforgeTD.GameLogic.Effects
{
    /// <summary>
    /// Legendary operations share program-grammar and lifecycle state in one
    /// cohesive simulation module. This adapter keeps the registry declarative.
    /// </summary>
    internal sealed class LegendaryCardEffectExecutor :
        IEffectExecutor
    {
        private readonly EffectOperation operation;

        public LegendaryCardEffectExecutor(
            EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            return simulation.ExecuteLegendaryEffect(
                context,
                operation,
                node);
        }
    }

    /// <summary>
    /// Mythic operations use the same narrow host port while their derived
    /// entities, links and lifecycle rules remain isolated from Legendary data.
    /// </summary>
    internal sealed class MythicCardEffectExecutor :
        IEffectExecutor
    {
        private readonly EffectOperation operation;

        public MythicCardEffectExecutor(
            EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            return simulation.ExecuteMythicEffect(
                context,
                operation,
                node);
        }
    }
}
