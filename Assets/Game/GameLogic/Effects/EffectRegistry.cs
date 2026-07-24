using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.GameLogic.Effects
{
    // 이 파일은 "데이터에 적힌 EffectOperation 이름"과 "실제로 상태를 바꾸는 C# 코드"를
    // 연결한다. 카드마다 거대한 switch를 두지 않고 작은 executor를 재사용하므로,
    // 구조가 같은 효과는 같은 실행 틀에 설정만 달리해 일관되게 처리할 수 있다.

    /// <summary>
    /// 효과 한 노드를 실행하는 데 필요한 출처와 연쇄 문맥을 모은 읽기 전용 값이다.
    /// 효과가 만든 상태·보상·진단을 어느 타워와 카드에 귀속할지, 그리고 같은 RootChain의
    /// 안전 예산을 공유할지를 결정한다. Unity 오브젝트 참조는 전혀 포함하지 않는다.
    /// </summary>
    public readonly struct EffectExecutionContext
    {
        /// <summary>시뮬레이션이 현재 카드와 이벤트 정보로 실행 문맥을 만든다.</summary>
        public EffectExecutionContext(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            CardId cardId,
            int cardInstanceId,
            EntityId sourceEntityId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            int continuationCardCount,
            int reservedContinuationEvents)
        {
            SubjectType = subjectType;
            SubjectId = subjectId;
            TowerId = towerId;
            CardId = cardId;
            CardInstanceId = cardInstanceId;
            SourceEntityId = sourceEntityId;
            RootChainId = rootChainId;
            ActivationId = activationId;
            ParentEventId = parentEventId;
            Depth = depth;
            ContinuationCardCount = continuationCardCount;
            ReservedContinuationEvents = reservedContinuationEvents;
        }

        /// <summary>같은 카드를 탄환 해석과 적 해석 중 어느 쪽으로 실행 중인지 나타낸다.</summary>
        public SubjectType SubjectType { get; }

        /// <summary>효과를 직접 적용할 현재 탄환 또는 적 EntityId다.</summary>
        public EntityId SubjectId { get; }

        /// <summary>이 카드 프로그램을 실행한 타워 인스턴스 ID다.</summary>
        public TowerId TowerId { get; }

        /// <summary>현재 실행 중인 카드 정의 ID다.</summary>
        public CardId CardId { get; }

        /// <summary>
        /// 같은 종류의 카드를 여러 장 가졌을 때도 개별 장착 카드를 구분하는 ID다.
        /// 보상 증가 같은 "같은 카드 1장당 한 번" 규칙의 중복 방지에 사용한다.
        /// </summary>
        public int CardInstanceId { get; }

        /// <summary>현재 효과를 발생시킨 원본 게임 엔티티 ID다.</summary>
        public EntityId SourceEntityId { get; }

        /// <summary>
        /// 최초 발동부터 모든 파생 이벤트가 공유하는 연쇄 ID다.
        /// 체인 깊이·이벤트·생성 한도를 우회하지 못하게 묶는 기준이다.
        /// </summary>
        public ChainId RootChainId { get; }

        /// <summary>한 번의 타워 발동을 식별하여 안정적인 이벤트 추적과 정렬에 사용한다.</summary>
        public ActivationId ActivationId { get; }

        /// <summary>현재 효과를 예약한 부모 이벤트 ID다.</summary>
        public EventId ParentEventId { get; }

        /// <summary>RootChain 안에서 현재 이벤트가 파생된 깊이다.</summary>
        public int Depth { get; }

        /// <summary>현재 카드 오른쪽에 아직 실행해야 할 카드 수다.</summary>
        public int ContinuationCardCount { get; }

        /// <summary>
        /// 현재 가지가 다음 카드들을 위해 이미 원자적으로 확보해 둔 이벤트 수다.
        /// 분열은 이 값을 고려해 원본과 새 가지 모두의 남은 카드를 한꺼번에 예약한다.
        /// </summary>
        public int ReservedContinuationEvents { get; }
    }

    /// <summary>
    /// executor 실행 뒤 카드 프로그램 진행 방법을 시뮬레이션에 돌려주는 값이다.
    /// 일반 효과는 같은 대상을 계속 사용하고, 분열처럼 대상이 늘어난 효과는
    /// 원본과 추가 대상 각각이 오른쪽 카드로 이어질 예약 정보를 함께 반환한다.
    /// </summary>
    public readonly struct EffectExecutionOutcome
    {
        /// <summary>일반 진행 또는 분기 진행 결과를 완전한 값으로 만든다.</summary>
        public EffectExecutionOutcome(
            bool subjectReplaced,
            EntityId additionalSubject,
            int originalContinuationReservations,
            int additionalContinuationReservations)
        {
            SubjectReplaced = subjectReplaced;
            AdditionalSubject = additionalSubject;
            OriginalContinuationReservations =
                originalContinuationReservations;
            AdditionalContinuationReservations =
                additionalContinuationReservations;
        }

        /// <summary>
        /// 대상 구성이 바뀌어 executor가 이후 continuation 분기를 직접 설명하는지 나타낸다.
        /// true이면 같은 카드 안의 뒤 노드를 중복 실행하지 않고 분기 처리로 넘어간다.
        /// </summary>
        public bool SubjectReplaced { get; }

        /// <summary>분열로 생긴 두 번째 탄환 또는 적 ID다. 일반 결과에서는 Invalid다.</summary>
        public EntityId AdditionalSubject { get; }

        /// <summary>원본 가지가 오른쪽 카드 실행에 사용할 사전 예약 수다.</summary>
        public int OriginalContinuationReservations { get; }

        /// <summary>추가 가지가 오른쪽 카드 실행에 사용할 사전 예약 수다.</summary>
        public int AdditionalContinuationReservations { get; }

        /// <summary>현재 대상 하나로 일반적인 다음 카드 진행을 계속한다는 결과를 만든다.</summary>
        public static EffectExecutionOutcome Continue()
        {
            return new EffectExecutionOutcome(
                false,
                EntityId.Invalid,
                0,
                0);
        }

        /// <summary>
        /// 원본과 새 대상이 모두 남은 카드를 실행하도록 분기 결과를 만든다.
        /// 예약 수는 SplitProjectile/SplitEnemy가 전체 예산을 먼저 확보한 뒤 전달한다.
        /// </summary>
        public static EffectExecutionOutcome Split(
            EntityId additionalSubject,
            int originalContinuationReservations,
            int additionalContinuationReservations)
        {
            return new EffectExecutionOutcome(
                true,
                additionalSubject,
                originalContinuationReservations,
                additionalContinuationReservations);
        }
    }

    /// <summary>
    /// CompiledEffectNode 하나를 논리 상태에 적용하는 실행 코드의 공통 계약이다.
    /// 구현체는 화면이나 GameObject를 직접 만지지 않고 GameSimulation이 제공하는
    /// 결정적 상태 변경 메서드만 호출한다.
    /// </summary>
    public interface IEffectExecutor
    {
        /// <summary>
        /// 주어진 문맥과 노드를 실행하고 카드 프로그램의 다음 진행 방식을 반환한다.
        /// </summary>
        /// <param name="simulation">상태 변경과 이벤트 예약을 담당하는 현재 시뮬레이션이다.</param>
        /// <param name="context">대상, 출처, 체인 및 continuation 정보다.</param>
        /// <param name="node">JSON에서 검증·컴파일된 효과 수치다.</param>
        EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node);
    }

    /// <summary>
    /// EffectOperation과 IEffectExecutor의 일대일 대응표다.
    /// enum의 정수값을 배열 인덱스로 사용하여 카드 실행 때 문자열 검색이나
    /// Dictionary 열거 순서에 의존하지 않는 빠르고 결정적인 조회를 제공한다.
    /// </summary>
    public sealed class EffectRegistry
    {
        private readonly IEffectExecutor[] executors;

        /// <summary>
        /// 현재 EffectOperation enum 전체 크기만큼 비어 있는 실행 표를 만든다.
        /// 사용 전에 Register로 필요한 모든 연산을 채워야 한다.
        /// </summary>
        public EffectRegistry()
        {
            int count = Enum.GetValues(typeof(EffectOperation)).Length;
            executors = new IEffectExecutor[count];
        }

        /// <summary>
        /// Phase 1에서 지원하는 모든 연산을 실행 코드와 연결한 기본 registry를 만든다.
        /// ContentCompiler에 이 registry의 IsRegistered를 전달하면 데이터와 코드 구현의
        /// 누락을 콘텐츠 로딩 시점에 함께 검증할 수 있다.
        /// </summary>
        public static EffectRegistry CreateDefault()
        {
            var registry = new EffectRegistry();

            // 대상 수와 카드 프로그램 흐름을 바꾸는 분열은 전용 executor를 쓴다.
            registry.Register(EffectOperation.Split, new SplitEffectExecutor());

            // 관통은 탄환 문맥에서는 탄환 수치를 바꾸고,
            // 적 문맥에서는 Pierced 상태를 부여하므로 내부에서 문맥을 나눈다.
            registry.Register(EffectOperation.AddPierce, new PierceEffectExecutor());

            // Bind 계열은 지금 즉시 적에게 적용하지 않고 탄환에 "적중 시 실행할 규칙"을
            // 부착한다. 분열 전후 카드 순서에 따라 이 바인딩 상속 결과가 달라진다.
            registry.Register(
                EffectOperation.BindBurn,
                new BindingEffectExecutor(BindingKind.Burn, BindingTrigger.OnHit));
            registry.Register(EffectOperation.ApplyBurn, new ApplyStatusEffectExecutor(StatusType.Burn));
            registry.Register(
                EffectOperation.ModifyProjectileSlow,
                new ProjectileModifierEffectExecutor(EffectOperation.ModifyProjectileSlow));
            registry.Register(EffectOperation.ApplySlow, new ApplyStatusEffectExecutor(StatusType.Slow));

            // 폭발은 탄환 문맥이면 첫 적중/소멸, 적 문맥이면 사망 사건에 연결되므로
            // 일반 BindingEffectExecutor보다 더 많은 문맥 정보가 필요하다.
            registry.Register(
                EffectOperation.BindExplosion,
                new ContextualExplosionEffectExecutor());
            registry.Register(
                EffectOperation.BindKnockback,
                new BindingEffectExecutor(BindingKind.Knockback, BindingTrigger.OnHit));
            registry.Register(
                EffectOperation.ApplyKnockback,
                new DirectEnemyEffectExecutor(EffectOperation.ApplyKnockback));
            // 표식·골드·기절의 OnFirstHit은 같은 탄환이 같은 종류의 효과를
            // 의도치 않게 반복 적용하지 않도록 첫 적중 사건에만 반응한다.
            registry.Register(
                EffectOperation.BindMark,
                new BindingEffectExecutor(
                    BindingKind.Mark,
                    BindingTrigger.OnFirstHit));
            registry.Register(EffectOperation.ApplyMark, new ApplyStatusEffectExecutor(StatusType.Mark));
            registry.Register(
                EffectOperation.BindGoldOnHit,
                new BindingEffectExecutor(BindingKind.Gold, BindingTrigger.OnHit));
            registry.Register(
                EffectOperation.IncreaseReward,
                new DirectEnemyEffectExecutor(EffectOperation.IncreaseReward));
            registry.Register(
                EffectOperation.BindPoison,
                new BindingEffectExecutor(BindingKind.Poison, BindingTrigger.OnHit));
            registry.Register(
                EffectOperation.ApplyPoison,
                new ApplyStatusEffectExecutor(StatusType.Poison));
            registry.Register(
                EffectOperation.EnlargeProjectile,
                new ProjectileModifierEffectExecutor(EffectOperation.EnlargeProjectile));
            registry.Register(
                EffectOperation.EnlargeEnemy,
                new DirectEnemyEffectExecutor(EffectOperation.EnlargeEnemy));
            registry.Register(
                EffectOperation.ShrinkProjectile,
                new ProjectileModifierEffectExecutor(EffectOperation.ShrinkProjectile));
            registry.Register(
                EffectOperation.ShrinkEnemy,
                new DirectEnemyEffectExecutor(EffectOperation.ShrinkEnemy));
            registry.Register(
                EffectOperation.BindStun,
                new BindingEffectExecutor(
                    BindingKind.Stun,
                    BindingTrigger.OnFirstHit));
            registry.Register(EffectOperation.ApplyStun, new ApplyStatusEffectExecutor(StatusType.Stun));
            return registry;
        }

        /// <summary>
        /// 연산 하나에 executor를 등록하거나 기존 등록을 명시적으로 교체한다.
        /// null executor는 실행 시점까지 오류를 숨기므로 즉시 거절한다.
        /// </summary>
        /// <param name="operation">JSON 효과 노드가 선택할 연산 종류다.</param>
        /// <param name="executor">해당 연산을 실제 상태 변경으로 번역할 구현체다.</param>
        public void Register(EffectOperation operation, IEffectExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }

            // enum 값이 곧 배열 위치다. 같은 operation을 다시 등록하면 마지막 구현이
            // 사용되므로 테스트 전용 규칙이나 향후 구현 교체에도 사용할 수 있다.
            executors[(int)operation] = executor;
        }

        /// <summary>현재 enum 값에 실행 코드가 연결되어 있는지 확인한다.</summary>
        public bool IsRegistered(EffectOperation operation)
        {
            int index = (int)operation;
            return index >= 0 && index < executors.Length && executors[index] != null;
        }

        /// <summary>
        /// 카드 실행에 사용할 executor를 가져온다.
        /// 미등록 연산을 조용히 무시하지 않고 즉시 예외로 알려 콘텐츠/코드 불일치를 드러낸다.
        /// </summary>
        public IEffectExecutor Get(EffectOperation operation)
        {
            if (!IsRegistered(operation))
            {
                throw new InvalidOperationException(
                    "No effect executor registered for " + operation + ".");
            }

            return executors[(int)operation];
        }
    }

    // 분열은 단순 수치 변경이 아니라 대상 수와 이후 카드 흐름을 둘로 나누므로
    // 전용 결과(EffectExecutionOutcome.Split)를 반환하는 제어 흐름 executor다.
    internal sealed class SplitEffectExecutor : IEffectExecutor
    {
        /// <summary>현재 문맥에 따라 탄환 또는 적 분열을 요청한다.</summary>
        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            // 실제 분열 메서드는 개체 생성 전에 원본·자식 양쪽 continuation 예산을
            // 원자적으로 확보한다. 실패하면 Invalid를 돌려 원본 하나로 계속 진행한다.
            EntityId child = context.SubjectType == SubjectType.Projectile
                ? simulation.SplitProjectile(context, node)
                : simulation.SplitEnemy(context, node);
            return child.IsValid
                ? EffectExecutionOutcome.Split(
                    child,
                    context.ContinuationCardCount,
                    context.ContinuationCardCount)
                : EffectExecutionOutcome.Continue();
        }
    }

    // "관통"이라는 카드 한 장의 이중 해석:
    // 탄환에는 남은 관통 횟수를 더하고, 적에게는 Pierced 상태를 부여한다.
    internal sealed class PierceEffectExecutor : IEffectExecutor
    {
        /// <summary>SubjectType에 맞는 관통 해석을 실행한다.</summary>
        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.SubjectType == SubjectType.Projectile)
            {
                simulation.AddProjectilePierce(context.SubjectId, node);
            }
            else
            {
                simulation.ApplyStatus(context, StatusType.Pierced, node);
            }

            return EffectExecutionOutcome.Continue();
        }
    }

    // 탄환 카드의 "적중하면 화상/중독/밀치기" 같은 지연 행동을 부착한다.
    // kind는 무엇을 할지, trigger는 탄환 수명주기의 언제 할지를 나타낸다.
    internal sealed class BindingEffectExecutor : IEffectExecutor
    {
        private readonly BindingKind kind;
        private readonly BindingTrigger trigger;

        // registry 구성 시 효과 종류별로 한 번 설정되며 실행 중에는 바뀌지 않는다.
        public BindingEffectExecutor(BindingKind kind, BindingTrigger trigger)
        {
            this.kind = kind;
            this.trigger = trigger;
        }

        /// <summary>현재 탄환에 출처와 노드 수치를 보존한 지연 바인딩을 추가한다.</summary>
        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            // 지금 즉시 적을 찾거나 피해를 주지 않는다. 실제 충돌 사건이 발생할 때
            // GameSimulation이 이 바인딩을 EventQueue 작업으로 바꾼다.
            simulation.AddProjectileBinding(context, trigger, kind, node);
            return EffectExecutionOutcome.Continue();
        }
    }

    // 폭발 카드의 두 해석은 발동 시점 자체가 다르다.
    // 탄환: 첫 적중 또는 수명 종료, 적: 그 적의 사망.
    internal sealed class ContextualExplosionEffectExecutor : IEffectExecutor
    {
        /// <summary>현재 대상 종류의 수명주기에 폭발 바인딩을 연결한다.</summary>
        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.SubjectType == SubjectType.Projectile)
            {
                simulation.AddProjectileBinding(
                    context,
                    BindingTrigger.OnFirstHitOrExpire,
                    BindingKind.Explosion,
                    node);
            }
            else
            {
                simulation.AddEnemyDeathBinding(context, BindingKind.Explosion, node);
            }

            return EffectExecutionOutcome.Continue();
        }
    }

    // 화상·중독·둔화·표식·기절처럼 상태 적용 절차가 같은 효과를
    // StatusType만 바꾸어 재사용하는 작은 adapter다.
    internal sealed class ApplyStatusEffectExecutor : IEffectExecutor
    {
        private readonly StatusType statusType;

        public ApplyStatusEffectExecutor(StatusType statusType)
        {
            this.statusType = statusType;
        }

        /// <summary>중앙 StatusSystem을 통해 출처가 보존된 상태이상을 적용한다.</summary>
        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ApplyStatus(context, statusType, node);
            return EffectExecutionOutcome.Continue();
        }
    }

    // 거대화·축소·탄환 둔화처럼 탄환의 물리 수치를 즉시 바꾸는 연산용 adapter다.
    internal sealed class ProjectileModifierEffectExecutor : IEffectExecutor
    {
        private readonly EffectOperation operation;

        public ProjectileModifierEffectExecutor(EffectOperation operation)
        {
            this.operation = operation;
        }

        /// <summary>현재 탄환에 operation별 결정적 수치 변경을 적용한다.</summary>
        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ModifyProjectile(context.SubjectId, operation, node);
            return EffectExecutionOutcome.Continue();
        }
    }

    // 밀치기·보상 증가·거대화·축소처럼 적 상태이상 인스턴스가 아닌
    // 적 자체의 즉시 변화를 GameSimulation의 공통 진입점으로 전달한다.
    internal sealed class DirectEnemyEffectExecutor : IEffectExecutor
    {
        private readonly EffectOperation operation;

        public DirectEnemyEffectExecutor(EffectOperation operation)
        {
            this.operation = operation;
        }

        /// <summary>현재 적에게 operation별 직접 효과를 적용한다.</summary>
        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ApplyDirectEnemyEffect(context, operation, node);
            return EffectExecutionOutcome.Continue();
        }
    }
}
