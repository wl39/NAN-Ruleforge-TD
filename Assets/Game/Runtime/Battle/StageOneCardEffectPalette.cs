using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;
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
        StunBurst = 31,
        Twin = 32,
        Sacrifice = 33,
        Return = 34,
        Rewind = 35,
        Resonance = 36,
        Absorb = 37,
        TimeStop = 38,
        Mutate = 39,
        Execute = 40,
        Parasite = 41,
        Rebirth = 42,
        Relay = 43,
        Recursion = 44,
        ReverseOrder = 45,
        DualInterpretation = 46,
        InfiniteOrbit = 47,
        Overclone = 48,
        ForbiddenDeal = 49,
        LastCommand = 50,
        FateLock = 51,
        Overload = 52,
        Singularity = 53,
        PhoenixCore = 54,
        TimeRift = 55,
        MirrorWorld = 56,
        Ouroboros = 57
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
        public const float StandardEffectDuration = 0.56f;

        private static readonly StageOneCardEffectStyle[] Styles =
        {
            // 기존 Common/Uncommon 카드도 모두 카드 전용 색과 모션을
            // 갖는다. 아래 스타일은 CardExecuted/StatusApplied 이벤트와
            // 투사체 표시 플래그가 함께 사용한다.
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
                0.46f, 0.68f, 0.08f, 0.16f),

            // 기존 카드의 visualStyleIndex는 저장·스냅샷 비트 ABI다.
            // 따라서 이 배열도 CardEffectVisualFlags의 비트 순서를
            // 그대로 유지해야 카드가 다른 카드의 VFX로 바뀌지 않는다.
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

            // Rare
            Style(
                "duplicate",
                105, 226, 255,
                202, 151, 255,
                StageOneCardEffectShape.Twin,
                0.56f, 0.74f, 0.06f, 0.28f),
            Style(
                "sacrifice",
                232, 48, 76,
                255, 207, 94,
                StageOneCardEffectShape.Sacrifice,
                0.56f, 0.82f, 0.085f, 0.22f),
            Style(
                "return",
                70, 238, 211,
                224, 255, 248,
                StageOneCardEffectShape.Return,
                0.56f, 0.78f, 0.065f, 0.32f),
            Style(
                "retrograde",
                101, 88, 230,
                255, 126, 211,
                StageOneCardEffectShape.Rewind,
                0.56f, 0.72f, 0.065f, 0.20f),
            Style(
                "resonance",
                72, 172, 255,
                255, 227, 104,
                StageOneCardEffectShape.Resonance,
                0.56f, 0.90f, 0.06f, 0.16f),
            Style(
                "absorb",
                57, 69, 184,
                183, 86, 244,
                StageOneCardEffectShape.Absorb,
                0.56f, 0.88f, 0.075f, 0.20f),
            Style(
                "time_stop",
                179, 232, 255,
                255, 255, 255,
                StageOneCardEffectShape.TimeStop,
                0.56f, 0.72f, 0.055f, 0.10f),
            Style(
                "mutation",
                143, 245, 60,
                245, 72, 223,
                StageOneCardEffectShape.Mutate,
                0.56f, 0.72f, 0.075f, 0.18f),
            Style(
                "execute",
                211, 29, 55,
                255, 242, 226,
                StageOneCardEffectShape.Execute,
                0.56f, 0.74f, 0.085f, 0.08f),
            Style(
                "parasite",
                112, 207, 54,
                137, 58, 178,
                StageOneCardEffectShape.Parasite,
                0.56f, 0.68f, 0.075f, 0.14f),
            Style(
                "rebirth",
                255, 111, 42,
                255, 237, 118,
                StageOneCardEffectShape.Rebirth,
                0.56f, 0.86f, 0.075f, 0.46f),
            Style(
                "chain",
                136, 94, 255,
                113, 232, 255,
                StageOneCardEffectShape.Relay,
                0.56f, 0.82f, 0.065f, 0.28f),

            // Legendary
            Style(
                "recursion",
                255, 94, 214,
                128, 224, 255,
                StageOneCardEffectShape.Recursion,
                0.56f, 0.86f, 0.065f, 0.28f),
            Style(
                "reverse_order",
                117, 89, 255,
                255, 137, 223,
                StageOneCardEffectShape.ReverseOrder,
                0.56f, 0.78f, 0.065f, 0.20f),
            Style(
                "dual_interpretation",
                255, 238, 132,
                118, 217, 255,
                StageOneCardEffectShape.DualInterpretation,
                0.56f, 0.82f, 0.065f, 0.22f),
            Style(
                "infinite_orbit",
                76, 226, 255,
                255, 177, 70,
                StageOneCardEffectShape.InfiniteOrbit,
                0.56f, 0.92f, 0.065f, 0.20f),
            Style(
                "overclone",
                125, 249, 209,
                211, 132, 255,
                StageOneCardEffectShape.Overclone,
                0.56f, 0.82f, 0.060f, 0.28f),
            Style(
                "forbidden_deal",
                255, 204, 65,
                224, 58, 93,
                StageOneCardEffectShape.ForbiddenDeal,
                0.56f, 0.68f, 0.075f, 0.22f),
            Style(
                "last_command",
                255, 89, 72,
                255, 238, 180,
                StageOneCardEffectShape.LastCommand,
                0.56f, 0.88f, 0.075f, 0.24f),
            Style(
                "fate_lock",
                255, 235, 102,
                90, 196, 255,
                StageOneCardEffectShape.FateLock,
                0.56f, 0.72f, 0.060f, 0.08f),
            Style(
                "overload",
                255, 79, 156,
                255, 226, 93,
                StageOneCardEffectShape.Overload,
                0.56f, 0.98f, 0.090f, 0.18f),

            // Mythic
            Style(
                "singularity",
                47, 35, 112,
                196, 103, 255,
                StageOneCardEffectShape.Singularity,
                0.56f, 1.02f, 0.075f, 0.18f),
            Style(
                "phoenix_core",
                255, 72, 35,
                255, 241, 108,
                StageOneCardEffectShape.PhoenixCore,
                0.56f, 0.96f, 0.080f, 0.52f),
            Style(
                "time_rift",
                101, 226, 255,
                236, 181, 255,
                StageOneCardEffectShape.TimeRift,
                0.56f, 0.90f, 0.060f, 0.20f),
            Style(
                "mirror_world",
                224, 242, 255,
                104, 148, 255,
                StageOneCardEffectShape.MirrorWorld,
                0.56f, 0.94f, 0.065f, 0.24f),
            Style(
                "ouroboros",
                82, 235, 132,
                255, 207, 71,
                StageOneCardEffectShape.Ouroboros,
                0.56f, 1.02f, 0.075f, 0.22f)
        };

        private static readonly Dictionary<string, int> StyleIndices =
            CreateStyleIndices();

        private static readonly HashSet<string> AuthoredStyleIds =
            CreateAuthoredStyleIds();

        // 카드 전용 연출을 아직 authoring하지 않은 콘텐츠 모듈도 화면에서
        // 조용히 사라지지 않게 하는 결정적 fallback 표다. 전투 규칙에는 전혀
        // 관여하지 않으며, merged CompiledContent가 바뀔 때 composition root가
        // 한 번 다시 구성한다.
        private static readonly Dictionary<
            string,
            StageOneCardEffectStyle> GeneratedCardStyles =
                new Dictionary<string, StageOneCardEffectStyle>(
                    StringComparer.Ordinal);

        private static readonly Dictionary<
            string,
            StageOneCardEffectStyle> GeneratedEventStyles =
                new Dictionary<string, StageOneCardEffectStyle>(
                    StringComparer.Ordinal);

        public static int StyleCount => Styles.Length;

        public static StageOneCardEffectStyle GetStyle(int index)
        {
            if (index < 0 || index >= Styles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Styles[index];
        }

        /// <summary>
        /// Builds the gallery list from the authoritative compiled card
        /// catalog. Authored cards keep their tuned style and newly composed
        /// module cards receive the deterministic fallback registered from
        /// their stable ID. The returned order is the compiled card order.
        /// </summary>
        public static StageOneCardEffectStyle[] CreateCardGalleryStyles(
            CompiledContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            RegisterContent(content);
            CompiledCardDefinition[] cards = content.Cards;
            var result =
                new StageOneCardEffectStyle[cards.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                CompiledCardDefinition card = cards[i];
                if (card == null ||
                    !TryGetCardStyle(
                        card.StableId,
                        out StageOneCardEffectStyle style))
                {
                    throw new InvalidOperationException(
                        "Card VFX style could not be resolved for gallery " +
                        "card at index " + i + ".");
                }

                result[i] = style;
            }

            return result;
        }

        /// <summary>
        /// Registers deterministic generic styles for cards that do not yet
        /// have an authored palette entry. This keeps a newly added data card
        /// visible in Stage01 and TestLab without requiring a C# palette edit.
        /// Exact authored card IDs keep their tuned style. A registered module
        /// card ID takes precedence over a similarly spelled presentation alias.
        /// </summary>
        public static void RegisterContent(CompiledContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            GeneratedCardStyles.Clear();
            CompiledCardDefinition[] cards = content.Cards;
            for (int i = 0; i < cards.Length; i++)
            {
                CompiledCardDefinition card = cards[i];
                if (card == null ||
                    string.IsNullOrWhiteSpace(card.StableId) ||
                    AuthoredStyleIds.Contains(card.StableId))
                {
                    continue;
                }

                GeneratedCardStyles[card.StableId] =
                    CreateGeneratedCardStyle(
                        card.StableId,
                        card.Tier);
            }
        }

        public static bool TryGetStyle(
            string effectId,
            out StageOneCardEffectStyle style)
        {
            if (TryGetCardStyle(effectId, out style))
            {
                return true;
            }

            return TryGetEventStyle(effectId, out style);
        }

        /// <summary>
        /// Resolves a stable card id only. Generated module-card styles are
        /// intentionally checked before presentation aliases, while an
        /// unregistered unknown id is rejected so missing content
        /// registration cannot be hidden by the semantic-event fallback.
        /// </summary>
        public static bool TryGetCardStyle(
            string cardId,
            out StageOneCardEffectStyle style)
        {
            if (!string.IsNullOrEmpty(cardId) &&
                GeneratedCardStyles.TryGetValue(cardId, out style))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(cardId) &&
                StyleIndices.TryGetValue(cardId, out int index))
            {
                style = Styles[index];
                return true;
            }

            style = default;
            return false;
        }

        /// <summary>
        /// Resolves status/effect event ids independently from card ids.
        /// Authored semantic aliases win even when a module card happens to
        /// use the same exact string.
        /// </summary>
        public static bool TryGetEventStyle(
            string effectId,
            out StageOneCardEffectStyle style)
        {
            if (!string.IsNullOrEmpty(effectId) &&
                StyleIndices.TryGetValue(effectId, out int index))
            {
                style = Styles[index];
                return true;
            }

            if (!string.IsNullOrEmpty(effectId) &&
                GeneratedEventStyles.TryGetValue(
                    effectId,
                    out style))
            {
                return true;
            }

            // 의미 사건이 전용 alias보다 먼저 추가되어도 연출이 조용히
            // 사라지지 않게 한다. 카드가 아닌 파생 event id도 같은 안정
            // 문자열에서 공용 스타일을 만든다.
            if (!string.IsNullOrWhiteSpace(effectId))
            {
                style = CreateGeneratedCardStyle(
                    effectId,
                    CardTier.Common);
                GeneratedEventStyles[effectId] = style;
                return true;
            }

            style = default;
            return false;
        }

        public static StageOneCardEffectStyle
            CreateGeneratedCardStyle(
                string stableId,
                CardTier tier)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < stableId.Length; i++)
            {
                char character = stableId[i];
                hash = (hash ^ (byte)character) * 16777619u;
                hash = (hash ^ (byte)(character >> 8)) * 16777619u;
            }

            int tierValue = Mathf.Clamp((int)tier, 1, 5);
            byte primaryR = (byte)(80 + (hash & 0x7f));
            byte primaryG = (byte)(80 + ((hash >> 7) & 0x7f));
            byte primaryB = (byte)(80 + ((hash >> 14) & 0x7f));
            byte secondaryR = (byte)Mathf.Min(255, primaryR + 48);
            byte secondaryG = (byte)Mathf.Min(255, primaryG + 48);
            byte secondaryB = (byte)Mathf.Min(255, primaryB + 48);
            StageOneCardEffectShape shape =
                (StageOneCardEffectShape)(
                    (int)((hash >> 21) % 20u));

            return Style(
                stableId,
                primaryR,
                primaryG,
                primaryB,
                secondaryR,
                secondaryG,
                secondaryB,
                shape,
                StandardEffectDuration,
                0.54f + (tierValue * 0.08f),
                0.055f + (tierValue * 0.006f),
                0.08f + (tierValue * 0.04f));
        }

        /// <summary>
        /// Resolves a visual bit by the append-only palette index contract.
        /// Callers can keep legacy priority rules below a tier boundary while
        /// all newly appended cards share one deterministic resolver.
        /// </summary>
        public static string ResolveHighestSetEffectId(
            CardEffectVisualFlags flags,
            int minimumStyleIndex)
        {
            ulong rawFlags = (ulong)flags;
            int firstIndex = Mathf.Clamp(
                minimumStyleIndex,
                0,
                Styles.Length);
            int lastIndex = Mathf.Min(
                Styles.Length - 1,
                63);
            for (int index = lastIndex;
                 index >= firstIndex;
                 index--)
            {
                if ((rawFlags & (1UL << index)) != 0UL)
                {
                    return Styles[index].Id;
                }
            }

            return string.Empty;
        }

        private static Dictionary<string, int> CreateStyleIndices()
        {
            var result = new Dictionary<string, int>(
                96,
                StringComparer.Ordinal);
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
            AddAlias(result, "duplicate_spawn", "duplicate");
            AddAlias(result, "rare_duplicate_projectile", "duplicate");
            AddAlias(result, "rare_duplicate_enemy", "duplicate");
            AddAlias(result, "rare_duplicate_health_share", "duplicate");
            AddAlias(result, "sacrifice_transfer", "sacrifice");
            AddAlias(result, "rare_sacrifice_projectile", "sacrifice");
            AddAlias(result, "rare_sacrifice_enemy", "sacrifice");
            AddAlias(result, "return_revive", "return");
            AddAlias(result, "rare_return_projectile", "return");
            AddAlias(result, "rare_rewind_enemy", "return");
            AddAlias(result, "retrograde_turn", "retrograde");
            AddAlias(result, "rare_retrograde_projectile", "retrograde");
            AddAlias(result, "rare_retrograde_enemy_start", "retrograde");
            AddAlias(result, "rare_retrograde_enemy_end", "retrograde");
            AddAlias(result, "resonance_link", "resonance");
            AddAlias(result, "resonance_projectile", "resonance");
            AddAlias(result, "resonance_enemy", "resonance");
            AddAlias(result, "absorb_merge", "absorb");
            AddAlias(result, "absorb_projectile", "absorb");
            AddAlias(result, "absorb_enemy", "absorb");
            AddAlias(result, "time_stop_release", "time_stop");
            AddAlias(result, "time_stop_projectile", "time_stop");
            AddAlias(result, "time_stop_store", "time_stop");
            AddAlias(result, "time_stop_enemy", "time_stop");
            AddAlias(result, "time_stop_enemy_release", "time_stop");
            AddAlias(result, "mutation_shift", "mutation");
            AddAlias(result, "mutation_projectile", "mutation");
            AddAlias(result, "mutation_projectile_execute", "mutation");
            AddAlias(result, "mutation_enemy", "mutation");
            AddAlias(result, "execute_hit", "execute");
            AddAlias(result, "execute_mark", "execute");
            AddAlias(result, "parasite_tick", "parasite");
            AddAlias(result, "parasite_attach", "parasite");
            AddAlias(result, "parasite_transfer", "parasite");
            AddAlias(result, "rebirth_spawn", "rebirth");
            AddAlias(result, "chain_transfer", "chain");
            AddAlias(result, "legendary_recursion", "recursion");
            AddAlias(result, "legendary_reverse_order", "reverse_order");
            AddAlias(result, "legendary_dual_interpretation", "dual_interpretation");
            AddAlias(result, "legendary_infinite_orbit", "infinite_orbit");
            AddAlias(result, "legendary_overclone", "overclone");
            AddAlias(result, "legendary_forbidden_deal", "forbidden_deal");
            AddAlias(result, "legendary_last_command", "last_command");
            AddAlias(result, "legendary_fate_lock", "fate_lock");
            AddAlias(result, "legendary_overload", "overload");
            AddAlias(result, "mythic_singularity", "singularity");
            AddAlias(result, "mythic_phoenix_core", "phoenix_core");
            AddAlias(result, "mythic_time_rift", "time_rift");
            AddAlias(result, "mythic_mirror_world", "mirror_world");
            AddAlias(result, "mythic_ouroboros", "ouroboros");
            return result;
        }

        private static HashSet<string> CreateAuthoredStyleIds()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Styles.Length; i++)
            {
                result.Add(Styles[i].Id);
            }

            return result;
        }

        private static void AddAlias(
            Dictionary<string, int> destination,
            string alias,
            string canonicalId)
        {
            int canonicalIndex = destination[canonicalId];
            destination[alias] = canonicalIndex;

            // 이전 OrdinalIgnoreCase 표가 허용하던 일반적인 소문자 event
            // ID는 명시 alias로 보존한다. Dictionary 자체는 Ordinal이라
            // 임의 mixed-case 카드 ID가 기존 표현 alias에 흡수되지는 않는다.
            string lowerAlias = alias.ToLowerInvariant();
            if (!destination.ContainsKey(lowerAlias))
            {
                destination[lowerAlias] = canonicalIndex;
            }
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
