namespace RuleforgeTD.GameLogic.Content
{
    /// <summary>
    /// 현재 시뮬레이션이 실제로 실행할 수 있는 타워 문법 한 가지를 정의한다.
    /// 콘텐츠 컴파일러와 런타임 트리거 레지스트리가 같은 계약을 사용하므로,
    /// 데이터에만 존재하고 실행 시 조용히 무시되는 Subject/Selector 조합을 만들 수 없다.
    /// </summary>
    internal readonly struct TowerExecutionContract
    {
        private static readonly TowerExecutionContract[] Supported =
        {
            new TowerExecutionContract(
                TowerTrigger.Attack,
                SubjectTypeMode.Projectile,
                SubjectSelector.PrimaryProjectile),
            new TowerExecutionContract(
                TowerTrigger.EnemyEnteredRange,
                SubjectTypeMode.Enemy,
                SubjectSelector.EnteringEnemy),
            new TowerExecutionContract(
                TowerTrigger.EnemyDied,
                SubjectTypeMode.Enemy,
                SubjectSelector.EnemiesNearEvent)
        };

        private TowerExecutionContract(
            TowerTrigger trigger,
            SubjectTypeMode subjectTypeMode,
            SubjectSelector selector)
        {
            Trigger = trigger;
            SubjectMode = subjectTypeMode;
            Selector = selector;
        }

        public TowerTrigger Trigger { get; }

        public SubjectTypeMode SubjectMode { get; }

        public SubjectSelector Selector { get; }

        /// <summary>
        /// 트리거에 대응하는 현재 실행 계약을 반환한다.
        /// 새 트리거는 실행 핸들러와 이 계약을 함께 추가해야 콘텐츠에서 사용할 수 있다.
        /// </summary>
        public static bool TryGet(
            TowerTrigger trigger,
            out TowerExecutionContract contract)
        {
            for (int index = 0; index < Supported.Length; index++)
            {
                if (Supported[index].Trigger == trigger)
                {
                    contract = Supported[index];
                    return true;
                }
            }

            contract = default;
            return false;
        }

        /// <summary>
        /// Trigger, SubjectTypeMode, SubjectSelector가 구현된 한 문법을 이루는지 검사한다.
        /// Alternating과 Inherit는 설계 enum에는 남겨 두되 실행기가 생기기 전까지 로딩을 거절한다.
        /// </summary>
        public static bool TryValidate(
            TowerTrigger trigger,
            SubjectTypeMode subjectTypeMode,
            SubjectSelector selector,
            out string error)
        {
            if (subjectTypeMode == SubjectTypeMode.Alternating ||
                subjectTypeMode == SubjectTypeMode.Inherit)
            {
                error =
                    "subject mode '" + subjectTypeMode +
                    "' is declared for future content but has no runtime handler.";
                return false;
            }

            if (!TryGet(trigger, out TowerExecutionContract contract))
            {
                error =
                    "trigger '" + trigger +
                    "' has no runtime handler.";
                return false;
            }

            if (contract.SubjectMode != subjectTypeMode ||
                contract.Selector != selector)
            {
                error =
                    "trigger '" + trigger + "' requires subject mode '" +
                    contract.SubjectMode + "' and selector '" +
                    contract.Selector + "', but received '" +
                    subjectTypeMode + "' and '" + selector + "'.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
