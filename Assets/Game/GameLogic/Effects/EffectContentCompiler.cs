using System;
using RuleforgeTD.GameLogic.Content;

namespace RuleforgeTD.GameLogic.Effects
{
    /// <summary>
    /// 순수 콘텐츠 컴파일러와 기본 효과 descriptor 레지스트리를 조립하는
    /// 애플리케이션 경계다. Content 계층은 실행기나 Simulation 계층을 참조하지 않는다.
    /// </summary>
    public static class EffectContentCompiler
    {
        public static CompiledContent Compile(
            ContentCatalogDto source,
            Func<EffectOperation, bool> isOperationRegistered = null)
        {
            return ContentCompiler.Compile(
                source,
                EffectRegistry.Default,
                isOperationRegistered);
        }
    }
}
