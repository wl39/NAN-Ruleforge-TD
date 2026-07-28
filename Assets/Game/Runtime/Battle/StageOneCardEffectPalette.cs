using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Lightweight procedural shapes used by the Stage 01 card-effect view.
    /// They deliberately avoid prefab and texture dependencies so the same
    /// presentation works in Editor and WebGL builds.
    /// </summary>
    public enum StageOneCardEffectShape
    {
        Arc = 0,
        Slash = 1,
        Streak = 2,
        Reticle = 3,
        Clock = 4,
        Rune = 5,
        Chain = 6,
        Launch = 7,
        Lightning = 8,
        IceBurst = 9,
        Echo = 10,
        Pulse = 11,
        Vortex = 12,
        Mirror = 13,
        Transfer = 14,
        Seal = 15,
        Corrosion = 16,
        Orbit = 17,
        Heal = 18,
        Fear = 19,
        Branch = 20,
        Lance = 21,
        Flame = 22,
        Hourglass = 23,
        Blast = 24,
        Impact = 25,
        Target = 26,
        Coin = 27,
        Toxic = 28,
        Grow = 29,
        Contract = 30,
        StunBurst = 31
    }

    /// <summary>
    /// Immutable visual language for one card. Numerical values are purely
    /// presentational and never feed back into combat simulation.
    /// </summary>
    public readonly struct StageOneCardEffectStyle
    {
        public StageOneCardEffectStyle(
            string id,
            Color primary,
            Color secondary,
            StageOneCardEffectShape shape,
            float duration,
            float radius,
            float width,
            float motionHeight)
        {
            Id = id ?? string.Empty;
            Primary = primary;
            Secondary = secondary;
            Shape = shape;
            Duration = Mathf.Max(0.05f, duration);
            Radius = Mathf.Max(0.05f, radius);
            Width = Mathf.Max(0.01f, width);
            MotionHeight = Mathf.Max(0f, motionHeight);
        }

        public string Id { get; }
        public Color Primary { get; }
        public Color Secondary { get; }
        public StageOneCardEffectShape Shape { get; }
        public float Duration { get; }
        public float Radius { get; }
        public float Width { get; }
        public float MotionHeight { get; }
    }

    /// <summary>
    /// Single source of truth for Common and Uncommon procedural VFX colours.
    /// Aliases include persistent status names and transient presentation-event
    /// ids so render code does not duplicate card-specific colour decisions.
    /// </summary>
    public static class StageOneCardEffectPalette
    {
        public const int StyleCount = 32;
        public const float StandardEffectDuration = 0.56f;

        private static readonly StageOneCardEffectStyle[] Styles =
        {
            // 기존 Common/Uncommon 카드도 모두 카드 전용 색과 모션을
            // 갖는다. 아래 스타일은 CardExecuted/StatusApplied 이벤트와
            // 투사체 표시 플래그가 함께 사용한다.
            Style(
                "split",
                120, 214, 255,
                214, 245, 255,
                StageOneCardEffectShape.Branch,
                0.24f, 0.66f, 0.065f, 0.34f),
            Style(
                "pierce",
                214, 232, 255,
                72, 173, 255,
                StageOneCardEffectShape.Lance,
                0.44f, 0.74f, 0.055f, 0.08f),
            Style(
                "burn",
                255, 92, 30,
                255, 220, 72,
                StageOneCardEffectShape.Flame,
                0.62f, 0.68f, 0.075f, 0.34f),
            Style(
                "slow",
                92, 174, 255,
                220, 244, 255,
                StageOneCardEffectShape.Hourglass,
                0.68f, 0.66f, 0.065f, 0.12f),
            Style(
                "explode",
                255, 126, 32,
                255, 238, 128,
                StageOneCardEffectShape.Blast,
                0.62f, 0.92f, 0.09f, 0.16f),
            Style(
                "knockback",
                236, 202, 132,
                255, 248, 218,
                StageOneCardEffectShape.Impact,
                0.30f, 0.72f, 0.085f, 0.12f),
            Style(
                "mark",
                255, 72, 92,
                255, 226, 96,
                StageOneCardEffectShape.Target,
                0.46f, 0.62f, 0.06f, 0.08f),
            Style(
                "gold_bounty",
                255, 205, 52,
                255, 249, 184,
                StageOneCardEffectShape.Coin,
                0.48f, 0.58f, 0.075f, 0.22f),
            Style(
                "poison",
                91, 222, 72,
                200, 255, 105,
                StageOneCardEffectShape.Toxic,
                0.70f, 0.66f, 0.075f, 0.20f),
            Style(
                "enlarge",
                244, 151, 66,
                255, 232, 160,
                StageOneCardEffectShape.Grow,
                0.46f, 0.82f, 0.07f, 0.16f),
            Style(
                "shrink",
                104, 224, 224,
                222, 255, 250,
                StageOneCardEffectShape.Contract,
                0.46f, 0.76f, 0.06f, 0.12f),
            Style(
                "stun",
                255, 232, 62,
                255, 251, 207,
                StageOneCardEffectShape.StunBurst,
                0.34f, 0.68f, 0.075f, 0.18f),

            // Common
            Style(
                "ricochet",
                82, 224, 255,
                220, 252, 255,
                StageOneCardEffectShape.Arc,
                0.28f, 0.58f, 0.075f, 0.42f),
            Style(
                "bleed",
                205, 42, 62,
                255, 136, 110,
                StageOneCardEffectShape.Slash,
                0.34f, 0.52f, 0.085f, 0.08f),
            Style(
                "accelerate",
                255, 190, 54,
                120, 245, 255,
                StageOneCardEffectShape.Streak,
                0.30f, 0.68f, 0.06f, 0.12f),
            Style(
                "homing",
                52, 235, 194,
                218, 255, 247,
                StageOneCardEffectShape.Reticle,
                0.42f, 0.62f, 0.055f, 0.10f),
            Style(
                "delay",
                120, 174, 255,
                226, 240, 255,
                StageOneCardEffectShape.Clock,
                0.52f, 0.56f, 0.055f, 0.10f),

            // Uncommon
            Style(
                "curse",
                170, 64, 235,
                238, 160, 255,
                StageOneCardEffectShape.Rune,
                0.58f, 0.68f, 0.075f, 0.16f),
            Style(
                "bind",
                142, 148, 158,
                225, 228, 232,
                StageOneCardEffectShape.Chain,
                0.52f, 0.62f, 0.085f, 0.08f),
            Style(
                "airborne",
                112, 211, 255,
                245, 252, 255,
                StageOneCardEffectShape.Launch,
                0.70f, 0.72f, 0.07f, 0.92f),
            Style(
                "shock",
                255, 226, 65,
                120, 210, 255,
                StageOneCardEffectShape.Lightning,
                0.26f, 0.72f, 0.065f, 0.18f),
            Style(
                "freeze",
                88, 218, 255,
                232, 252, 255,
                StageOneCardEffectShape.IceBurst,
                0.48f, 0.68f, 0.065f, 0.18f),
            Style(
                "afterimage",
                176, 154, 255,
                220, 244, 255,
                StageOneCardEffectShape.Echo,
                0.62f, 0.64f, 0.055f, 0.12f),
            Style(
                "pulse",
                65, 171, 255,
                185, 235, 255,
                StageOneCardEffectShape.Pulse,
                0.46f, 0.92f, 0.055f, 0.08f),
            Style(
                "magnet",
                60, 104, 220,
                120, 237, 255,
                StageOneCardEffectShape.Vortex,
                0.68f, 0.82f, 0.065f, 0.16f),
            Style(
                "reflect",
                206, 222, 240,
                115, 160, 255,
                StageOneCardEffectShape.Mirror,
                0.38f, 0.68f, 0.07f, 0.24f),
            Style(
                "contagion",
                126, 232, 102,
                190, 94, 236,
                StageOneCardEffectShape.Transfer,
                0.48f, 0.66f, 0.065f, 0.22f),
            Style(
                "seal",
                247, 224, 146,
                255, 252, 230,
                StageOneCardEffectShape.Seal,
                0.54f, 0.62f, 0.075f, 0.10f),
            Style(
                "corrosion",
                133, 205, 48,
                222, 242, 105,
                StageOneCardEffectShape.Corrosion,
                0.56f, 0.66f, 0.08f, 0.12f),
            Style(
                "orbit",
                255, 143, 48,
                255, 225, 112,
                StageOneCardEffectShape.Orbit,
                0.58f, 0.76f, 0.065f, 0.20f),
            Style(
                "lifesteal",
                215, 47, 92,
                255, 172, 190,
                StageOneCardEffectShape.Heal,
                0.52f, 0.62f, 0.075f, 0.46f),
            Style(
                "fear",
                92, 36, 118,
                232, 88, 178,
                StageOneCardEffectShape.Fear,
                0.46f, 0.68f, 0.08f, 0.16f)
        };

        private static readonly Dictionary<string, int> StyleIndices =
            CreateStyleIndices();

        public static StageOneCardEffectStyle GetStyle(int index)
        {
            if (index < 0 || index >= Styles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Styles[index];
        }

        public static bool TryGetStyle(
            string effectId,
            out StageOneCardEffectStyle style)
        {
            if (!string.IsNullOrEmpty(effectId) &&
                StyleIndices.TryGetValue(effectId, out int index))
            {
                style = Styles[index];
                return true;
            }

            style = default;
            return false;
        }

        private static Dictionary<string, int> CreateStyleIndices()
        {
            var result = new Dictionary<string, int>(
                96,
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Styles.Length; i++)
            {
                result.Add(Styles[i].Id, i);
            }

            AddAlias(result, "Split", "split");
            AddAlias(result, "Pierced", "pierce");
            AddAlias(result, "Burn", "burn");
            AddAlias(result, "Slow", "slow");
            AddAlias(result, "Explosion", "explode");
            AddAlias(result, "Knockback", "knockback");
            AddAlias(result, "Mark", "mark");
            AddAlias(result, "Gold", "gold_bounty");
            AddAlias(result, "GoldBounty", "gold_bounty");
            AddAlias(result, "CardBounty", "gold_bounty");
            AddAlias(result, "Poison", "poison");
            AddAlias(result, "Enlarge", "enlarge");
            AddAlias(result, "Shrink", "shrink");
            AddAlias(result, "Stun", "stun");
            AddAlias(result, "Ricochet", "ricochet");
            AddAlias(result, "Bleed", "bleed");
            AddAlias(result, "HomingPriority", "homing");
            AddAlias(result, "Delay", "delay");
            AddAlias(result, "Curse", "curse");
            AddAlias(result, "Bind", "bind");
            AddAlias(result, "Blind", "bind");
            AddAlias(result, "Airborne", "airborne");
            AddAlias(result, "Shock", "shock");
            AddAlias(result, "Chill", "freeze");
            AddAlias(result, "Frozen", "freeze");
            AddAlias(result, "FreezeImmunity", "freeze");
            AddAlias(result, "Afterimage", "afterimage");
            AddAlias(result, "Pulse", "pulse");
            AddAlias(result, "Magnet", "magnet");
            AddAlias(result, "Reflect", "reflect");
            AddAlias(result, "Contagion", "contagion");
            AddAlias(result, "Seal", "seal");
            AddAlias(result, "Corrosion", "corrosion");
            AddAlias(result, "Orbit", "orbit");
            AddAlias(result, "Lifesteal", "lifesteal");
            AddAlias(result, "Fear", "fear");
            AddAlias(result, "FearHaste", "fear");

            AddAlias(result, "bind_pulse", "bind");
            AddAlias(result, "airborne_land", "airborne");
            AddAlias(result, "shock_chain", "shock");
            AddAlias(result, "freeze_shard", "freeze");
            AddAlias(result, "afterimage_spawn", "afterimage");
            AddAlias(result, "magnet_merge", "magnet");
            AddAlias(result, "reflect_turn", "reflect");
            AddAlias(result, "contagion_transfer", "contagion");
            AddAlias(result, "corrosion_tick", "corrosion");
            AddAlias(result, "orbit_hit", "orbit");
            AddAlias(result, "lifesteal_heal", "lifesteal");
            return result;
        }

        private static void AddAlias(
            Dictionary<string, int> destination,
            string alias,
            string canonicalId)
        {
            destination[alias] = destination[canonicalId];
        }

        private static StageOneCardEffectStyle Style(
            string id,
            byte primaryR,
            byte primaryG,
            byte primaryB,
            byte secondaryR,
            byte secondaryG,
            byte secondaryB,
            StageOneCardEffectShape shape,
            float duration,
            float radius,
            float width,
            float motionHeight)
        {
            // Individual legacy timings remain beside their visual tuning
            // values, but runtime playback is intentionally normalized.
            duration = StandardEffectDuration;
            return new StageOneCardEffectStyle(
                id,
                new Color32(
                    primaryR,
                    primaryG,
                    primaryB,
                    255),
                new Color32(
                    secondaryR,
                    secondaryG,
                    secondaryB,
                    255),
                shape,
                StandardEffectDuration,
                radius,
                width,
                motionHeight);
        }
    }
}
