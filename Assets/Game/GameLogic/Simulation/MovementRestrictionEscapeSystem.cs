using System;
using RuleforgeTD.GameLogic.Content;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 적의 전진을 줄이거나 완전히 소비하는 이동 제한의 독립 분류다.
    /// 새 제어 효과를 추가할 때 이 분류에 연결하면 장기 고정 탈출 규칙도 함께 적용된다.
    /// </summary>
    [Flags]
    internal enum MovementRestrictionCategory
    {
        None = 0,
        SpeedReduction = 1 << 0,
        HardControlStatus = 1 << 1,
        TemporalLockRuntime = 1 << 2
    }

    public sealed partial class GameSimulation
    {
        /// <summary>
        /// 동일 경로 진행도에서 이동 제한을 받고 있는 시간을 추적한다.
        /// 설정된 상한에 도달하면 일정 시간 모든 이동 제한 범주를 무시하되,
        /// 상태 수명과 지속 피해 틱은 제거하거나 멈추지 않는다.
        /// </summary>
        private bool UpdateMovementRestrictionEscape(
            EnemyState enemy)
        {
            if (enemy == null || !enemy.Alive)
            {
                return false;
            }

            if (enemy.MovementEscapeUntilTick > tick)
            {
                // 탈출 중에도 시간 정지 런타임의 자연 만료와 해제 이벤트는
                // 원래 틱에 처리한다. 이동 판정에서만 결과를 무시한다.
                IsEnemyRareTimeStopped(enemy);
                return true;
            }

            if (!enemy.MovementEscapeWatchInitialized ||
                enemy.MovementEscapeWatchProgressMilli !=
                    enemy.PathProgressMilli)
            {
                enemy.MovementEscapeWatchInitialized = true;
                enemy.MovementEscapeWatchProgressMilli =
                    enemy.PathProgressMilli;
                enemy.MovementEscapeStationarySinceTick = tick;
                enemy.MovementEscapeUntilTick = 0;
            }

            MovementRestrictionCategory categories =
                ResolveMovementRestrictionCategories(enemy);
            if (categories == MovementRestrictionCategory.None)
            {
                enemy.MovementEscapeStationarySinceTick = tick;
                return false;
            }

            if (tick - enemy.MovementEscapeStationarySinceTick <
                run.MovementEscapeStationaryTicks)
            {
                return false;
            }

            enemy.MovementEscapeUntilTick = checked(
                tick + run.MovementEscapeImmunityTicks);
            enemy.MovementEscapeStationarySinceTick = tick;
            AddPresentation(
                PresentationEventType.EffectTriggered,
                enemy.Id.Value,
                -1,
                (int)categories,
                "movement_escape");
            return true;
        }

        private MovementRestrictionCategory
            ResolveMovementRestrictionCategories(EnemyState enemy)
        {
            MovementRestrictionCategory categories =
                MovementRestrictionCategory.None;
            if (GetSlowBps(enemy) > 0 ||
                enemy.SpeedMultiplierBps < 10000)
            {
                categories |=
                    MovementRestrictionCategory.SpeedReduction;
            }

            if (HasActiveStatus(enemy, StatusType.Stun) ||
                HasActiveStatus(enemy, StatusType.Delay) ||
                HasActiveStatus(enemy, StatusType.Bind) ||
                HasActiveStatus(enemy, StatusType.Airborne) ||
                HasActiveStatus(enemy, StatusType.Frozen))
            {
                categories |=
                    MovementRestrictionCategory.HardControlStatus;
            }

            if (IsEnemyRareTimeStopped(enemy))
            {
                categories |=
                    MovementRestrictionCategory.TemporalLockRuntime;
            }

            return categories;
        }
    }
}
