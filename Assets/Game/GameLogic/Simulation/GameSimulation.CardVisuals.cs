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
            string stableCardId)
        {
            ProjectileState projectile =
                FindProjectile(projectileId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            projectile.VisualFlags |=
                GetCardVisualFlag(stableCardId);
        }

        internal void MarkEnemyCardVisual(
            EntityId enemyId,
            string stableCardId)
        {
            EnemyState enemy = FindEnemy(enemyId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            enemy.VisualFlags |=
                GetCardVisualFlag(stableCardId);
        }

        public static ProjectileEffectVisualFlags
            GetCardVisualFlag(string stableCardId)
        {
            switch (stableCardId)
            {
                case "split":
                    return ProjectileEffectVisualFlags.Split;
                case "pierce":
                    return ProjectileEffectVisualFlags.Pierce;
                case "burn":
                    return ProjectileEffectVisualFlags.Burn;
                case "slow":
                    return ProjectileEffectVisualFlags.Slow;
                case "explode":
                    return ProjectileEffectVisualFlags.Explode;
                case "knockback":
                    return ProjectileEffectVisualFlags.Knockback;
                case "mark":
                    return ProjectileEffectVisualFlags.Mark;
                case "gold_bounty":
                    return ProjectileEffectVisualFlags.GoldBounty;
                case "poison":
                    return ProjectileEffectVisualFlags.Poison;
                case "enlarge":
                    return ProjectileEffectVisualFlags.Enlarge;
                case "shrink":
                    return ProjectileEffectVisualFlags.Shrink;
                case "stun":
                    return ProjectileEffectVisualFlags.Stun;
                case "ricochet":
                    return ProjectileEffectVisualFlags.Ricochet;
                case "bleed":
                    return ProjectileEffectVisualFlags.Bleed;
                case "accelerate":
                    return ProjectileEffectVisualFlags.Accelerate;
                case "homing":
                    return ProjectileEffectVisualFlags.Homing;
                case "delay":
                    return ProjectileEffectVisualFlags.Delay;
                case "curse":
                    return ProjectileEffectVisualFlags.Curse;
                case "bind":
                    return ProjectileEffectVisualFlags.Bind;
                case "airborne":
                    return ProjectileEffectVisualFlags.Airborne;
                case "shock":
                    return ProjectileEffectVisualFlags.Shock;
                case "freeze":
                    return ProjectileEffectVisualFlags.Freeze;
                case "afterimage":
                    return ProjectileEffectVisualFlags.Afterimage;
                case "pulse":
                    return ProjectileEffectVisualFlags.Pulse;
                case "magnet":
                    return ProjectileEffectVisualFlags.Magnet;
                case "reflect":
                    return ProjectileEffectVisualFlags.Reflect;
                case "contagion":
                    return ProjectileEffectVisualFlags.Contagion;
                case "seal":
                    return ProjectileEffectVisualFlags.Seal;
                case "corrosion":
                    return ProjectileEffectVisualFlags.Corrosion;
                case "orbit":
                    return ProjectileEffectVisualFlags.Orbit;
                case "lifesteal":
                    return ProjectileEffectVisualFlags.Lifesteal;
                case "fear":
                    return ProjectileEffectVisualFlags.Fear;
                default:
                    return ProjectileEffectVisualFlags.None;
            }
        }

        internal ProjectileEffectVisualFlags
            GetProjectileImpactVisualFlags(
                ProjectileState projectile)
        {
            if (projectile == null)
            {
                return ProjectileEffectVisualFlags.None;
            }

            return projectile.VisualFlags |
                GetCommonProjectileVisualFlags(projectile) |
                GetProjectileUncommonEffectFlags(projectile.Id);
        }

        internal static ProjectileEffectVisualFlags
            GetEnemyDeathVisualFlags(EnemyState enemy)
        {
            if (enemy == null)
            {
                return ProjectileEffectVisualFlags.None;
            }

            ProjectileEffectVisualFlags result =
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

        private static ProjectileEffectVisualFlags
            GetStatusVisualFlag(StatusType type)
        {
            switch (type)
            {
                case StatusType.Burn:
                    return ProjectileEffectVisualFlags.Burn;
                case StatusType.Poison:
                    return ProjectileEffectVisualFlags.Poison;
                case StatusType.Slow:
                    return ProjectileEffectVisualFlags.Slow;
                case StatusType.Mark:
                    return ProjectileEffectVisualFlags.Mark;
                case StatusType.Pierced:
                    return ProjectileEffectVisualFlags.Pierce;
                case StatusType.Stun:
                    return ProjectileEffectVisualFlags.Stun;
                case StatusType.Ricochet:
                    return ProjectileEffectVisualFlags.Ricochet;
                case StatusType.Bleed:
                    return ProjectileEffectVisualFlags.Bleed;
                case StatusType.HomingPriority:
                    return ProjectileEffectVisualFlags.Homing;
                case StatusType.Delay:
                    return ProjectileEffectVisualFlags.Delay;
                case StatusType.Curse:
                    return ProjectileEffectVisualFlags.Curse;
                case StatusType.Bind:
                    return ProjectileEffectVisualFlags.Bind;
                case StatusType.Airborne:
                    return ProjectileEffectVisualFlags.Airborne;
                case StatusType.Shock:
                    return ProjectileEffectVisualFlags.Shock;
                case StatusType.Chill:
                case StatusType.Frozen:
                case StatusType.FreezeImmunity:
                    return ProjectileEffectVisualFlags.Freeze;
                case StatusType.Afterimage:
                    return ProjectileEffectVisualFlags.Afterimage;
                case StatusType.Pulse:
                    return ProjectileEffectVisualFlags.Pulse;
                case StatusType.Magnet:
                    return ProjectileEffectVisualFlags.Magnet;
                case StatusType.Reflect:
                    return ProjectileEffectVisualFlags.Reflect;
                case StatusType.Contagion:
                    return ProjectileEffectVisualFlags.Contagion;
                case StatusType.Seal:
                    return ProjectileEffectVisualFlags.Seal;
                case StatusType.Corrosion:
                    return ProjectileEffectVisualFlags.Corrosion;
                case StatusType.Orbit:
                    return ProjectileEffectVisualFlags.Orbit;
                case StatusType.Lifesteal:
                    return ProjectileEffectVisualFlags.Lifesteal;
                case StatusType.Fear:
                case StatusType.FearHaste:
                    return ProjectileEffectVisualFlags.Fear;
                default:
                    return ProjectileEffectVisualFlags.None;
            }
        }
    }
}
