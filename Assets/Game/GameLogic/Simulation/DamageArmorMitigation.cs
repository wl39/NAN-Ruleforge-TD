using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// Deterministic, data-driven armor response shared by every damage
    /// request. Multi-target and burn damage deliberately read armor more
    /// strongly, while armor ignore still reduces armor before sensitivity.
    /// </summary>
    internal static class DamageArmorMitigation
    {
        private const int StandardSensitivityBps = 10000;

        internal static long Apply(
            long amount,
            int armor,
            int armorIgnoreBps,
            DamageKind kind,
            EventTags tags,
            CompiledRunDefinition run)
        {
            if (amount <= 0)
            {
                return 0;
            }
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            int boundedArmorIgnore =
                Math.Max(0, Math.Min(10000, armorIgnoreBps));
            long effectiveArmor =
                DeterministicMath.MultiplyBasisPoints(
                    Math.Max(0, armor),
                    10000 - boundedArmorIgnore);
            int sensitivityBps = ResolveSensitivityBps(
                kind,
                tags,
                run);
            long weightedArmor =
                DeterministicMath.MultiplyBasisPoints(
                    effectiveArmor,
                    sensitivityBps);
            int scale = Math.Max(1, run.ArmorMitigationScale);
            int boundedWeightedArmor = (int)Math.Min(
                int.MaxValue - (long)scale,
                Math.Max(0L, weightedArmor));
            long resolved = DeterministicMath.MultiplyDivide(
                amount,
                scale,
                scale + boundedWeightedArmor);
            return Math.Max(1, resolved);
        }

        internal static int ResolveSensitivityBps(
            DamageKind kind,
            EventTags tags,
            CompiledRunDefinition run)
        {
            int result = StandardSensitivityBps;
            if (kind == DamageKind.Explosion ||
                (tags & EventTags.Area) != 0)
            {
                result = Math.Max(
                    result,
                    run.AreaArmorSensitivityBps);
            }
            if (kind == DamageKind.Fire)
            {
                result = Math.Max(
                    result,
                    run.BurnArmorSensitivityBps);
            }

            return result;
        }
    }
}
