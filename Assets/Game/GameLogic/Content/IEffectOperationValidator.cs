using System;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Content
{
    /// <summary>
    /// 효과 연산이 실행될 수 있는 카드 주체 문맥이다. 플래그 조합을 사용해
    /// 분열·관통·문법 카드처럼 두 해석을 공유하는 연산도 명시적으로 표현한다.
    /// </summary>
    [Flags]
    public enum EffectSubjectMask
    {
        None = 0,
        Projectile = 1 << 0,
        Enemy = 1 << 1,
        Both = Projectile | Enemy
    }

    /// <summary>
    /// 콘텐츠 컴파일러가 효과 실행 구현을 알지 않고도 연산 등록 여부와
    /// 데이터 매개변수 계약만 확인할 수 있게 하는 읽기 전용 포트다.
    /// </summary>
    public interface IEffectOperationValidator
    {
        bool IsRegistered(EffectOperation operation);

        bool SupportsSubject(
            EffectOperation operation,
            SubjectType subjectType);

        bool IsValid(
            EffectOperation operation,
            EffectNodeDto node);

        bool IsValid(
            EffectOperation operation,
            SubjectType subjectType,
            EffectNodeDto node);
    }
}
