namespace RuleforgeTD.GameLogic.Effects
{
    /// <summary>
    /// 카드가 탄환이나 적에게 나중에 실행할 효과를 붙일 때의 발동 시점이다.
    /// 효과 실행 계약에 속하며 시뮬레이션 상태 구현과는 독립적이다.
    /// </summary>
    public enum BindingTrigger
    {
        /// <summary>관통을 포함한 모든 유효 적중마다 발동한다.</summary>
        OnHit = 0,
        /// <summary>첫 적중과 소멸 중 먼저 일어난 사건에서 한 번만 발동한다.</summary>
        OnFirstHitOrExpire = 1,
        /// <summary>적 사망이 최종 확정된 뒤 발동한다.</summary>
        OnDeath = 2,
        /// <summary>탄환의 최초 유효 적중에서만 발동한다.</summary>
        OnFirstHit = 3
    }

    /// <summary>
    /// 지연 실행 바인딩이 실제로 수행할 효과 종류다.
    /// </summary>
    public enum BindingKind
    {
        Burn = 0,
        Poison = 1,
        Explosion = 2,
        Knockback = 3,
        Mark = 4,
        Gold = 5,
        Stun = 6,
        Bleed = 7
    }
}
