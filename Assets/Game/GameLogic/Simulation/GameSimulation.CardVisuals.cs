using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 카드 실행 결과를 전투 판정과 분리된 투사체 표시 플래그로 기록한다.
    /// 카드가 물리 수치만 바꾸는 경우에도 화면 계층이 카드 정체성을 추측하지
    /// 않고 고유 색상과 궤적을 유지할 수 있게 한다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private void MarkProjectileCardVisual(
            EntityId projectileId,
            CompiledCardDefinition card)
        {
            ProjectileState projectile =
                FindProjectile(projectileId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            projectile.VisualFlags |=
                GetCardVisualFlag(card);
        }

        internal void MarkEnemyCardVisual(
            EntityId enemyId,
            CompiledCardDefinition card)
        {
            EnemyState enemy = FindEnemy(enemyId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            enemy.VisualFlags |=
                GetCardVisualFlag(card);
        }

        public static CardEffectVisualFlags
            GetCardVisualFlag(CompiledCardDefinition card)
        {
            return card == null
                ? CardEffectVisualFlags.None
                : (CardEffectVisualFlags)
                    card.VisualEffectFlag;
        }

        internal CardEffectVisualFlags
            GetProjectileImpactVisualFlags(
                ProjectileState projectile)
        {
            if (projectile == null)
            {
                return CardEffectVisualFlags.None;
            }

            return projectile.VisualFlags |
                GetCommonProjectileVisualFlags(projectile) |
                GetProjectileUncommonEffectFlags(projectile.Id);
        }

        internal static CardEffectVisualFlags
            GetEnemyDeathVisualFlags(EnemyState enemy)
        {
            if (enemy == null)
            {
                return CardEffectVisualFlags.None;
            }

            CardEffectVisualFlags result =
                enemy.VisualFlags;
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status == null ||
                    status.Stacks <= 0 ||
                    status.RemainingTicks <= 0)
                {
                    continue;
                }

                result |= GetStatusVisualFlag(status.Type);
            }

            return result;
        }

        private static CardEffectVisualFlags
            GetStatusVisualFlag(StatusType type)
        {
            switch (type)
            {
                case StatusType.Burn:
                    return CardEffectVisualFlags.Burn;
                case StatusType.Poison:
                    return CardEffectVisualFlags.Poison;
                case StatusType.Slow:
                    return CardEffectVisualFlags.Slow;
                case StatusType.Mark:
                    return CardEffectVisualFlags.Mark;
                case StatusType.Pierced:
                    return CardEffectVisualFlags.Pierce;
                case StatusType.Stun:
                    return CardEffectVisualFlags.Stun;
                case StatusType.Ricochet:
                    return CardEffectVisualFlags.Ricochet;
                case StatusType.Bleed:
                    return CardEffectVisualFlags.Bleed;
                case StatusType.HomingPriority:
                    return CardEffectVisualFlags.Homing;
                case StatusType.Delay:
                    return CardEffectVisualFlags.Delay;
                case StatusType.Curse:
                    return CardEffectVisualFlags.Curse;
                case StatusType.Bind:
                    return CardEffectVisualFlags.Bind;
                case StatusType.Airborne:
                    return CardEffectVisualFlags.Airborne;
                case StatusType.Shock:
                    return CardEffectVisualFlags.Shock;
                case StatusType.Chill:
                case StatusType.Frozen:
                case StatusType.FreezeImmunity:
                    return CardEffectVisualFlags.Freeze;
                case StatusType.Afterimage:
                    return CardEffectVisualFlags.Afterimage;
                case StatusType.Pulse:
                    return CardEffectVisualFlags.Pulse;
                case StatusType.Magnet:
                    return CardEffectVisualFlags.Magnet;
                case StatusType.Reflect:
                    return CardEffectVisualFlags.Reflect;
                case StatusType.Contagion:
                    return CardEffectVisualFlags.Contagion;
                case StatusType.Seal:
                    return CardEffectVisualFlags.Seal;
                case StatusType.Corrosion:
                    return CardEffectVisualFlags.Corrosion;
                case StatusType.Orbit:
                    return CardEffectVisualFlags.Orbit;
                case StatusType.Lifesteal:
                    return CardEffectVisualFlags.Lifesteal;
                case StatusType.Fear:
                case StatusType.FearHaste:
                    return CardEffectVisualFlags.Fear;
                default:
                    return CardEffectVisualFlags.None;
            }
        }
    }
}
