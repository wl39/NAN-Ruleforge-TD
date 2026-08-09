using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// Shared card-program grammar. Traversal, pass scaling and completion
    /// routing live here so Legendary/Mythic cards do not each reimplement an
    /// event cursor or bypass the central ChainBudget.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private ProgramExecutionSpec CreateDefaultProgramExecution(
            TowerState tower,
            SubjectType subjectType)
        {
            int direction = ProgramContainsOperation(
                tower,
                subjectType,
                EffectOperation.ReverseProgramOrder)
                    ? -1
                    : 1;
            return new ProgramExecutionSpec(
                direction,
                10000,
                0,
                EffectExecutionFlags.None);
        }

        private static ProgramExecutionSpec
            CreateProgramExecution(
                in EffectExecutionContext context)
        {
            return new ProgramExecutionSpec(
                context.TraversalDirection,
                context.PowerBps,
                context.RepeatIndex,
                context.ExecutionFlags);
        }

        private bool ProgramContainsOperation(
            TowerState tower,
            SubjectType subjectType,
            EffectOperation operation)
        {
            if (tower == null)
            {
                return false;
            }

            for (int index = 0;
                 index < tower.Program.Length;
                 index++)
            {
                SubjectType configured =
                    index < tower.ProgramSubjectTypes.Length
                        ? tower.ProgramSubjectTypes[index]
                        : tower.SubjectType;
                if (configured != subjectType)
                {
                    continue;
                }

                CompiledCardDefinition card =
                    content.GetCard(tower.Program[index]);
                CompiledEffectNode[] nodes =
                    subjectType == SubjectType.Projectile
                        ? card.ProjectileEffectsInternal
                        : card.EnemyEffectsInternal;
                for (int nodeIndex = 0;
                     nodeIndex < nodes.Length;
                     nodeIndex++)
                {
                    if (nodes[nodeIndex].Operation == operation)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int FindProgramEntryIndex(
            TowerState tower,
            SubjectType subjectType,
            in ProgramExecutionSpec execution)
        {
            if (tower == null)
            {
                return -1;
            }

            int index = execution.Direction > 0
                ? 0
                : tower.Program.Length - 1;
            while (index >= 0 &&
                   index < tower.Program.Length)
            {
                SubjectType configured =
                    index < tower.ProgramSubjectTypes.Length
                        ? tower.ProgramSubjectTypes[index]
                        : tower.SubjectType;
                if (configured == subjectType)
                {
                    return index;
                }
                index += execution.Direction;
            }

            return -1;
        }

        /// <summary>
        /// Applies the power carried by a repeated pass. Counts that describe
        /// topology stay structural, while potency and duration decay.
        /// </summary>
        private static CompiledEffectNode ScaleProgramPassNode(
            in CompiledEffectNode node,
            int powerBps)
        {
            if (powerBps >= 10000 || powerBps <= 0)
            {
                return node;
            }

            int amount = IsProgramStructuralAmount(node.Operation)
                ? node.Amount
                : ScaleProgramValue(node.Amount, powerBps);
            return new CompiledEffectNode(
                node.Operation,
                amount,
                ScaleProgramValue(node.Amount2, powerBps),
                ScaleProgramValue(node.Amount3, powerBps),
                ScaleProgramValue(node.DurationTicks, powerBps),
                node.IntervalTicks,
                node.MaxStacks,
                node.RadiusMilli,
                node.Limit,
                node.ChanceBps,
                node.ReferenceId);
        }

        private static bool IsProgramStructuralAmount(
            EffectOperation operation)
        {
            switch (operation)
            {
                case EffectOperation.Split:
                case EffectOperation.AddPierce:
                case EffectOperation.ConfigureProjectileRicochet:
                case EffectOperation.EnableRecursion:
                case EffectOperation.ReverseProgramOrder:
                case EffectOperation.EnableProjectileDualInterpretation:
                case EffectOperation.ApplyEnemyDualInterpretation:
                case EffectOperation.EnableProjectileInfiniteOrbit:
                case EffectOperation.ApplyEnemyInfiniteOrbit:
                case EffectOperation.EnableProjectileOverclone:
                case EffectOperation.ApplyEnemyOverclone:
                case EffectOperation.EnableProjectileForbiddenDeal:
                case EffectOperation.EnableProjectileLastCommand:
                case EffectOperation.ApplyEnemyLastCommand:
                case EffectOperation.EnableProjectileFateLock:
                case EffectOperation.ApplyEnemyFateLock:
                case EffectOperation.EnableProjectileOverload:
                case EffectOperation.ApplyEnemyOverload:
                case EffectOperation.EnableProjectileOuroboros:
                case EffectOperation.ApplyEnemyOuroboros:
                    return true;
                default:
                    return false;
            }
        }

        private static int ScaleProgramValue(
            int value,
            int powerBps)
        {
            if (value <= 0)
            {
                return value;
            }

            return (int)Math.Max(
                1,
                Math.Min(
                    int.MaxValue,
                    DeterministicMath.MultiplyBasisPoints(
                        value,
                        powerBps)));
        }

        private void HandleProgramPassCompleted(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            in ProgramExecutionSpec execution)
        {
            // Rare Chain keeps its scale in activation-scoped state and
            // removes it during its completion handler. Capture it into the
            // immutable pass spec first so later Recursion/Overload/Ouroboros
            // passes retain the inherited potency without keeping stale state.
            int inheritedPowerBps = (int)
                DeterministicMath.MultiplyBasisPoints(
                    execution.PowerBps,
                    GetRareChainScale(activationId));
            ProgramExecutionSpec completionExecution =
                execution.WithPowerBps(
                    Math.Max(1, inheritedPowerBps));
            HandleRareProgramCompleted(
                subjectType,
                subjectId,
                towerId,
                rootChainId,
                activationId,
                parentEventId,
                depth);
            HandleLegendaryProgramCompleted(
                subjectType,
                subjectId,
                towerId,
                rootChainId,
                activationId,
                parentEventId,
                depth,
                in completionExecution);
            HandleMythicProgramCompleted(
                subjectType,
                subjectId,
                towerId,
                rootChainId,
                activationId,
                parentEventId,
                depth,
                in completionExecution);
        }
    }
}
