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
    }
}
