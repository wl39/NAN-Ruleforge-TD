using RuleforgeTD.GameLogic.Content;

namespace RuleforgeTD.GameLogic.Effects
{
    /// <summary>
    /// 고급 카드 operation은 데이터가 탄환/적 해석을 이미 구분하므로 하나의
    /// 무상태 adapter를 공유하고 실제 규칙은 효과 실행 포트 뒤에 둔다.
    /// </summary>
    internal sealed class UncommonEffectExecutor : IEffectExecutor
    {
        private readonly EffectOperation operation;

        public UncommonEffectExecutor(EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ExecuteUncommonEffect(
                context,
                operation,
                node);
            return EffectExecutionOutcome.Continue();
        }
    }
}
