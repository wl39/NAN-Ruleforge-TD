using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// 카드 프로그램이 현재 다루는 주체가 탄환인지 적인지를 구분한다.
    /// 같은 카드가 이 값에 따라 ProjectileProgram 또는 EnemyProgram으로 해석된다.
    /// </summary>
    public enum SubjectType
    {
        /// <summary>날아가는 탄환의 물리 수치와 적중/소멸 행동에 카드를 적용한다.</summary>
        Projectile = 0,
        /// <summary>적 개체의 체력, 이동, 상태와 사망 행동에 카드를 적용한다.</summary>
        Enemy = 1
    }

    /// <summary>
    /// 골드가 어디에서 생성되었는지를 표시한다.
    /// 경제 트리거가 자신이 만든 골드를 다시 읽어 무한 반복하는 것을 막는 데 사용한다.
    /// </summary>
    public enum RewardOrigin
    {
        /// <summary>일반적인 적 처치 보상이다.</summary>
        EnemyDrop = 0,
        /// <summary>골드 획득 카드가 직접 만든 보상이다.</summary>
        CardBounty = 1,
        /// <summary>아케인 포지 같은 타워 트리거가 만든 보상이다.</summary>
        TowerTrigger = 2,
        /// <summary>상점 구매 취소 등으로 돌려받은 골드다.</summary>
        ShopRefund = 3,
        /// <summary>테스트 또는 개발 도구가 만든 골드다.</summary>
        Debug = 4
    }

    /// <summary>
    /// 한 시뮬레이션 틱 안에서 이벤트를 처리하는 고정 단계다.
    /// 숫자가 작은 단계가 먼저 실행되며, 100 단위 간격은 향후 중간 단계를 추가할 여지를 둔다.
    /// </summary>
    public enum EventPhase
    {
        // 입력부터 웨이브 판정까지의 순서를 바꾸면 같은 카드 조합의 결과도 달라질 수 있다.
        // 따라서 이 숫자들은 단순 표시용이 아니라 결정적 전투 규칙의 일부다.
        /// <summary>플레이어 명령을 받아 계획/전투 상태를 바꾸는 단계다.</summary>
        Command = 0,
        /// <summary>이전에 예약해 둔 일반 작업을 깨우는 단계다.</summary>
        Scheduled = 100,
        /// <summary>상태이상 틱과 만료를 처리하는 단계다.</summary>
        Status = 200,
        /// <summary>적과 탄환의 논리 위치를 전진시키는 단계다.</summary>
        Movement = 300,
        /// <summary>이동이 끝난 위치로 공간 검색 자료를 갱신하는 단계다.</summary>
        SpatialIndex = 400,
        /// <summary>타워의 조건과 공격 발동을 처리하는 단계다.</summary>
        Tower = 500,
        /// <summary>탄환 충돌, 적중, 수명 종료를 처리하는 단계다.</summary>
        Projectile = 600,
        /// <summary>요청된 피해를 방어력·저항 규칙에 따라 확정하는 단계다.</summary>
        Damage = 700,
        /// <summary>체력이 0이 된 적의 부활 또는 사망을 확정하는 단계다.</summary>
        Death = 800,
        /// <summary>확정된 처치와 카드 보상을 지급하는 단계다.</summary>
        Reward = 900,
        /// <summary>웨이브 종료, 다음 진행 상태, 승패를 판정하는 단계다.</summary>
        Wave = 1_000,
        /// <summary>화면과 사운드 계층에 전달할 표현 사건을 만드는 마지막 단계다.</summary>
        Presentation = 1_100
    }

    /// <summary>
    /// 이벤트가 표현하는 구체적인 게임 사건의 종류다.
    /// <see cref="EventPhase"/>가 “언제 처리하는가”라면 이 enum은 “무슨 일인가”를 뜻한다.
    /// </summary>
    public enum EventType
    {
        /// <summary>실제 작업이 없는 기본값이다.</summary>
        None = 0,
        /// <summary>플레이어가 제출한 게임 명령이다.</summary>
        Command = 1,
        /// <summary>화상·중독 등 상태이상의 주기 효과다.</summary>
        StatusTick = 2,
        /// <summary>지속시간이 끝난 상태이상이다.</summary>
        StatusExpired = 3,
        /// <summary>적의 경로 진행 위치가 바뀐 사건이다.</summary>
        EnemyMoved = 4,
        /// <summary>타워의 발동 조건이 충족된 사건이다.</summary>
        TowerTriggered = 5,
        /// <summary>장착 카드 배열의 실행을 시작하는 사건이다.</summary>
        ProgramStarted = 6,
        /// <summary>카드 프로그램에서 현재 카드 하나를 실행하는 작업이다.</summary>
        CardExecute = 7,
        /// <summary>새 탄환이 시뮬레이션에 생성된 사건이다.</summary>
        ProjectileSpawned = 8,
        /// <summary>탄환과 적의 충돌이 확인된 사건이다.</summary>
        ProjectileHit = 9,
        /// <summary>수명 또는 소멸 규칙으로 탄환이 끝난 사건이다.</summary>
        ProjectileExpired = 10,
        /// <summary>아직 방어력 등을 적용하지 않은 피해 계산 요청이다.</summary>
        DamageRequested = 11,
        /// <summary>최종 수치가 대상 체력에 반영된 피해 사건이다.</summary>
        DamageApplied = 12,
        /// <summary>대상에게 상태이상 인스턴스를 추가/병합한 사건이다.</summary>
        StatusApplied = 13,
        /// <summary>상태이상 인스턴스를 제거한 사건이다.</summary>
        StatusRemoved = 14,
        /// <summary>적 하나의 가계에서 추가 개체가 만들어진 사건이다.</summary>
        EnemySplit = 15,
        /// <summary>부활 판정까지 마치고 적 사망이 확정된 사건이다.</summary>
        EnemyDied = 16,
        /// <summary>출처 규칙을 통과한 골드가 지급된 사건이다.</summary>
        RewardGranted = 17,
        /// <summary>현재 웨이브 전투가 시작된 사건이다.</summary>
        WaveStarted = 18,
        /// <summary>현재 웨이브의 모든 적 처리가 끝난 사건이다.</summary>
        WaveCompleted = 19,
        /// <summary>웨이브 후 카드 선택지 세 칸이 결정된 사건이다.</summary>
        DraftGenerated = 20,
        /// <summary>논리에는 영향 없이 화면 연출을 위한 사건이다.</summary>
        Presentation = 21
    }

    /// <summary>
    /// 이벤트에 동시에 붙일 수 있는 추가 성격 표식이다.
    /// </summary>
    /// <remarks>
    /// <see cref="FlagsAttribute"/>가 있으므로 비트 OR로 여러 값을 함께 보관할 수 있다.
    /// 예를 들어 범위 지속 피해라면 Area | DamageOverTime처럼 표현한다.
    /// </remarks>
    [Flags]
    public enum EventTags : ulong
    {
        /// <summary>추가 성격 표식이 없다.</summary>
        None = 0UL,
        /// <summary>원본이 아니라 효과로 새로 생성된 대상/사건이다.</summary>
        Generated = 1UL << 0,
        /// <summary>부모나 원본으로부터 규칙을 물려받았다.</summary>
        Inherited = 1UL << 1,
        /// <summary>재귀나 반복 패스에서 다시 실행된 사건이다.</summary>
        Repeated = 1UL << 2,
        /// <summary>하나가 아닌 범위 안의 대상에 적용되는 효과다.</summary>
        Area = 1UL << 3,
        /// <summary>특정 한 대상에 적용되는 효과다.</summary>
        SingleTarget = 1UL << 4,
        /// <summary>화상·중독처럼 시간에 걸쳐 적용되는 피해다.</summary>
        DamageOverTime = 1UL << 5,
        /// <summary>둔화·기절·밀치기 같은 이동/행동 제어다.</summary>
        Control = 1UL << 6,
        /// <summary>골드 생성이나 소비와 관련된 사건이다.</summary>
        Economic = 1UL << 7,
        /// <summary>치명타 판정이 적용된 사건이다.</summary>
        Critical = 1UL << 8,
        /// <summary>탄환 문맥 또는 탄환 자체와 관련된 사건이다.</summary>
        Projectile = 1UL << 9,
        /// <summary>적 문맥 또는 적 자체와 관련된 사건이다.</summary>
        Enemy = 1UL << 10,
        /// <summary>사망 조건이나 사망 후 효과와 관련된 사건이다.</summary>
        Death = 1UL << 11
    }

    /// <summary>진단 기록을 화면이나 로그에서 어느 수준으로 취급할지 나타낸다.</summary>
    public enum DiagnosticSeverity
    {
        /// <summary>정상 실행을 이해하는 데 도움이 되는 참고 정보다.</summary>
        Info = 0,
        /// <summary>효과 일부가 안전 상한에 걸리는 등 확인이 필요한 상황이다.</summary>
        Warning = 1,
        /// <summary>잘못된 이벤트처럼 구현 또는 데이터 수정이 필요한 상황이다.</summary>
        Error = 2
    }

    /// <summary>
    /// 안전장치가 효과를 거절하거나 이상 상태를 발견한 구체적인 이유다.
    /// 게임 플레이 결과 대신 개발용 진단 버퍼에 남는다.
    /// </summary>
    public enum DiagnosticCode
    {
        /// <summary>진단 원인이 없는 기본값이다.</summary>
        None = 0,
        /// <summary>파생 이벤트 깊이가 연쇄 상한에 도달했다.</summary>
        ChainDepthLimitReached = 1,
        /// <summary>한 연쇄가 허용된 이벤트 수를 모두 사용했다.</summary>
        ChainEventBudgetExceeded = 2,
        /// <summary>한 연쇄가 허용된 탄환 생성 수를 모두 사용했다.</summary>
        ProjectileSpawnBudgetExceeded = 3,
        /// <summary>적 가계의 분열 횟수 또는 누적 개체 수 한도에 도달했다.</summary>
        EnemyLineageLimitReached = 4,
        /// <summary>한 연쇄의 카드 실행 횟수 한도에 도달했다.</summary>
        CardTriggerLimitReached = 5,
        /// <summary>탄환이 허용된 최대 수명에 도달해 제거되었다.</summary>
        ProjectileLifetimeLimitReached = 6,
        /// <summary>현재 틱 전체의 이벤트 처리 한도에 도달했다.</summary>
        TickEventBudgetExceeded = 7,
        /// <summary>고정 용량 이벤트 큐에 더 넣을 공간이 없다.</summary>
        EventQueueCapacityExceeded = 8,
        /// <summary>필수 값이나 대상이 유효하지 않은 이벤트를 발견했다.</summary>
        InvalidEvent = 9,
        /// <summary>정수 범위를 넘는 계산을 적용 전에 차단했다.</summary>
        IntegerOverflowPrevented = 10,
        /// <summary>동시에 유지할 수 있는 위험 지대 수에 도달했다.</summary>
        ActiveHazardLimitReached = 11
    }

    /// <summary>
    /// 이벤트/생성/반복 예산 예약이 실패한 이유다.
    /// 효과 시스템은 이 값을 진단 코드로 변환해 어떤 상한에 걸렸는지 설명한다.
    /// </summary>
    public enum BudgetFailure
    {
        /// <summary>예약이 성공했으며 실패 원인이 없다.</summary>
        None = 0,
        /// <summary>음수 개수 등 예약 요청 자체가 잘못되었다.</summary>
        InvalidRequest = 1,
        /// <summary>요청한 파생 깊이가 최대 연쇄 깊이를 넘는다.</summary>
        ChainDepthLimit = 2,
        /// <summary>연쇄 이벤트 총량 상한을 넘는다.</summary>
        ChainEventLimit = 3,
        /// <summary>연쇄 탄환 생성 총량 상한을 넘는다.</summary>
        ProjectileSpawnLimit = 4,
        /// <summary>연쇄 카드 실행 총량 상한을 넘는다.</summary>
        CardTriggerLimit = 5,
        /// <summary>적 가계의 분열 발동 횟수 상한을 넘는다.</summary>
        EnemySplitLimit = 6,
        /// <summary>적 가계의 누적 개체 생성 수 상한을 넘는다.</summary>
        EnemyLineageEntityLimit = 7,
        /// <summary>한 연쇄에서 허용한 재귀 패스 수를 넘는다.</summary>
        RecursionLimit = 8,
        /// <summary>한 연쇄에서 허용한 신화 반복 패스 수를 넘는다.</summary>
        MythicRepeatLimit = 9,
        /// <summary>현재 틱 전체 이벤트 수 상한을 넘는다.</summary>
        TickEventLimit = 10
    }
}
