using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 기존 씬·테스트 직렬화 호환을 위한 이름 어댑터다.
    /// 신규 공용 UI는 RuntimeSafeAreaFitter를 직접 사용한다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class StageOneSafeAreaFitter :
        RuntimeSafeAreaFitter
    {
    }
}
