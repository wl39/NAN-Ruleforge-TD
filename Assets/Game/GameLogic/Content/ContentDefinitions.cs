using System;

namespace RuleforgeTD.GameLogic.Content
{
    // 이 파일의 클래스 이름에 붙은 DTO는 Data Transfer Object의 약자다.
    // 즉, phase1-content.json의 모양을 거의 그대로 받아 적기 위한 "입력용 그릇"이다.
    // DTO는 JSON 역직렬화를 위해 public 필드와 변경 가능한 배열을 사용하지만,
    // 실제 전투는 이 객체를 직접 읽지 않는다. ContentCompiler가 모든 값을 검증한 뒤
    // CompiledContent의 정수 ID와 방어적으로 복사된 배열로 바꾸어 시뮬레이션에 전달한다.

    /// <summary>
    /// 카드가 전투 규칙을 얼마나 크게 바꾸는지를 나타내는 설계 등급이다.
    /// 숫자값은 JSON의 tier 값과 일치하며, 드래프트 가중치 배열의 순서에도 사용된다.
    /// </summary>
    public enum CardTier
    {
        /// <summary>단순하고 범용적인 원자 효과 중심의 1티어다.</summary>
        Common = 1,

        /// <summary>상태이상·범위·제어가 등장하는 2티어다.</summary>
        Uncommon = 2,

        /// <summary>복제·반복·전염처럼 연쇄를 확장하는 3티어다.</summary>
        Rare = 3,

        /// <summary>카드 실행 순서와 문법을 바꾸는 4티어다.</summary>
        Legendary = 4,

        /// <summary>런의 핵심 규칙을 바꾸는 5티어다.</summary>
        Mythic = 5
    }

    /// <summary>
    /// 카드 효과 노드가 실행할 수 있는 원자 연산의 목록이다.
    /// JSON에는 이 이름을 문자열로 기록하고, 컴파일 시 enum과 EffectRegistry의
    /// 실제 실행 코드로 연결한다. 새 항목만 추가하고 executor를 등록하지 않으면
    /// 콘텐츠 검증이 실패하므로 "데이터에는 있지만 아무 일도 안 하는 카드"를 막는다.
    /// </summary>
    public enum EffectOperation
    {
        /// <summary>원본을 유지하면서 탄환 또는 적 가지 하나를 추가한다.</summary>
        Split = 0,

        /// <summary>탄환 관통 횟수를 늘리거나 적에게 천공 상태를 준다.</summary>
        AddPierce = 1,

        /// <summary>탄환에 적중 시 화상 적용 규칙을 부착한다.</summary>
        BindBurn = 2,

        /// <summary>현재 적에게 화상 상태를 즉시 적용한다.</summary>
        ApplyBurn = 3,

        /// <summary>탄환을 느리게 하는 대신 수명·크기·충돌 범위를 조절한다.</summary>
        ModifyProjectileSlow = 4,

        /// <summary>현재 적에게 이동 둔화를 적용한다.</summary>
        ApplySlow = 5,

        /// <summary>탄환의 첫 적중/소멸 또는 적의 사망에 폭발을 연결한다.</summary>
        BindExplosion = 6,

        /// <summary>탄환에 적중 시 밀치기 규칙을 부착한다.</summary>
        BindKnockback = 7,

        /// <summary>현재 적을 경로 뒤쪽으로 즉시 밀친다.</summary>
        ApplyKnockback = 8,

        /// <summary>탄환에 첫 적중 시 표식 적용 규칙을 부착한다.</summary>
        BindMark = 9,

        /// <summary>현재 적에게 표식 상태를 즉시 적용한다.</summary>
        ApplyMark = 10,

        /// <summary>탄환에 새로운 적 적중 시 제한된 골드 획득 규칙을 부착한다.</summary>
        BindGoldOnHit = 11,

        /// <summary>현재 적이 가진 가계 보상 예산의 지급량을 증가시킨다.</summary>
        IncreaseReward = 12,

        /// <summary>탄환에 적중 시 중독 적용 규칙을 부착한다.</summary>
        BindPoison = 13,

        /// <summary>현재 적에게 중독 상태를 즉시 적용한다.</summary>
        ApplyPoison = 14,

        /// <summary>현재 탄환의 크기·피해·속도 같은 물리 수치를 거대화 배율로 바꾼다.</summary>
        EnlargeProjectile = 15,

        /// <summary>현재 적의 크기·속도·보상을 거대화 규칙으로 바꾼다.</summary>
        EnlargeEnemy = 16,

        /// <summary>현재 탄환의 크기·피해·속도·치명타를 축소 규칙으로 바꾼다.</summary>
        ShrinkProjectile = 17,

        /// <summary>현재 적의 크기·체력·속도를 축소 규칙으로 바꾼다.</summary>
        ShrinkEnemy = 18,

        /// <summary>탄환에 첫 적중 시 기절 적용 규칙을 부착한다.</summary>
        BindStun = 19,

        /// <summary>현재 적에게 기절 또는 정예·보스 제어 게이지를 적용한다.</summary>
        ApplyStun = 20,

        /// <summary>탄환이 적중 후 다른 적에게 튕길 수 있도록 도탄 규칙을 설정한다.</summary>
        ConfigureProjectileRicochet = 21,
        /// <summary>적이 밀치기·에어본될 때 주변 적에게 튕기는 도탄 상태를 적용한다.</summary>
        ApplyEnemyRicochet = 22,
        /// <summary>탄환 적중 시 출혈을 적용하는 규칙을 부착한다.</summary>
        BindBleed = 23,
        /// <summary>적이 이동할 때 피해를 받는 출혈 상태를 적용한다.</summary>
        ApplyBleed = 24,
        /// <summary>탄환 속도와 비행 거리에 따른 피해 증가를 설정한다.</summary>
        AccelerateProjectile = 25,
        /// <summary>적의 이동 속도와 가계 보상 예산을 함께 증가시킨다.</summary>
        AccelerateEnemy = 26,
        /// <summary>탄환이 표식 또는 가까운 적을 계속 추적하게 한다.</summary>
        EnableProjectileHoming = 27,
        /// <summary>적을 유도 탄환의 우선 추적 대상으로 지정한다.</summary>
        ApplyHomingPriority = 28,
        /// <summary>탄환을 잠시 정지시킨 뒤 강화하여 다시 움직이게 한다.</summary>
        DelayProjectile = 29,
        /// <summary>적의 다음 이동 또는 특수 능력 실행을 지연한다.</summary>
        ApplyDelay = 30,

        /// <summary>탄환 적중 시 저주를 적용하는 규칙을 부착한다.</summary>
        BindCurse = 31,
        /// <summary>적에게 상태 피해와 지속시간을 증폭하는 저주를 적용한다.</summary>
        ApplyCurse = 32,
        /// <summary>탄환을 적중 위치에 고정해 주기적인 속박 파동을 만들게 한다.</summary>
        CreateBindTrap = 33,
        /// <summary>적을 일정 시간 이동하지 못하게 속박한다.</summary>
        ApplyBind = 34,
        /// <summary>탄환을 포물선 비행 후 범위 착탄하도록 설정한다.</summary>
        MakeAirborneProjectile = 35,
        /// <summary>적을 공중에 띄운 뒤 착지 충돌 피해를 발생시킨다.</summary>
        ApplyAirborne = 36,
        /// <summary>탄환 적중 시 연쇄 감전을 발생시키는 규칙을 부착한다.</summary>
        BindShock = 37,
        /// <summary>적에게 충전 중첩을 적용하고 최대 중첩에서 방전시킨다.</summary>
        ApplyShock = 38,
        /// <summary>탄환 적중 시 냉기 중첩과 소멸 파편을 만드는 규칙을 부착한다.</summary>
        BindFreeze = 39,
        /// <summary>적에게 냉기를 적용하고 최대 중첩에서 빙결시킨다.</summary>
        ApplyFreeze = 40,
        /// <summary>탄환이 지나간 경로를 약한 유령 탄환이 다시 따라가게 한다.</summary>
        CreateAfterimageProjectile = 41,
        /// <summary>적의 이동 경로에 피해 전달 잔상을 남긴다.</summary>
        ApplyAfterimage = 42,
        /// <summary>탄환이 비행 중 주기적으로 범위 파동을 방출하게 한다.</summary>
        EnableProjectilePulse = 43,
        /// <summary>적이 주기적으로 주변에 상태이상을 퍼뜨리게 한다.</summary>
        ApplyEnemyPulse = 44,
        /// <summary>탄환이 가까운 아군 탄환을 끌어당겨 합체하게 한다.</summary>
        EnableProjectileMagnet = 45,
        /// <summary>적이 주변 아군 탄환을 자신에게 끌어당기게 한다.</summary>
        ApplyEnemyMagnet = 46,
        /// <summary>탄환이 타워로 되돌아왔다가 다시 적을 추적하게 한다.</summary>
        EnableProjectileReflect = 47,
        /// <summary>적중한 탄환을 가까운 다른 적에게 반사시키는 상태를 적용한다.</summary>
        ApplyEnemyReflect = 48,
        /// <summary>탄환이 접촉한 아군 탄환에 상태 카드 하나를 전달하게 한다.</summary>
        EnableProjectileContagion = 49,
        /// <summary>적이 보유한 디버프를 주기적으로 가까운 적에게 옮기게 한다.</summary>
        ApplyEnemyContagion = 50,
        /// <summary>탄환 첫 적중 시 특수 능력을 봉인하는 규칙을 부착한다.</summary>
        BindSeal = 51,
        /// <summary>적의 보호막·치유·순간이동·소환 능력을 봉인한다.</summary>
        ApplySeal = 52,
        /// <summary>탄환 적중 시 방어력을 감소시키는 부식 규칙을 부착한다.</summary>
        BindCorrosion = 53,
        /// <summary>적의 방어력과 최대 체력을 서서히 감소시키는 부식을 적용한다.</summary>
        ApplyCorrosion = 54,
        /// <summary>탄환이 처음 적중한 적 주위를 공전하며 반복 피해를 주게 한다.</summary>
        EnableProjectileOrbit = 55,
        /// <summary>적이 현재 위치 주변을 회전하며 다른 적과 충돌하게 한다.</summary>
        ApplyEnemyOrbit = 56,
        /// <summary>탄환 피해 일부로 본진 체력을 회복하는 규칙을 부착한다.</summary>
        BindLifesteal = 57,
        /// <summary>해당 적에게 가한 피해 일부로 본진 체력을 회복하게 한다.</summary>
        ApplyLifesteal = 58,
        /// <summary>탄환 적중 시 적을 경로 반대 방향으로 도망치게 하는 규칙을 부착한다.</summary>
        BindFear = 59,
        /// <summary>적에게 공포와 종료 후의 일시적 가속을 적용한다.</summary>
        ApplyFear = 60
    }

    /// <summary>
    /// 타워가 카드 프로그램 실행을 시작하는 게임 사건이다.
    /// 같은 카드라도 이 트리거와 대상 문맥이 달라지면 전혀 다른 문장이 된다.
    /// </summary>
    public enum TowerTrigger
    {
        /// <summary>기본 공격을 수행할 때 발동한다.</summary>
        Attack = 0,

        /// <summary>적이 처음 타워 사거리 안으로 들어올 때 발동한다.</summary>
        EnemyEnteredRange = 1,

        /// <summary>적의 사망이 확정되었을 때 발동한다.</summary>
        EnemyDied = 2
    }

    /// <summary>
    /// 타워의 카드 프로그램을 탄환 해석과 적 해석 중 어느 쪽으로 실행할지 정한다.
    /// Alternating과 Inherit는 이후 확장 타워를 위한 문법 계약도 포함한다.
    /// </summary>
    public enum SubjectTypeMode
    {
        /// <summary>장착 카드의 ProjectileEffects를 실행한다.</summary>
        Projectile = 0,

        /// <summary>장착 카드의 EnemyEffects를 실행한다.</summary>
        Enemy = 1,

        /// <summary>발동마다 탄환과 적 문맥을 번갈아 사용하는 확장 모드다.</summary>
        Alternating = 2,

        /// <summary>인접/원본 발동의 대상 문맥을 이어받는 확장 모드다.</summary>
        Inherit = 3
    }

    /// <summary>
    /// 트리거가 발생했을 때 카드 효과를 받을 구체적인 대상을 고르는 방식이다.
    /// 예를 들어 발사 직후 탄환, 사거리에 진입한 적, 사망 지점 주변 적을 구분한다.
    /// </summary>
    public enum SubjectSelector
    {
        /// <summary>타워가 방금 만든 주 탄환을 대상으로 삼는다.</summary>
        PrimaryProjectile = 0,

        /// <summary>사거리에 진입해 트리거를 일으킨 그 적을 대상으로 삼는다.</summary>
        EnteringEnemy = 1,

        /// <summary>사망 등 사건 위치 주변의 적들을 안정 정렬해 대상으로 삼는다.</summary>
        EnemiesNearEvent = 2
    }

    /// <summary>
    /// 중앙 StatusSystem이 추적하는 Phase 1 상태이상 종류다.
    /// 카드의 "부여 예약" 연산과 "즉시 적용" 연산은 최종적으로 이 값으로 합쳐진다.
    /// </summary>
    public enum StatusType
    {
        /// <summary>짧은 주기로 화염 지속 피해를 주는 중첩 상태다.</summary>
        Burn = 0,

        /// <summary>긴 시간 독 지속 피해를 주는 중첩 상태다.</summary>
        Poison = 1,

        /// <summary>합산 상한 60%를 적용받는 이동 속도 감소 상태다.</summary>
        Slow = 2,

        /// <summary>받는 피해와 타워 목표 우선순위를 높이는 상태다.</summary>
        Mark = 3,

        /// <summary>해당 적을 맞힌 탄환의 통과와 방어력 무시를 돕는 천공 상태다.</summary>
        Pierced = 4,

        /// <summary>일반 적을 멈추고 정예·보스에는 제어 게이지로 변환되는 상태다.</summary>
        Stun = 5,
        /// <summary>밀치기·에어본 시 주변 적에게 튕기는 충돌 상태다.</summary>
        Ricochet = 6,
        /// <summary>이동하거나 강제 이동할 때 추가 물리 피해를 받는 상태다.</summary>
        Bleed = 7,
        /// <summary>유도 탄환의 표적 선택에서 우선되는 상태다.</summary>
        HomingPriority = 8,
        /// <summary>다음 이동 또는 특수 능력 실행을 늦추는 상태다.</summary>
        Delay = 9,
        /// <summary>상태이상 피해와 지속시간을 증폭하는 상태다.</summary>
        Curse = 10,
        /// <summary>적의 이동만 멈추는 속박 상태다.</summary>
        Bind = 11,
        /// <summary>적이 공중에 떠 있어 이동하지 못하는 상태다.</summary>
        Airborne = 12,
        /// <summary>최대 중첩에서 주변으로 방전되는 충전 상태다.</summary>
        Shock = 13,
        /// <summary>최대 중첩에서 빙결로 전환되는 냉기 상태다.</summary>
        Chill = 14,
        /// <summary>적의 이동과 행동을 잠시 멈추는 빙결 상태다.</summary>
        Frozen = 15,
        /// <summary>빙결 직후 반복 빙결을 막는 짧은 면역 상태다.</summary>
        FreezeImmunity = 16,
        /// <summary>적의 이동 경로에 피해 전달 잔상을 남기는 상태다.</summary>
        Afterimage = 17,
        /// <summary>주기적으로 주변에 효과를 퍼뜨리는 파동 상태다.</summary>
        Pulse = 18,
        /// <summary>주변 탄환을 적 쪽으로 끌어당기는 자석 상태다.</summary>
        Magnet = 19,
        /// <summary>적중 탄환을 다른 적에게 반사시키는 상태다.</summary>
        Reflect = 20,
        /// <summary>주기적으로 디버프 하나를 가까운 적에게 옮기는 상태다.</summary>
        Contagion = 21,
        /// <summary>적의 특수 능력 실행을 막는 봉인 상태다.</summary>
        Seal = 22,
        /// <summary>방어력과 최대 체력을 점진적으로 낮추는 부식 상태다.</summary>
        Corrosion = 23,
        /// <summary>적이 현재 위치 주변을 회전하게 하는 상태다.</summary>
        Orbit = 24,
        /// <summary>해당 적에게 가한 피해 일부를 본진 회복으로 전환하는 상태다.</summary>
        Lifesteal = 25,
        /// <summary>적을 경로 반대 방향으로 이동시키는 공포 상태다.</summary>
        Fear = 26,
        /// <summary>공포가 끝난 뒤 잠시 적용되는 이동 속도 증가 상태다.</summary>
        FearHaste = 27
    }

    /// <summary>
    /// 일반 적과 제어 저항 게이지를 사용하는 정예·보스를 구분하는 등급이다.
    /// </summary>
    public enum EnemyRank
    {
        /// <summary>기절 등 일반 제어 효과를 직접 받는 보통 적이다.</summary>
        Normal = 0,

        /// <summary>강한 제어를 저항 게이지로 변환하는 정예 적이다.</summary>
        Elite = 1,

        /// <summary>강한 제어를 저항 게이지로 변환하는 보스 적이다.</summary>
        Boss = 2
    }

    /// <summary>
    /// 보스가 고정 틱 시뮬레이션에서 실행할 데이터 기반 핵심 능력이다.
    /// 렌더링 에셋이나 애니메이션 이름과는 독립적이다.
    /// </summary>
    public enum BossAbilityType
    {
        None = 0,
        Shield = 1,
        Summon = 2,
        Teleport = 3
    }

    /// <summary>
    /// JSON에 적힌 카드 효과 한 단계를 그대로 받는 입력 레코드다.
    /// 각 숫자 필드의 의미는 operation마다 다르며 ContentCompiler가 조합과 범위를 검증한다.
    /// 사용하지 않는 칸은 0으로 두어 하나의 공통 형식으로 여러 효과를 표현한다.
    /// </summary>
    [Serializable]
    public sealed class EffectNodeDto
    {
        /// <summary>EffectOperation 이름. 대소문자는 구분하지 않지만 오탈자는 허용하지 않는다.</summary>
        public string operation;

        // amount 계열은 효과별 주요 수치다. 예를 들어 피해량, 중첩량,
        // 배율 등의 역할을 하며 정확한 의미는 해당 executor가 결정한다.
        public int amount;
        public int amount2;
        public int amount3;

        // 시간은 초(float)가 아니라 30Hz 기준 정수 틱으로 기록한다.
        // 이 방식은 Editor와 WebGL에서 같은 순서와 결과를 재현하기 위한 선택이다.
        public int durationTicks;
        public int intervalTicks;

        /// <summary>상태이상 등의 최대 중첩 수다.</summary>
        public int maxStacks;

        /// <summary>범위 반지름. 월드 단위의 1/1000인 milli 정수 단위다.</summary>
        public int radiusMilli;

        /// <summary>횟수, 상한 또는 basis point 상한 등 연산별 제한값이다.</summary>
        public int limit;

        /// <summary>확률을 0~10000 basis point로 표현한다. 10000은 100%다.</summary>
        public int chanceBps;

        /// <summary>추후 다른 정의를 참조해야 하는 효과를 위한 안정 문자열 ID다.</summary>
        public string referenceId;
    }

    /// <summary>
    /// JSON 카드 한 장의 원본 정의다.
    /// 모든 카드는 projectileEffects와 enemyEffects를 모두 비어 있지 않게 제공해야 한다.
    /// 이것이 "같은 카드가 탄환과 적 문맥에서 반드시 이중 해석된다"는 핵심 규칙의
    /// 데이터 단계 계약이다.
    /// </summary>
    [Serializable]
    public sealed class CardDefinitionDto
    {
        /// <summary>
        /// 저장 데이터와 다른 콘텐츠의 참조에 쓰는 안정 ID다.
        /// 배열 위치와 달리 콘텐츠 편집 뒤에도 의미가 유지되므로 임의로 바꾸면 안 된다.
        /// </summary>
        public string id;

        /// <summary>화면 표시 문구를 찾는 로컬라이제이션 키다. 실제 한글 이름 자체가 아니다.</summary>
        public string displayNameKey;

        /// <summary>CardTier의 숫자값이다.</summary>
        public int tier;

        /// <summary>타워의 연산력 한도에서 차감되는 비용이다.</summary>
        public int computeCost;

        /// <summary>타워의 물리적인 카드 슬롯에서 차지하는 칸 수다.</summary>
        public int slotCost = 1;

        /// <summary>드래프트 시너지와 빌드 분류에 쓰는 설계 태그다.</summary>
        public string[] tags;

        /// <summary>SubjectType이 Projectile일 때 왼쪽부터 실행할 효과 목록이다.</summary>
        public EffectNodeDto[] projectileEffects;

        /// <summary>SubjectType이 Enemy일 때 왼쪽부터 실행할 효과 목록이다.</summary>
        public EffectNodeDto[] enemyEffects;
    }

    /// <summary>
    /// 타워 한 레벨의 절대 밸런스 값이다. 이전 레벨과의 차이가 아니라
    /// 해당 레벨에서 실제로 사용할 수치를 기록해 데이터 수정 시 누적 오차를 막는다.
    /// </summary>
    [Serializable]
    public sealed class TowerLevelBalanceDto
    {
        public int upgradeCost;
        public int unlockedSlots;
        public int computeCapacity;
        public int cooldownTicks;
        public int rangeMilli;
        public int selectorRadiusMilli;
        public int targetLimit;
        public int perTargetCooldownTicks;
        public int volleyCount;
    }

    /// <summary>
    /// JSON 타워 정의다. 타워는 "언제, 누구에게, 어떤 카드를 실행하는가"라는
    /// 문장의 앞부분과 기본 전투 수치를 제공한다.
    /// </summary>
    [Serializable]
    public sealed class TowerDefinitionDto
    {
        /// <summary>저장·참조용 안정 문자열 ID다.</summary>
        public string id;

        /// <summary>표시 이름을 찾기 위한 로컬라이제이션 키다.</summary>
        public string displayNameKey;

        /// <summary>TowerTrigger 이름 문자열이다.</summary>
        public string trigger;

        /// <summary>SubjectTypeMode 이름 문자열이며 카드의 탄환/적 해석을 고른다.</summary>
        public string subjectTypeMode;

        /// <summary>SubjectSelector 이름 문자열이며 실제 효과 대상을 고른다.</summary>
        public string selector;

        /// <summary>장착 가능한 카드 슬롯 수다.</summary>
        public int slotCount;

        /// <summary>장착 카드 computeCost 합계의 최대값이다.</summary>
        public int computeCapacity;

        /// <summary>첫 무료 타워 이후 이 타워 정의를 처음 건설할 때의 골드 비용이다.</summary>
        public int constructionCost;

        /// <summary>
        /// 같은 정의의 타워가 이미 하나 있을 때마다 건설비에 더할 basis point다.
        /// 5000이면 두 번째 150%, 세 번째 200%가 된다.
        /// </summary>
        public int duplicateCostStepBps;

        /// <summary>레벨 1~7의 비용·슬롯·전투 수치다.</summary>
        public TowerLevelBalanceDto[] levels;

        /// <summary>타워가 다시 발동하기까지 기다리는 정수 틱 수다.</summary>
        public int cooldownTicks;

        /// <summary>
        /// 공격 준비 연출을 시작한 뒤 실제 탄환을 생성하기까지 기다리는 정수 틱 수다.
        /// 0이면 이전 콘텐츠처럼 준비 이벤트와 탄환 생성이 같은 틱에 일어난다.
        /// </summary>
        public int attackWindupTicks;

        /// <summary>탐지 및 공격 범위의 milli 단위 반지름이다.</summary>
        public int rangeMilli;

        /// <summary>기본 피해의 milli 정수 단위 값이다.</summary>
        public int baseDamageMilli;

        /// <summary>탄환이 한 시뮬레이션 틱 동안 이동하는 milli 거리다.</summary>
        public int projectileSpeedMilliPerTick;

        /// <summary>탄환이 자동 소멸하기까지의 최대 틱 수다.</summary>
        public int projectileLifetimeTicks;

        /// <summary>사망 주변 적처럼 이벤트 기준 대상을 찾을 때 사용하는 milli 반지름이다.</summary>
        public int selectorRadiusMilli;

        /// <summary>한 번의 트리거가 선택할 수 있는 최대 대상 수다.</summary>
        public int targetLimit;

        /// <summary>같은 적을 반복 발동 대상으로 삼기 전의 대기 틱 수다.</summary>
        public int perTargetCooldownTicks;
    }

    /// <summary>
    /// JSON 적 원형 정의다. 실제 웨이브에 등장하는 개체는 이 값을 복사해
    /// 체력, 속도, 보상 가계 원장을 가진 런타임 엔티티로 생성된다.
    /// </summary>
    [Serializable]
    public sealed class EnemyDefinitionDto
    {
        /// <summary>웨이브와 저장 데이터에서 참조하는 안정 문자열 ID다.</summary>
        public string id;

        /// <summary>표시 이름을 찾기 위한 로컬라이제이션 키다.</summary>
        public string displayNameKey;

        /// <summary>EnemyRank 이름 문자열이다.</summary>
        public string rank;

        /// <summary>최대 체력의 milli 정수 단위 값이다.</summary>
        public int maxHealthMilli;

        /// <summary>피해 계산의 방어력 단계에서 사용하는 정수 방어력이다.</summary>
        public int armor;

        /// <summary>한 틱에 경로를 따라 이동하는 milli 거리다.</summary>
        public int speedMilliPerTick;

        /// <summary>
        /// 이 적의 가계 전체가 나누어 갖는 골드 예산이다.
        /// 분열해도 총액이 늘어나지 않도록 개체 단위 보상과 구분한다.
        /// </summary>
        public int rewardBudget;

        /// <summary>분열 가계 전체가 나누어 갖는 웨이브 처치 기여도 예산이다.</summary>
        public int waveProgressBudget;

        /// <summary>경로 끝에 도달했을 때 본진에 주는 정수 피해다.</summary>
        public int leakDamage;

        /// <summary>화염 피해 저항을 basis point로 표현한다.</summary>
        public int fireResistanceBps;

        /// <summary>독 피해 저항을 basis point로 표현한다.</summary>
        public int poisonResistanceBps;

        /// <summary>정예·보스가 행동 방해를 받는 데 필요한 제어 게이지 기준값이다.</summary>
        public int controlGaugeThreshold;

        /// <summary>강한 제어를 받을 때 누적되는 기본 게이지 양이다.</summary>
        public int controlGaugeStep;

        /// <summary>BossAbilityType 이름. 일반/정예 적은 None을 사용한다.</summary>
        public string bossAbility = "None";

        /// <summary>기본/50% 이하 단계의 능력 재사용 대기시간이다.</summary>
        public int bossAbilityIntervalTicks;
        public int bossEnragedAbilityIntervalTicks;

        /// <summary>두 번째 단계가 시작되는 체력 비율이다. 5000은 50%다.</summary>
        public int bossPhaseHealthBps = 5000;

        /// <summary>Shield 능력이 최대 체력 대비 생성하는 보호막 비율이다.</summary>
        public int bossShieldBps;

        /// <summary>Summon 능력이 생성할 적 안정 ID와 단계별 수량/동시 상한이다.</summary>
        public string bossSummonEnemyId;
        public int bossSummonCount;
        public int bossEnragedSummonCount;
        public int bossMaxActiveSummons;
        public int bossSummonHealthBps = 5000;

        /// <summary>Teleport 능력의 시전 시간과 단계별 경로 전진 비율이다.</summary>
        public int bossCastTicks;
        public int bossTeleportDistanceBps;
        public int bossEnragedTeleportDistanceBps;
    }

    /// <summary>웨이브 안에서 특정 적 종류를 언제, 몇 마리 생성할지 적는 한 묶음이다.</summary>
    [Serializable]
    public sealed class WaveSpawnDto
    {
        /// <summary>EnemyDefinitionDto.id를 가리키는 안정 문자열 참조다.</summary>
        public string enemyId;

        /// <summary>이 묶음에서 생성할 총 개체 수다.</summary>
        public int count;

        /// <summary>웨이브 시작 후 첫 개체를 생성할 상대 틱이다.</summary>
        public int firstSpawnTick;

        /// <summary>같은 묶음의 다음 개체를 생성하기까지의 틱 간격이다.</summary>
        public int intervalTicks;
    }

    /// <summary>하나의 웨이브와 그 안의 생성 묶음들을 나타내는 JSON 정의다.</summary>
    [Serializable]
    public sealed class WaveDefinitionDto
    {
        /// <summary>로그와 콘텐츠 식별에 쓰는 안정 문자열 ID다.</summary>
        public string id;

        /// <summary>이 웨이브가 예약하는 적 생성 묶음들이다.</summary>
        public WaveSpawnDto[] spawns;
    }

    /// <summary>
    /// 강력한 조합은 허용하면서 실제 무한 반복과 브라우저 정지를 막는 JSON 안전 예산이다.
    /// 이 값들은 효과의 위력을 낮추는 밸런스 수치가 아니라, 한 체인·한 틱·한 개체가
    /// 소비할 수 있는 작업량의 절대 상한이다.
    /// </summary>
    [Serializable]
    public sealed class SafetyLimitsDto
    {
        /// <summary>파생 이벤트가 부모를 따라 내려갈 수 있는 최대 깊이다.</summary>
        public int maxChainDepth = 8;

        /// <summary>하나의 RootChain이 예약할 수 있는 이벤트 총량이다.</summary>
        public int maxEventsPerChain = 256;

        /// <summary>하나의 체인에서 새로 만들 수 있는 탄환 수다.</summary>
        public int maxProjectileSpawnsPerChain = 64;

        /// <summary>
        /// 이전 콘텐츠와 리플레이의 호환성을 위해 남겨 둔 분열 횟수 힌트다.
        /// 실제 분열은 체력이 1 미만이 되면 자연 종료하고, 가계 개체 수 상한이 비상 보호선으로 작동한다.
        /// </summary>
        public int maxEnemySplitCount = 255;

        /// <summary>분열과 파생을 포함한 하나의 적 가계 최대 구성원 수다.</summary>
        public int maxEnemyLineageMembers = 256;

        /// <summary>탄환 하나가 허용하는 최대 도탄 횟수다.</summary>
        public int maxProjectileBounces = 8;

        /// <summary>탄환 하나가 허용하는 최대 관통 횟수다.</summary>
        public int maxProjectilePierces = 12;

        /// <summary>어떤 효과가 늘리더라도 넘을 수 없는 탄환 수명 틱이다.</summary>
        public int maxProjectileLifetimeTicks = 450;

        /// <summary>한 체인에서 재귀 문법이 다시 발동할 수 있는 횟수다.</summary>
        public int maxRecursiveTriggersPerChain = 1;

        /// <summary>한 시뮬레이션 틱에서 처리할 수 있는 이벤트 총량이다.</summary>
        public int maxEventsPerTick = 4096;

        /// <summary>아직 처리하지 않은 이벤트 큐가 보관할 수 있는 총량이다.</summary>
        public int maxQueuedEvents = 16384;

        /// <summary>한 체인에서 실행할 수 있는 카드 노드 수의 상한이다.</summary>
        public int maxCardTriggersPerChain = 32;

        /// <summary>우로보로스 같은 신화 반복 문법의 체인당 최대 반복 수다.</summary>
        public int maxMythicRepeatsPerChain = 3;

        /// <summary>불길·독안개처럼 동시에 존재할 수 있는 장판 개수다.</summary>
        public int maxActiveHazards = 2048;

        /// <summary>거절된 효과 원인을 보관하는 진단 원형 버퍼의 크기다.</summary>
        public int diagnosticCapacity = 256;
    }

    /// <summary>
    /// 한 런의 공통 규칙, 시작 자원, 배치점, 이동 경로와 드래프트 설정을 담는 JSON 정의다.
    /// 실제 플레이 중 변하는 체력·골드와 달리 이 객체는 콘텐츠 원본이며,
    /// 컴파일 뒤 CompiledRunDefinition으로 고정된다.
    /// </summary>
    [Serializable]
    public sealed class RunDefinitionDto
    {
        /// <summary>결정적 시뮬레이션의 초당 틱 수다. 현재 계약상 정확히 30이어야 한다.</summary>
        public int tickRate = 30;

        /// <summary>새 런의 본진 시작 체력이다.</summary>
        public int baseHealth = 20;

        /// <summary>새 런의 시작 골드다.</summary>
        public int startingGold;

        /// <summary>플레이어가 런 시작 시 하나를 고를 수 있는 타워 안정 ID 목록이다.</summary>
        public string[] startingTowerChoices;

        /// <summary>시작 선택과 별도로 처음부터 건설 가능한 지원 타워 안정 ID 목록이다.</summary>
        public string[] initiallyUnlockedTowers;

        /// <summary>런 시작 시 카드 인벤토리에 지급할 카드 안정 ID 목록이다.</summary>
        public string[] startingCards;

        // JSON 포맷의 단순성을 위해 좌표를 X/Y 평행 배열로 저장한다.
        // 같은 인덱스의 X와 Y가 한 점이며 컴파일 시 SimPosition 하나로 합쳐진다.
        public int[] buildSpotXMilli;
        public int[] buildSpotYMilli;

        /// <summary>
        /// 건설 지점별 해금 골드 비용이다. 좌표 배열과 같은 길이이며 0인 지점은
        /// 런 시작부터 해금되고, 양수인 지점은 계획 단계에서 비용을 지불해야 한다.
        /// </summary>
        public int[] buildSpotUnlockCosts;

        /// <summary>무료 시작 타워 이후 모든 추가 타워에 적용하는 고정 건설 비용이다.</summary>
        public int towerConstructionCost = 100;

        public int[] pathPointXMilli;
        public int[] pathPointYMilli;

        /// <summary>웨이브 뒤 한 번에 제시할 서로 다른 드래프트 카드 수다.</summary>
        public int draftOfferCount = 3;

        /// <summary>일반 드래프트 및 보스 확정 카드팩을 지급하는 1 기반 웨이브 번호다.</summary>
        public int[] regularDraftWaveNumbers;
        public int[] bossCardPackWaveNumbers;

        /// <summary>
        /// 반짝이는 운반 몬스터를 예약하는 누적 처치 진행도 임계값이다.
        /// 일반 적 한 마리는 기본적으로 10,000 진행도를 제공한다.
        /// </summary>
        public int[] cardPackProgressThresholds;
        public int normalKillProgress = 10000;
        public int eliteKillProgress = 30000;

        /// <summary>분열 후 각 가지가 부모 진행도의 몇 bps를 상속하는지 나타낸다.</summary>
        public int splitCardPackProgressBps = 5100;

        /// <summary>반짝이는 운반 몬스터의 체력·속도·크기 배율이다.</summary>
        public int shimmeringHealthBps = 15000;
        public int shimmeringSpeedBps = 12000;
        public int shimmeringSizeBps = 11000;

        /// <summary>일반부터 신화까지 5개 티어의 드래프트 상대 가중치다.</summary>
        public int[] tierWeights;

        /// <summary>치명타 피해 배율이다. 10000은 1배, 15000은 1.5배다.</summary>
        public int criticalDamageBps = 15000;

        /// <summary>정예·보스의 제어 게이지가 찼을 때 행동을 방해하는 틱 수다.</summary>
        public int controlInterruptTicks = 24;

        /// <summary>반복 제어 저항 증가가 도달할 수 있는 게이지 기준값 상한이다.</summary>
        public int maxControlGaugeThreshold = 200;

        /// <summary>적 충돌 판정의 기본 milli 반지름이다.</summary>
        public int enemyBaseHitRadiusMilli = 250;
    }

    /// <summary>
    /// 하나의 밸런스 JSON 파일 전체를 받는 최상위 DTO다.
    /// ContentCompiler.Compile이 이 객체 전체를 한 번에 검증하므로 일부만 성공한
    /// 불완전한 콘텐츠가 전투에 들어가는 것을 방지한다.
    /// </summary>
    [Serializable]
    public sealed class ContentCatalogDto
    {
        /// <summary>콘텐츠/저장 호환성 판단에 사용하는 양의 버전 번호다.</summary>
        public int version;

        /// <summary>이 빌드에서 사용할 모든 카드 원본 정의다.</summary>
        public CardDefinitionDto[] cards;

        /// <summary>이 빌드에서 사용할 모든 타워 원본 정의다.</summary>
        public TowerDefinitionDto[] towers;

        /// <summary>이 빌드에서 사용할 모든 적 원본 정의다.</summary>
        public EnemyDefinitionDto[] enemies;

        /// <summary>런에서 순서대로 진행할 웨이브 원본 정의다.</summary>
        public WaveDefinitionDto[] waves;

        /// <summary>연쇄작용의 기술적 상한 모음이다.</summary>
        public SafetyLimitsDto safety;

        /// <summary>한 런의 고정 규칙과 시작 설정이다.</summary>
        public RunDefinitionDto run;
    }
}
