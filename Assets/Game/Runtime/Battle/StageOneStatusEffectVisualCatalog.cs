using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// 시뮬레이션 상태와 카드 VFX 정체성 사이의 읽기 전용 표시 계약이다.
    /// 전투 규칙이나 상태 수치를 소유하지 않고, 여러 적 표시 컴포넌트가 같은
    /// 색상·플래그 매핑을 중복 구현하지 않도록 한곳에서만 관리한다.
    /// </summary>
    public readonly struct StageOneStatusEffectVisualDefinition
    {
        public StageOneStatusEffectVisualDefinition(
            CardEffectVisualFlags flag,
            string effectId,
            bool showDebuffIcon,
            string nameKey,
            string descriptionKey)
        {
            Flag = flag;
            EffectId = effectId ?? string.Empty;
            ShowDebuffIcon = showDebuffIcon;
            NameKey = nameKey ?? string.Empty;
            DescriptionKey = descriptionKey ?? string.Empty;
        }

        public CardEffectVisualFlags Flag { get; }
        public string EffectId { get; }
        public bool ShowDebuffIcon { get; }
        public string NameKey { get; }
        public string DescriptionKey { get; }
    }

    public static class StageOneStatusEffectVisualCatalog
    {
        public static bool TryGet(
            StatusType type,
            out StageOneStatusEffectVisualDefinition definition)
        {
            switch (type)
            {
                case StatusType.Burn:
                    definition = Entry(
                        CardEffectVisualFlags.Burn,
                        "burn");
                    return true;
                case StatusType.Poison:
                    definition = Entry(
                        CardEffectVisualFlags.Poison,
                        "poison");
                    return true;
                case StatusType.Slow:
                    definition = Entry(
                        CardEffectVisualFlags.Slow,
                        "slow");
                    return true;
                case StatusType.Mark:
                    definition = Entry(
                        CardEffectVisualFlags.Mark,
                        "mark");
                    return true;
                case StatusType.Pierced:
                    definition = Entry(
                        CardEffectVisualFlags.Pierce,
                        "pierce");
                    return true;
                case StatusType.Stun:
                    definition = Entry(
                        CardEffectVisualFlags.Stun,
                        "stun");
                    return true;
                case StatusType.Ricochet:
                    definition = Entry(
                        CardEffectVisualFlags.Ricochet,
                        "ricochet");
                    return true;
                case StatusType.Bleed:
                    definition = Entry(
                        CardEffectVisualFlags.Bleed,
                        "bleed");
                    return true;
                case StatusType.HomingPriority:
                    definition = Entry(
                        CardEffectVisualFlags.Homing,
                        "homing");
                    return true;
                case StatusType.Delay:
                    definition = Entry(
                        CardEffectVisualFlags.Delay,
                        "delay");
                    return true;
                case StatusType.Curse:
                    definition = Entry(
                        CardEffectVisualFlags.Curse,
                        "curse");
                    return true;
                case StatusType.Bind:
                    definition = Entry(
                        CardEffectVisualFlags.Bind,
                        "bind");
                    return true;
                case StatusType.Airborne:
                    definition = Entry(
                        CardEffectVisualFlags.Airborne,
                        "airborne");
                    return true;
                case StatusType.Shock:
                    definition = Entry(
                        CardEffectVisualFlags.Shock,
                        "shock");
                    return true;
                case StatusType.Chill:
                case StatusType.Frozen:
                    definition = Entry(
                        CardEffectVisualFlags.Freeze,
                        "freeze");
                    return true;
                case StatusType.FreezeImmunity:
                    definition = Entry(
                        CardEffectVisualFlags.Freeze,
                        "freeze",
                        false);
                    return true;
                case StatusType.Afterimage:
                    definition = Entry(
                        CardEffectVisualFlags.Afterimage,
                        "afterimage");
                    return true;
                case StatusType.Pulse:
                    definition = Entry(
                        CardEffectVisualFlags.Pulse,
                        "pulse");
                    return true;
                case StatusType.Magnet:
                    definition = Entry(
                        CardEffectVisualFlags.Magnet,
                        "magnet");
                    return true;
                case StatusType.Reflect:
                    definition = Entry(
                        CardEffectVisualFlags.Reflect,
                        "reflect");
                    return true;
                case StatusType.Contagion:
                    definition = Entry(
                        CardEffectVisualFlags.Contagion,
                        "contagion");
                    return true;
                case StatusType.Seal:
                    definition = Entry(
                        CardEffectVisualFlags.Seal,
                        "seal");
                    return true;
                case StatusType.Corrosion:
                    definition = Entry(
                        CardEffectVisualFlags.Corrosion,
                        "corrosion");
                    return true;
                case StatusType.Orbit:
                    definition = Entry(
                        CardEffectVisualFlags.Orbit,
                        "orbit");
                    return true;
                case StatusType.Lifesteal:
                    definition = Entry(
                        CardEffectVisualFlags.Lifesteal,
                        "lifesteal");
                    return true;
                case StatusType.Fear:
                    definition = Entry(
                        CardEffectVisualFlags.Fear,
                        "fear");
                    return true;
                case StatusType.FearHaste:
                    definition = Entry(
                        CardEffectVisualFlags.Fear,
                        "fear",
                        false);
                    return true;
                default:
                    definition =
                        default(StageOneStatusEffectVisualDefinition);
                    return false;
            }
        }

        public static CardEffectVisualFlags ToVisualFlag(
            StatusType type)
        {
            return TryGet(type, out var definition)
                ? definition.Flag
                : CardEffectVisualFlags.None;
        }

        private static StageOneStatusEffectVisualDefinition Entry(
            CardEffectVisualFlags flag,
            string effectId,
            bool showDebuffIcon = true)
        {
            string prefix =
                "status_effect." + effectId;
            return new StageOneStatusEffectVisualDefinition(
                flag,
                effectId,
                showDebuffIcon,
                prefix + ".name",
                prefix + ".description");
        }
    }
}
