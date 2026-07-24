namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// 게임 로직에서 공통으로 참조할 수 있는 제품 메타데이터의 기본값 모음이다.
    /// </summary>
    /// <remarks>
    /// 실제 화면에 표시할 제목은 로컬라이제이션/텍스트 데이터가 우선이다.
    /// 이 값은 표시 데이터가 없을 때 사용할 안전한 기본값이며, 게임 규칙에는 영향을 주지 않는다.
    /// </remarks>
    public static class GameMetadata
    {
        /// <summary>
        /// 별도의 표시 문자열이 제공되지 않았을 때 사용하는 기본 게임 제목이다.
        /// </summary>
        public const string DefaultGameTitle = "Ruleforge TD";
    }
}
