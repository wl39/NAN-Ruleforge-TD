using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Effects
{
    // 이 파일은 "데이터에 적힌 EffectOperation 이름"과 "실제로 상태를 바꾸는 C# 코드"를
    // 연결한다. 카드마다 거대한 switch를 두지 않고 작은 executor를 재사용하므로,
    // 구조가 같은 효과는 같은 실행 틀에 설정만 달리해 일관되게 처리할 수 있다.

    /// <summary>
    /// A queued card-program pass can carry grammar metadata without coupling
    /// individual executors to the simulation's ProgramFrame implementation.
    /// Flags are inherited by every continuation in the same pass.
    /// </summary>
    [Flags]
    public enum EffectExecutionFlags
    {
        None = 0,
        Repeated = 1 << 0,
        SingleCard = 1 << 1,
        SuppressRecursion = 1 << 2,
        SuppressOverload = 1 << 3,
        SuppressOuroboros = 1 << 4,
        LastCommand = 1 << 5,
        DualInterpretation = 1 << 6
    }

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
            int reservedContinuationEvents,
            int cardIndex = -1,
            int traversalDirection = 1,
            int powerBps = 10000,
            int repeatIndex = 0,
            EffectExecutionFlags executionFlags =
                EffectExecutionFlags.None)
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
            CardIndex = cardIndex;
            TraversalDirection = traversalDirection < 0 ? -1 : 1;
            PowerBps = powerBps <= 0 ? 10000 : powerBps;
            RepeatIndex = Math.Max(0, repeatIndex);
            ExecutionFlags = executionFlags;
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

        /// <summary>현재 패스 방향에서 이 카드 뒤에 아직 실행해야 할 카드 수다.</summary>
        public int ContinuationCardCount { get; }

        /// <summary>
        /// 현재 가지가 다음 카드들을 위해 이미 원자적으로 확보해 둔 이벤트 수다.
        /// 분열은 이 값을 고려해 원본과 새 가지 모두의 남은 카드를 한꺼번에 예약한다.
        /// </summary>
        public int ReservedContinuationEvents { get; }

        /// <summary>현재 실행 중인 카드의 타워 프로그램 인덱스다.</summary>
        public int CardIndex { get; }

        /// <summary>이 프로그램 패스가 왼쪽(+1) 또는 오른쪽(-1)으로 진행하는지 나타낸다.</summary>
        public int TraversalDirection { get; }

        /// <summary>연쇄·반복 패스가 효과 수치에 적용하는 basis-point 위력이다.</summary>
        public int PowerBps { get; }

        /// <summary>같은 규칙이 만든 0 기반 반복 패스 번호다.</summary>
        public int RepeatIndex { get; }

        /// <summary>현재 패스의 재진입 억제와 단일 카드 실행 같은 문법 표식이다.</summary>
        public EffectExecutionFlags ExecutionFlags { get; }

        public bool HasExecutionFlag(EffectExecutionFlags flag)
        {
            return (ExecutionFlags & flag) != 0;
        }
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
            int additionalContinuationReservations,
            EntityId secondAdditionalSubject,
            int secondAdditionalContinuationReservations)
        {
            SubjectReplaced = subjectReplaced;
            AdditionalSubject = additionalSubject;
            OriginalContinuationReservations =
                originalContinuationReservations;
            AdditionalContinuationReservations =
                additionalContinuationReservations;
            SecondAdditionalSubject = secondAdditionalSubject;
            SecondAdditionalContinuationReservations =
                secondAdditionalContinuationReservations;
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

        /// <summary>
        /// 시간 균열처럼 한 번에 두 개의 추가 가지를 만든 효과의 세 번째 대상이다.
        /// 일반 분열과 복제에서는 Invalid다.
        /// </summary>
        public EntityId SecondAdditionalSubject { get; }

        /// <summary>두 번째 추가 가지가 continuation에 사용할 사전 예약 수다.</summary>
        public int SecondAdditionalContinuationReservations { get; }

        /// <summary>현재 대상 하나로 일반적인 다음 카드 진행을 계속한다는 결과를 만든다.</summary>
        public static EffectExecutionOutcome Continue()
        {
            return new EffectExecutionOutcome(
                false,
                EntityId.Invalid,
                0,
                0,
                EntityId.Invalid,
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
                additionalContinuationReservations,
                EntityId.Invalid,
                0);
        }

        /// <summary>
        /// 원본과 두 추가 대상이 모두 오른쪽(또는 역순의 다음) 카드로 이어지는
        /// 세 갈래 결과를 만든다.
        /// </summary>
        public static EffectExecutionOutcome BranchThree(
            EntityId firstAdditionalSubject,
            EntityId secondAdditionalSubject,
            int continuationReservations)
        {
            return new EffectExecutionOutcome(
                true,
                firstAdditionalSubject,
                continuationReservations,
                continuationReservations,
                secondAdditionalSubject,
                continuationReservations);
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
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node);
    }

    /// <summary>
    /// 효과 executor가 권위 시뮬레이션에 요청할 수 있는 최소 상태 변경 포트다.
    /// executor는 GameSimulation의 전체 런·웨이브·UI 조회 표면을 알지 않는다.
    /// </summary>
    public interface IEffectExecutionHost
    {
        EntityId SplitProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        EntityId SplitEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void AddProjectilePierce(
            EntityId projectileId,
            in CompiledEffectNode node);

        void AddProjectileBinding(
            in EffectExecutionContext context,
            BindingTrigger trigger,
            BindingKind kind,
            in CompiledEffectNode node);

        void AddEnemyDeathBinding(
            in EffectExecutionContext context,
            BindingKind kind,
            in CompiledEffectNode node);

        void ApplyStatus(
            in EffectExecutionContext context,
            StatusType statusType,
            in CompiledEffectNode node);

        void ModifyProjectile(
            EntityId projectileId,
            EffectOperation operation,
            in CompiledEffectNode node);

        void ApplyDirectEnemyEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node);

        void ConfigureProjectileRicochet(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ApplyEnemyRicochet(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ApplyBleed(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void AccelerateProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void AccelerateEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void EnableProjectileHoming(
            in EffectExecutionContext context);

        void ApplyHomingPriority(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void DelayProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ApplyDelay(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ExecuteUncommonEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node);

        EntityId DuplicateRareProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        EntityId DuplicateRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void SacrificeRareProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void SacrificeRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ConfigureRareProjectileReturn(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void RewindRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ConfigureRareProjectileRetrograde(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ApplyRareEnemyRetrograde(
            in EffectExecutionContext context,
            in CompiledEffectNode node);

        void ExecuteRareResonanceAbsorbTimeMutation(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node);

        void ExecuteRareDeathChainEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node);

        EffectExecutionOutcome ExecuteLegendaryEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node);

        EffectExecutionOutcome ExecuteMythicEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node);
    }

    /// <summary>
    /// JSON 효과 노드가 연산 고유의 매개변수 계약을 만족하는지 검사한다.
    /// 공통 정수 안전 범위는 ContentCompiler가 먼저 검사하고, 이 검사는
    /// 배율·필수 필드·연산별 상한처럼 executor가 실제로 요구하는 의미를 맡는다.
    /// </summary>
    public delegate bool EffectNodeValidator(EffectNodeDto node);

    /// <summary>
    /// 효과 연산의 실행 코드와 입력 계약을 하나의 등록 단위로 묶는다.
    /// 새 연산을 추가할 때 executor만 등록하고 ContentCompiler의 별도 switch를
    /// 빠뜨리는 식의 불완전한 확장을 방지한다.
    /// </summary>
    public sealed class EffectOperationDescriptor
    {
        public EffectOperationDescriptor(
            EffectOperation operation,
            IEffectExecutor executor,
            EffectNodeValidator validator)
            : this(
                operation,
                executor,
                validator,
                EffectRegistry.LegacyModuleId,
                EffectSubjectMask.Both)
        {
        }

        public EffectOperationDescriptor(
            EffectOperation operation,
            IEffectExecutor executor,
            EffectNodeValidator validator,
            string moduleId,
            EffectSubjectMask supportedSubjects)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                throw new ArgumentException(
                    "An effect module id is required.",
                    nameof(moduleId));
            }
            if (supportedSubjects == EffectSubjectMask.None ||
                (supportedSubjects & ~EffectSubjectMask.Both) !=
                    EffectSubjectMask.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(supportedSubjects));
            }

            Operation = operation;
            Executor = executor ??
                throw new ArgumentNullException(nameof(executor));
            Validator = validator ??
                throw new ArgumentNullException(nameof(validator));
            ModuleId = moduleId.Trim();
            SupportedSubjects = supportedSubjects;
        }

        public EffectOperation Operation { get; }
        public IEffectExecutor Executor { get; }
        public EffectNodeValidator Validator { get; }
        public string ModuleId { get; }
        public EffectSubjectMask SupportedSubjects { get; }

        public bool SupportsSubject(SubjectType subjectType)
        {
            EffectSubjectMask requested;
            switch (subjectType)
            {
                case SubjectType.Projectile:
                    requested = EffectSubjectMask.Projectile;
                    break;
                case SubjectType.Enemy:
                    requested = EffectSubjectMask.Enemy;
                    break;
                default:
                    return false;
            }

            return (SupportedSubjects & requested) !=
                   EffectSubjectMask.None;
        }

        public bool IsValid(EffectNodeDto node)
        {
            return node != null && Validator(node);
        }

        public bool IsValid(
            SubjectType subjectType,
            EffectNodeDto node)
        {
            return SupportsSubject(subjectType) &&
                   IsValid(node);
        }
    }

    /// <summary>
    /// EffectOperation과 실행기·입력 계약의 일대일 대응표다.
    /// enum의 정수값을 배열 인덱스로 사용하여 카드 실행 때 문자열 검색이나
    /// Dictionary 열거 순서에 의존하지 않는 빠르고 결정적인 조회를 제공한다.
    /// </summary>
    public sealed class EffectRegistry : IEffectOperationValidator
    {
        public const string CoreModuleId = "Core";
        public const string CommonModuleId = "Common";
        public const string UncommonModuleId = "Uncommon";
        public const string RareModuleId = "Rare";
        public const string LegendaryModuleId = "Legendary";
        public const string MythicModuleId = "Mythic";
        public const string LegacyModuleId = "Legacy";

        private readonly struct ModuleRegistration
        {
            public ModuleRegistration(
                string moduleId,
                Action<EffectRegistry> register)
            {
                ModuleId = moduleId;
                Register = register;
            }

            public string ModuleId { get; }
            public Action<EffectRegistry> Register { get; }
        }

        private static readonly ModuleRegistration[] DefaultModules =
        {
            new ModuleRegistration(
                CoreModuleId,
                RegisterCoreOperations),
            new ModuleRegistration(
                CommonModuleId,
                RegisterCommonOperations),
            new ModuleRegistration(
                UncommonModuleId,
                RegisterUncommonOperations),
            new ModuleRegistration(
                RareModuleId,
                RegisterRareOperations),
            new ModuleRegistration(
                LegendaryModuleId,
                RegisterLegendaryOperations),
            new ModuleRegistration(
                MythicModuleId,
                RegisterMythicOperations)
        };

        private static readonly Lazy<EffectRegistry> DefaultRegistry =
            new Lazy<EffectRegistry>(ComposeDefault);

        private readonly EffectOperationDescriptor[] descriptors;
        private string activeModuleId;
        private int registeredOperationCount;

        public static EffectRegistry Default =>
            DefaultRegistry.Value;

        public bool IsFrozen { get; private set; }

        public int RegisteredOperationCount =>
            registeredOperationCount;

        public static string[] DefaultModuleOrder
        {
            get
            {
                var result = new string[DefaultModules.Length];
                for (int i = 0; i < DefaultModules.Length; i++)
                {
                    result[i] = DefaultModules[i].ModuleId;
                }

                return result;
            }
        }

        /// <summary>
        /// 현재 EffectOperation enum 전체 크기만큼 비어 있는 실행 표를 만든다.
        /// 사용 전에 Register로 필요한 모든 연산을 채워야 한다.
        /// </summary>
        public EffectRegistry()
        {
            Array values = Enum.GetValues(typeof(EffectOperation));
            int maximumValue = 0;
            for (int i = 0; i < values.Length; i++)
            {
                maximumValue = Math.Max(
                    maximumValue,
                    (int)(EffectOperation)values.GetValue(i));
            }
            // enum 멤버 수가 아니라 가장 큰 안정 ID를 기준으로 잡아, 향후 호환성
            // 때문에 번호를 비워 두더라도 유효한 뒤쪽 연산 등록이 범위를 벗어나지 않는다.
            descriptors =
                new EffectOperationDescriptor[
                    checked(maximumValue + 1)];
        }

        /// <summary>
        /// 지원하는 모든 연산을 실행 코드와 의미 validator에 연결한 기본 registry를 만든다.
        /// ContentCompiler도 같은 descriptor를 사용하므로 등록 누락과 입력 계약 위반을
        /// 콘텐츠 로딩 시점에 함께 검증한다.
        /// </summary>
        public static EffectRegistry CreateDefault()
        {
            return Default;
        }

        private static EffectRegistry ComposeDefault()
        {
            var registry = new EffectRegistry();
            for (int i = 0; i < DefaultModules.Length; i++)
            {
                ModuleRegistration module = DefaultModules[i];
                registry.ComposeModule(
                    module.ModuleId,
                    module.Register);
            }

            registry.Freeze();
            return registry;
        }

        private void ComposeModule(
            string moduleId,
            Action<EffectRegistry> register)
        {
            if (IsFrozen)
            {
                throw new InvalidOperationException(
                    "A frozen effect registry cannot compose modules.");
            }
            if (!string.IsNullOrEmpty(activeModuleId))
            {
                throw new InvalidOperationException(
                    "Effect modules cannot be composed recursively.");
            }
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                throw new ArgumentException(
                    "An effect module id is required.",
                    nameof(moduleId));
            }
            if (register == null)
            {
                throw new ArgumentNullException(nameof(register));
            }

            activeModuleId = moduleId.Trim();
            try
            {
                register(this);
            }
            finally
            {
                activeModuleId = null;
            }
        }

        private static void RegisterCoreOperations(
            EffectRegistry registry)
        {
            registry.Register(
                EffectOperation.Split,
                new SplitEffectExecutor(),
                node => node.amount == 2 &&
                        IsBoundedMultiplier(node.amount2));
            registry.Register(
                EffectOperation.AddPierce,
                new PierceEffectExecutor(),
                node => node.amount <= 10_000 &&
                        node.amount2 <= 10_000);
            registry.Register(
                EffectOperation.BindBurn,
                new BindingEffectExecutor(
                    BindingKind.Burn,
                    BindingTrigger.OnHit),
                node => node.amount > 0 &&
                        node.amount2 > 0 &&
                        node.amount2 <= 100_000 &&
                        node.amount3 > 0 &&
                        node.amount3 <= 36_000 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.ApplyBurn,
                new ApplyStatusEffectExecutor(StatusType.Burn),
                HasRequiredBurnStatusFields);
            registry.Register(
                EffectOperation.ModifyProjectileSlow,
                new ProjectileModifierEffectExecutor(
                    EffectOperation.ModifyProjectileSlow),
                HasThreeBoundedMultipliers);
            registry.Register(
                EffectOperation.ApplySlow,
                new ApplyStatusEffectExecutor(StatusType.Slow),
                node => node.amount <= 10_000 &&
                        node.limit <= 10_000);
            registry.Register(
                EffectOperation.BindExplosion,
                new ContextualExplosionEffectExecutor(),
                node => node.amount <= 100_000);
            registry.Register(
                EffectOperation.BindKnockback,
                new BindingEffectExecutor(
                    BindingKind.Knockback,
                    BindingTrigger.OnHit),
                HasBoundedKnockback);
            registry.Register(
                EffectOperation.ApplyKnockback,
                new DirectEnemyEffectExecutor(
                    EffectOperation.ApplyKnockback),
                HasBoundedKnockback);
            registry.Register(
                EffectOperation.BindMark,
                new BindingEffectExecutor(
                    BindingKind.Mark,
                    BindingTrigger.OnFirstHit),
                HasBoundedMark);
            registry.Register(
                EffectOperation.ApplyMark,
                new ApplyStatusEffectExecutor(StatusType.Mark),
                HasBoundedMark);
            registry.Register(
                EffectOperation.BindGoldOnHit,
                new BindingEffectExecutor(
                    BindingKind.Gold,
                    BindingTrigger.OnHit),
                HasRequiredGoldBindingFields);
            registry.Register(
                EffectOperation.IncreaseReward,
                new DirectEnemyEffectExecutor(
                    EffectOperation.IncreaseReward),
                node => node.amount <= 100_000 &&
                        node.limit <= 100_000);
            registry.Register(
                EffectOperation.BindPoison,
                new BindingEffectExecutor(
                    BindingKind.Poison,
                    BindingTrigger.OnHit),
                HasRequiredPoisonBindingFields);
            registry.Register(
                EffectOperation.ApplyPoison,
                new ApplyStatusEffectExecutor(StatusType.Poison),
                HasRequiredPoisonStatusFields);
            RegisterOperations(
                registry,
                operation => operation == EffectOperation.EnlargeProjectile ||
                             operation == EffectOperation.ShrinkProjectile
                    ? (IEffectExecutor)new ProjectileModifierEffectExecutor(
                        operation)
                    : new DirectEnemyEffectExecutor(operation),
                HasThreeBoundedMultipliers,
                EffectOperation.EnlargeProjectile,
                EffectOperation.EnlargeEnemy,
                EffectOperation.ShrinkProjectile,
                EffectOperation.ShrinkEnemy);
            registry.Register(
                EffectOperation.BindStun,
                new BindingEffectExecutor(
                    BindingKind.Stun,
                    BindingTrigger.OnFirstHit),
                HasRequiredStunBindingFields);
            registry.Register(
                EffectOperation.ApplyStun,
                new ApplyStatusEffectExecutor(StatusType.Stun),
                HasRequiredStunStatusFields);
        }

        private static void RegisterCommonOperations(
            EffectRegistry registry)
        {
            Func<EffectOperation, IEffectExecutor> executor =
                operation => new CommonCardEffectExecutor(operation);
            registry.Register(
                EffectOperation.ConfigureProjectileRicochet,
                executor(EffectOperation.ConfigureProjectileRicochet),
                node => node.amount > 0 &&
                        IsBoundedMultiplier(node.amount2) &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.ApplyEnemyRicochet,
                executor(EffectOperation.ApplyEnemyRicochet),
                node => node.amount2 > 0 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0);
            RegisterOperations(
                registry,
                executor,
                node => node.amount > 0 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0,
                EffectOperation.BindBleed,
                EffectOperation.ApplyBleed);
            registry.Register(
                EffectOperation.AccelerateProjectile,
                executor(EffectOperation.AccelerateProjectile),
                node => IsBoundedMultiplier(node.amount) &&
                        node.limit > 0 &&
                        node.amount2 <= node.limit &&
                        node.limit <= 30_000);
            registry.Register(
                EffectOperation.AccelerateEnemy,
                executor(EffectOperation.AccelerateEnemy),
                node => IsBoundedMultiplier(node.amount) &&
                        node.amount2 <= node.limit &&
                        node.limit <= 100_000);
            registry.Register(
                EffectOperation.EnableProjectileHoming,
                executor(EffectOperation.EnableProjectileHoming),
                HasNoParameters);
            registry.Register(
                EffectOperation.ApplyHomingPriority,
                executor(EffectOperation.ApplyHomingPriority),
                node => node.durationTicks > 0 &&
                        node.maxStacks > 0);
            registry.Register(
                EffectOperation.DelayProjectile,
                executor(EffectOperation.DelayProjectile),
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0);
            registry.Register(
                EffectOperation.ApplyDelay,
                executor(EffectOperation.ApplyDelay),
                node => node.durationTicks > 0 &&
                        node.maxStacks > 0);
        }

        private static void RegisterUncommonOperations(
            EffectRegistry registry)
        {
            Func<EffectOperation, IEffectExecutor> executor =
                operation => new UncommonEffectExecutor(operation);
            RegisterOperations(
                registry,
                executor,
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 30_000,
                EffectOperation.BindCurse,
                EffectOperation.ApplyCurse);
            registry.Register(
                EffectOperation.CreateBindTrap,
                executor(EffectOperation.CreateBindTrap),
                node => node.amount > 0 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0);
            registry.Register(
                EffectOperation.ApplyBind,
                executor(EffectOperation.ApplyBind),
                node => node.amount > 0 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0);
            registry.Register(
                EffectOperation.MakeAirborneProjectile,
                executor(EffectOperation.MakeAirborneProjectile),
                node => node.amount > 0 &&
                        node.amount2 > 0 &&
                        node.amount2 <= 10_000 &&
                        node.durationTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0);
            registry.Register(
                EffectOperation.ApplyAirborne,
                executor(EffectOperation.ApplyAirborne),
                node => node.amount > 0 &&
                        node.amount2 > 0 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0);
            registry.Register(
                EffectOperation.BindShock,
                executor(EffectOperation.BindShock),
                node => node.amount > 0 &&
                        node.amount2 > 0 &&
                        node.amount2 <= 10_000 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0);
            registry.Register(
                EffectOperation.ApplyShock,
                executor(EffectOperation.ApplyShock),
                node => node.amount > 0 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0);
            RegisterOperations(
                registry,
                executor,
                node => node.amount > 0 &&
                        node.amount3 > 0 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0,
                EffectOperation.BindFreeze,
                EffectOperation.ApplyFreeze);
            registry.Register(
                EffectOperation.CreateAfterimageProjectile,
                executor(EffectOperation.CreateAfterimageProjectile),
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0);
            registry.Register(
                EffectOperation.ApplyAfterimage,
                executor(EffectOperation.ApplyAfterimage),
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.EnableProjectilePulse,
                executor(EffectOperation.EnableProjectilePulse),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0);
            RegisterOperations(
                registry,
                executor,
                node => node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0,
                EffectOperation.ApplyEnemyPulse,
                EffectOperation.ApplyEnemyContagion);
            registry.Register(
                EffectOperation.EnableProjectileMagnet,
                executor(EffectOperation.EnableProjectileMagnet),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.amount2 > 0 &&
                        node.amount2 <= 10_000 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.ApplyEnemyMagnet,
                executor(EffectOperation.ApplyEnemyMagnet),
                node => node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.EnableProjectileReflect,
                executor(EffectOperation.EnableProjectileReflect),
                node => node.durationTicks > 0 &&
                        node.limit > 0);
            registry.Register(
                EffectOperation.ApplyEnemyReflect,
                executor(EffectOperation.ApplyEnemyReflect),
                node => node.durationTicks > 0 &&
                        node.maxStacks > 0);
            registry.Register(
                EffectOperation.EnableProjectileContagion,
                executor(EffectOperation.EnableProjectileContagion),
                node => node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0);
            RegisterOperations(
                registry,
                executor,
                node => node.durationTicks > 0 &&
                        node.maxStacks > 0,
                EffectOperation.BindSeal,
                EffectOperation.ApplySeal);
            RegisterOperations(
                registry,
                executor,
                node => node.amount > 0 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.limit > 0 &&
                        node.limit <= 10_000 &&
                        node.chanceBps > 0,
                EffectOperation.BindCorrosion,
                EffectOperation.ApplyCorrosion);
            registry.Register(
                EffectOperation.EnableProjectileOrbit,
                executor(EffectOperation.EnableProjectileOrbit),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.ApplyEnemyOrbit,
                executor(EffectOperation.ApplyEnemyOrbit),
                node => node.amount > 0 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 10_000);
            RegisterOperations(
                registry,
                executor,
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0,
                EffectOperation.BindLifesteal,
                EffectOperation.ApplyLifesteal);
            RegisterOperations(
                registry,
                executor,
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.maxStacks > 0 &&
                        IsBoundedMultiplier(node.limit),
                EffectOperation.BindFear,
                EffectOperation.ApplyFear);
        }

        private static void RegisterRareOperations(
            EffectRegistry registry)
        {
            Func<EffectOperation, IEffectExecutor> generation =
                operation =>
                    new RareGenerationMotionEffectExecutor(operation);
            registry.Register(
                EffectOperation.DuplicateProjectile,
                generation(EffectOperation.DuplicateProjectile),
                node => IsBoundedMultiplier(node.amount));
            registry.Register(
                EffectOperation.DuplicateEnemy,
                generation(EffectOperation.DuplicateEnemy),
                node => IsBoundedMultiplier(node.amount) &&
                        node.amount2 > 0 &&
                        node.amount2 <= 10_000);
            registry.Register(
                EffectOperation.SacrificeProjectile,
                generation(EffectOperation.SacrificeProjectile),
                node => IsBoundedMultiplier(node.amount) &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.SacrificeEnemy,
                generation(EffectOperation.SacrificeEnemy),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 32);
            registry.Register(
                EffectOperation.ConfigureProjectileReturn,
                generation(EffectOperation.ConfigureProjectileReturn),
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0);
            registry.Register(
                EffectOperation.RewindEnemy,
                generation(EffectOperation.RewindEnemy),
                node => node.durationTicks > 0);
            RegisterOperations(
                registry,
                generation,
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0,
                EffectOperation.ConfigureProjectileRetrograde,
                EffectOperation.ApplyEnemyRetrograde);

            Func<EffectOperation, IEffectExecutor> resonance =
                operation =>
                    new RareResonanceTimeEffectExecutor(operation);
            RegisterOperations(
                registry,
                resonance,
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.radiusMilli > 0 &&
                        node.limit >= node.amount &&
                        node.limit <= 30_000,
                EffectOperation.ConfigureProjectileResonance,
                EffectOperation.ApplyEnemyResonance);
            registry.Register(
                EffectOperation.ConfigureProjectileAbsorb,
                resonance(EffectOperation.ConfigureProjectileAbsorb),
                node => IsBoundedMultiplier(node.amount) &&
                        node.amount2 > 0 &&
                        node.amount2 <= 30_000 &&
                        node.radiusMilli > 0);
            registry.Register(
                EffectOperation.ApplyEnemyAbsorb,
                resonance(EffectOperation.ApplyEnemyAbsorb),
                node => IsBoundedMultiplier(node.amount) &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 16);
            registry.Register(
                EffectOperation.ConfigureProjectileTimeStop,
                resonance(EffectOperation.ConfigureProjectileTimeStop),
                node => node.amount > 0 &&
                        node.amount2 > 0 &&
                        node.amount2 <= 32 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 32);
            registry.Register(
                EffectOperation.ApplyEnemyTimeStop,
                resonance(EffectOperation.ApplyEnemyTimeStop),
                node => node.durationTicks > 0);
            registry.Register(
                EffectOperation.ConfigureProjectileMutation,
                resonance(EffectOperation.ConfigureProjectileMutation),
                node => node.durationTicks > 0 &&
                        node.limit > 0 &&
                        node.limit <= 4);
            registry.Register(
                EffectOperation.ApplyEnemyMutation,
                resonance(EffectOperation.ApplyEnemyMutation),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.amount2 > 0 &&
                        node.amount2 <= 10_000);

            Func<EffectOperation, IEffectExecutor> death =
                operation =>
                    new RareDeathChainEffectExecutor(operation);
            registry.Register(
                EffectOperation.EnableProjectileExecute,
                death(EffectOperation.EnableProjectileExecute),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        node.amount2 > 0 &&
                        node.amount2 <= 100_000);
            registry.Register(
                EffectOperation.ApplyEnemyExecute,
                death(EffectOperation.ApplyEnemyExecute),
                node => node.amount > 0 &&
                        node.amount <= 10_000);
            registry.Register(
                EffectOperation.EnableProjectileParasite,
                death(EffectOperation.EnableProjectileParasite),
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.limit > 0 &&
                        node.limit <= 64);
            registry.Register(
                EffectOperation.ApplyEnemyParasite,
                death(EffectOperation.ApplyEnemyParasite),
                node => node.amount > 0 &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 32);
            registry.Register(
                EffectOperation.EnableProjectileRebirth,
                death(EffectOperation.EnableProjectileRebirth),
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0);
            registry.Register(
                EffectOperation.ApplyEnemyRebirth,
                death(EffectOperation.ApplyEnemyRebirth),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        IsBoundedMultiplier(node.amount2));
            RegisterOperations(
                registry,
                death,
                node => IsBoundedMultiplier(node.amount) &&
                        node.radiusMilli > 0,
                EffectOperation.EnableProjectileChain,
                EffectOperation.ApplyEnemyChain);
        }

        private static void RegisterLegendaryOperations(
            EffectRegistry registry)
        {
            Func<EffectOperation, IEffectExecutor> grammar =
                operation =>
                    new LegendaryCardEffectExecutor(operation);

            registry.Register(
                EffectOperation.EnableRecursion,
                grammar(EffectOperation.EnableRecursion),
                node => node.limit > 0 &&
                        node.limit <= 1);
            registry.Register(
                EffectOperation.ReverseProgramOrder,
                grammar(EffectOperation.ReverseProgramOrder),
                HasNoParameters);
            RegisterOperations(
                registry,
                grammar,
                node => IsBoundedMultiplier(node.amount) &&
                        node.limit > 0 &&
                        node.limit <= 1,
                EffectOperation.EnableProjectileDualInterpretation,
                EffectOperation.ApplyEnemyDualInterpretation);
            RegisterOperations(
                registry,
                grammar,
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 32,
                EffectOperation.EnableProjectileInfiniteOrbit,
                EffectOperation.ApplyEnemyInfiniteOrbit);
            RegisterOperations(
                registry,
                grammar,
                node => IsBoundedMultiplier(node.amount) &&
                        node.limit > 0 &&
                        node.limit <= 32,
                EffectOperation.EnableProjectileOverclone,
                EffectOperation.ApplyEnemyOverclone);
            registry.Register(
                EffectOperation.EnableProjectileForbiddenDeal,
                grammar(EffectOperation.EnableProjectileForbiddenDeal),
                node => node.amount > 0 &&
                        node.amount <= 1000 &&
                        IsBoundedMultiplier(node.amount2) &&
                        node.limit > 0 &&
                        node.limit <= 32);
            registry.Register(
                EffectOperation.ApplyEnemyForbiddenDeal,
                grammar(EffectOperation.ApplyEnemyForbiddenDeal),
                node => node.amount > 0 &&
                        IsBoundedMultiplier(node.amount2) &&
                        IsBoundedMultiplier(node.amount3) &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.limit > 0 &&
                        node.limit <= 64);
            RegisterOperations(
                registry,
                grammar,
                node => IsBoundedMultiplier(node.amount) &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 32,
                EffectOperation.EnableProjectileLastCommand,
                EffectOperation.ApplyEnemyLastCommand);
            registry.Register(
                EffectOperation.EnableProjectileFateLock,
                grammar(EffectOperation.EnableProjectileFateLock),
                node => IsBoundedMultiplier(node.amount) &&
                        node.limit > 0 &&
                        node.limit <= 1);
            registry.Register(
                EffectOperation.ApplyEnemyFateLock,
                grammar(EffectOperation.ApplyEnemyFateLock),
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.limit > 0 &&
                        node.limit <= 1);
            registry.Register(
                EffectOperation.EnableProjectileOverload,
                grammar(EffectOperation.EnableProjectileOverload),
                node => IsBoundedMultiplier(node.amount) &&
                        IsBoundedMultiplier(node.amount2) &&
                        node.durationTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 1);
            registry.Register(
                EffectOperation.ApplyEnemyOverload,
                grammar(EffectOperation.ApplyEnemyOverload),
                node => IsBoundedMultiplier(node.amount) &&
                        IsBoundedMultiplier(node.amount2) &&
                        IsBoundedMultiplier(node.amount3) &&
                        node.durationTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 1);
        }

        private static void RegisterMythicOperations(
            EffectRegistry registry)
        {
            Func<EffectOperation, IEffectExecutor> rules =
                operation =>
                    new MythicCardEffectExecutor(operation);

            registry.Register(
                EffectOperation.EnableProjectileSingularity,
                rules(EffectOperation.EnableProjectileSingularity),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        IsBoundedMultiplier(node.amount2) &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 64);
            registry.Register(
                EffectOperation.ApplyEnemySingularity,
                rules(EffectOperation.ApplyEnemySingularity),
                node => node.amount > 0 &&
                        node.amount <= 10_000 &&
                        IsBoundedMultiplier(node.amount2) &&
                        IsBoundedMultiplier(node.amount3) &&
                        node.durationTicks > 0 &&
                        node.intervalTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 64);
            registry.Register(
                EffectOperation.EnableProjectilePhoenixCore,
                rules(EffectOperation.EnableProjectilePhoenixCore),
                node => IsBoundedMultiplier(node.amount) &&
                        IsBoundedMultiplier(node.amount2) &&
                        IsBoundedMultiplier(node.amount3) &&
                        node.durationTicks > 0 &&
                        node.limit > 0 &&
                        node.limit <= 1);
            registry.Register(
                EffectOperation.ApplyEnemyPhoenixCore,
                rules(EffectOperation.ApplyEnemyPhoenixCore),
                node => IsBoundedMultiplier(node.amount) &&
                        IsBoundedMultiplier(node.amount2) &&
                        node.durationTicks > 0 &&
                        node.maxStacks > 0 &&
                        node.limit > 0 &&
                        node.limit <= 1);
            RegisterOperations(
                registry,
                rules,
                node => IsBoundedMultiplier(node.amount) &&
                        node.durationTicks > 0 &&
                        node.radiusMilli > 0 &&
                        node.limit == 2,
                EffectOperation.CreateProjectileTimeRift,
                EffectOperation.ApplyEnemyTimeRift);
            RegisterOperations(
                registry,
                rules,
                node => IsBoundedMultiplier(node.amount) &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 1,
                EffectOperation.CreateProjectileMirrorWorld,
                EffectOperation.ApplyEnemyMirrorWorld);
            RegisterOperations(
                registry,
                rules,
                node => IsBoundedMultiplier(node.amount) &&
                        node.radiusMilli > 0 &&
                        node.limit > 0 &&
                        node.limit <= 16,
                EffectOperation.EnableProjectileOuroboros,
                EffectOperation.ApplyEnemyOuroboros);
        }

        private static void RegisterOperations(
            EffectRegistry registry,
            Func<EffectOperation, IEffectExecutor> createExecutor,
            EffectNodeValidator validator,
            params EffectOperation[] operations)
        {
            for (int i = 0; i < operations.Length; i++)
            {
                EffectOperation operation = operations[i];
                registry.Register(
                    operation,
                    createExecutor(operation),
                    validator);
            }
        }

        private static bool HasRequiredBurnStatusFields(
            EffectNodeDto node)
        {
            return node != null &&
                   node.amount > 0 &&
                   node.durationTicks > 0 &&
                   node.intervalTicks > 0 &&
                   node.maxStacks > 0 &&
                   node.radiusMilli > 0;
        }

        private static bool HasRequiredGoldBindingFields(
            EffectNodeDto node)
        {
            return node != null &&
                   node.amount > 0 &&
                   node.amount2 > 0 &&
                   node.limit > 0;
        }

        private static bool HasRequiredPoisonBindingFields(
            EffectNodeDto node)
        {
            return node != null &&
                   node.amount > 0 &&
                   node.amount2 > 0 &&
                   node.durationTicks > 0 &&
                   node.intervalTicks > 0 &&
                   node.maxStacks > 0 &&
                   node.radiusMilli > 0;
        }

        private static bool HasRequiredPoisonStatusFields(
            EffectNodeDto node)
        {
            return node != null &&
                   node.amount > 0 &&
                   node.durationTicks > 0 &&
                   node.intervalTicks > 0 &&
                   node.maxStacks > 0;
        }

        private static bool HasRequiredStunBindingFields(
            EffectNodeDto node)
        {
            return node != null &&
                   node.amount > 0 &&
                   node.durationTicks > 0 &&
                   node.limit > 0;
        }

        private static bool HasRequiredStunStatusFields(
            EffectNodeDto node)
        {
            return node != null &&
                   node.amount > 0 &&
                   node.durationTicks > 0;
        }

        private static bool HasNoParameters(EffectNodeDto node)
        {
            return node != null &&
                   node.amount == 0 &&
                   node.amount2 == 0 &&
                   node.amount3 == 0 &&
                   node.durationTicks == 0 &&
                   node.intervalTicks == 0 &&
                   node.maxStacks == 0 &&
                   node.radiusMilli == 0 &&
                   node.limit == 0 &&
                   node.chanceBps == 0 &&
                   string.IsNullOrWhiteSpace(node.referenceId);
        }

        private static bool HasThreeBoundedMultipliers(
            EffectNodeDto node)
        {
            return IsBoundedMultiplier(node.amount) &&
                   IsBoundedMultiplier(node.amount2) &&
                   IsBoundedMultiplier(node.amount3);
        }

        private static bool HasBoundedKnockback(EffectNodeDto node)
        {
            return node.amount <= 100_000 &&
                   node.amount2 <= 1_000_000_000;
        }

        private static bool HasBoundedMark(EffectNodeDto node)
        {
            return node.amount <= 10_000 &&
                   node.limit <= 30_000;
        }

        private static bool IsBoundedMultiplier(int value)
        {
            return value > 0 && value <= 30_000;
        }

        /// <summary>
        /// Phase 1의 이중 해석 데이터가 사용하는 연산별 주체 계약이다.
        /// 새 enum 항목은 이 목록과 모듈 등록을 모두 명시적으로 확장해야 하므로
        /// 이름 패턴이나 enum 홀짝에 기대어 잘못된 문맥을 추론하지 않는다.
        /// </summary>
        private static EffectSubjectMask ResolveDefaultSubjectMask(
            EffectOperation operation)
        {
            switch (operation)
            {
                case EffectOperation.Split:
                case EffectOperation.AddPierce:
                case EffectOperation.BindExplosion:
                case EffectOperation.EnableRecursion:
                case EffectOperation.ReverseProgramOrder:
                    return EffectSubjectMask.Both;

                case EffectOperation.BindBurn:
                case EffectOperation.ModifyProjectileSlow:
                case EffectOperation.BindKnockback:
                case EffectOperation.BindMark:
                case EffectOperation.BindGoldOnHit:
                case EffectOperation.BindPoison:
                case EffectOperation.EnlargeProjectile:
                case EffectOperation.ShrinkProjectile:
                case EffectOperation.BindStun:
                case EffectOperation.ConfigureProjectileRicochet:
                case EffectOperation.BindBleed:
                case EffectOperation.AccelerateProjectile:
                case EffectOperation.EnableProjectileHoming:
                case EffectOperation.DelayProjectile:
                case EffectOperation.BindCurse:
                case EffectOperation.CreateBindTrap:
                case EffectOperation.MakeAirborneProjectile:
                case EffectOperation.BindShock:
                case EffectOperation.BindFreeze:
                case EffectOperation.CreateAfterimageProjectile:
                case EffectOperation.EnableProjectilePulse:
                case EffectOperation.EnableProjectileMagnet:
                case EffectOperation.EnableProjectileReflect:
                case EffectOperation.EnableProjectileContagion:
                case EffectOperation.BindSeal:
                case EffectOperation.BindCorrosion:
                case EffectOperation.EnableProjectileOrbit:
                case EffectOperation.BindLifesteal:
                case EffectOperation.BindFear:
                case EffectOperation.DuplicateProjectile:
                case EffectOperation.SacrificeProjectile:
                case EffectOperation.ConfigureProjectileReturn:
                case EffectOperation.ConfigureProjectileRetrograde:
                case EffectOperation.ConfigureProjectileResonance:
                case EffectOperation.ConfigureProjectileAbsorb:
                case EffectOperation.ConfigureProjectileTimeStop:
                case EffectOperation.ConfigureProjectileMutation:
                case EffectOperation.EnableProjectileExecute:
                case EffectOperation.EnableProjectileParasite:
                case EffectOperation.EnableProjectileRebirth:
                case EffectOperation.EnableProjectileChain:
                case EffectOperation.EnableProjectileDualInterpretation:
                case EffectOperation.EnableProjectileInfiniteOrbit:
                case EffectOperation.EnableProjectileOverclone:
                case EffectOperation.EnableProjectileForbiddenDeal:
                case EffectOperation.EnableProjectileLastCommand:
                case EffectOperation.EnableProjectileFateLock:
                case EffectOperation.EnableProjectileOverload:
                case EffectOperation.EnableProjectileSingularity:
                case EffectOperation.EnableProjectilePhoenixCore:
                case EffectOperation.CreateProjectileTimeRift:
                case EffectOperation.CreateProjectileMirrorWorld:
                case EffectOperation.EnableProjectileOuroboros:
                    return EffectSubjectMask.Projectile;

                case EffectOperation.ApplyBurn:
                case EffectOperation.ApplySlow:
                case EffectOperation.ApplyKnockback:
                case EffectOperation.ApplyMark:
                case EffectOperation.IncreaseReward:
                case EffectOperation.ApplyPoison:
                case EffectOperation.EnlargeEnemy:
                case EffectOperation.ShrinkEnemy:
                case EffectOperation.ApplyStun:
                case EffectOperation.ApplyEnemyRicochet:
                case EffectOperation.ApplyBleed:
                case EffectOperation.AccelerateEnemy:
                case EffectOperation.ApplyHomingPriority:
                case EffectOperation.ApplyDelay:
                case EffectOperation.ApplyCurse:
                case EffectOperation.ApplyBind:
                case EffectOperation.ApplyAirborne:
                case EffectOperation.ApplyShock:
                case EffectOperation.ApplyFreeze:
                case EffectOperation.ApplyAfterimage:
                case EffectOperation.ApplyEnemyPulse:
                case EffectOperation.ApplyEnemyMagnet:
                case EffectOperation.ApplyEnemyReflect:
                case EffectOperation.ApplyEnemyContagion:
                case EffectOperation.ApplySeal:
                case EffectOperation.ApplyCorrosion:
                case EffectOperation.ApplyEnemyOrbit:
                case EffectOperation.ApplyLifesteal:
                case EffectOperation.ApplyFear:
                case EffectOperation.DuplicateEnemy:
                case EffectOperation.SacrificeEnemy:
                case EffectOperation.RewindEnemy:
                case EffectOperation.ApplyEnemyRetrograde:
                case EffectOperation.ApplyEnemyResonance:
                case EffectOperation.ApplyEnemyAbsorb:
                case EffectOperation.ApplyEnemyTimeStop:
                case EffectOperation.ApplyEnemyMutation:
                case EffectOperation.ApplyEnemyExecute:
                case EffectOperation.ApplyEnemyParasite:
                case EffectOperation.ApplyEnemyRebirth:
                case EffectOperation.ApplyEnemyChain:
                case EffectOperation.ApplyEnemyDualInterpretation:
                case EffectOperation.ApplyEnemyInfiniteOrbit:
                case EffectOperation.ApplyEnemyOverclone:
                case EffectOperation.ApplyEnemyForbiddenDeal:
                case EffectOperation.ApplyEnemyLastCommand:
                case EffectOperation.ApplyEnemyFateLock:
                case EffectOperation.ApplyEnemyOverload:
                case EffectOperation.ApplyEnemySingularity:
                case EffectOperation.ApplyEnemyPhoenixCore:
                case EffectOperation.ApplyEnemyTimeRift:
                case EffectOperation.ApplyEnemyMirrorWorld:
                case EffectOperation.ApplyEnemyOuroboros:
                    return EffectSubjectMask.Enemy;

                default:
                    throw new InvalidOperationException(
                        "No default subject contract is defined for " +
                        operation + ".");
            }
        }

        /// <summary>
        /// 모든 enum 멤버가 정확히 한 descriptor를 가진 registry만 읽기 전용으로
        /// 전환한다. 중복은 Register에서, 누락은 이 경계에서 즉시 실패한다.
        /// </summary>
        public void Freeze()
        {
            if (IsFrozen)
            {
                return;
            }

            Array operations = Enum.GetValues(typeof(EffectOperation));
            for (int i = 0; i < operations.Length; i++)
            {
                EffectOperation operation =
                    (EffectOperation)operations.GetValue(i);
                if (!IsRegistered(operation))
                {
                    throw new InvalidOperationException(
                        "Effect registry cannot be frozen because " +
                        operation + " is not registered.");
                }
            }

            if (registeredOperationCount != operations.Length)
            {
                throw new InvalidOperationException(
                    "Effect registry coverage is not exactly once: " +
                    registeredOperationCount + " descriptors for " +
                    operations.Length + " enum values.");
            }

            IsFrozen = true;
        }

        /// <summary>
        /// 연산 하나에 executor를 등록한다.
        /// 기존 실행 전용 API와 호환되는 overload다. 검증에 사용할 registry라면
        /// validator overload를 사용해야 하며, 그렇지 않으면 검증 시 즉시 실패한다.
        /// </summary>
        /// <param name="operation">JSON 효과 노드가 선택할 연산 종류다.</param>
        /// <param name="executor">해당 연산을 실제 상태 변경으로 번역할 구현체다.</param>
        public void Register(EffectOperation operation, IEffectExecutor executor)
        {
            Register(
                operation,
                executor,
                node => throw new InvalidOperationException(
                    "No effect validator registered for " +
                    operation + "."));
        }

        /// <summary>
        /// 기존 호출자용 등록 API다. 기본 모듈 조립 중에는 현재 모듈과 명시된
        /// Phase 1 주체 계약을 사용하고, 독립 custom registry에서는 Legacy/Both로 등록한다.
        /// </summary>
        public void Register(
            EffectOperation operation,
            IEffectExecutor executor,
            EffectNodeValidator validator)
        {
            bool composingDefaultModule =
                !string.IsNullOrEmpty(activeModuleId);
            Register(
                composingDefaultModule
                    ? activeModuleId
                    : LegacyModuleId,
                composingDefaultModule
                    ? ResolveDefaultSubjectMask(operation)
                    : EffectSubjectMask.Both,
                operation,
                executor,
                validator);
        }

        /// <summary>
        /// 외부 효과 모듈이 소유권과 허용 주체를 빠뜨리지 않고 descriptor를
        /// 등록하는 확장 API다. 이미 등록된 연산과 frozen registry 변경은 거절한다.
        /// </summary>
        public void Register(
            string moduleId,
            EffectSubjectMask supportedSubjects,
            EffectOperation operation,
            IEffectExecutor executor,
            EffectNodeValidator validator)
        {
            if (IsFrozen)
            {
                throw new InvalidOperationException(
                    "A frozen effect registry cannot be modified.");
            }
            if (!Enum.IsDefined(typeof(EffectOperation), operation))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }

            int index = (int)operation;
            if (index < 0 || index >= descriptors.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }
            if (descriptors[index] != null)
            {
                throw new InvalidOperationException(
                    "Effect operation " + operation +
                    " is already registered by module '" +
                    descriptors[index].ModuleId + "'.");
            }

            descriptors[index] = new EffectOperationDescriptor(
                operation,
                executor,
                validator,
                moduleId,
                supportedSubjects);
            registeredOperationCount++;
        }

        /// <summary>현재 enum 값에 실행 코드가 연결되어 있는지 확인한다.</summary>
        public bool IsRegistered(EffectOperation operation)
        {
            int index = (int)operation;
            return index >= 0 &&
                   index < descriptors.Length &&
                   descriptors[index] != null;
        }

        public bool SupportsSubject(
            EffectOperation operation,
            SubjectType subjectType)
        {
            return GetDescriptor(operation)
                .SupportsSubject(subjectType);
        }

        /// <summary>
        /// 등록된 연산의 의미 범위를 검사한다. 미등록 연산은 설정 오류이므로 즉시 실패한다.
        /// </summary>
        public bool IsValid(
            EffectOperation operation,
            EffectNodeDto node)
        {
            return GetDescriptor(operation).IsValid(node);
        }

        public bool IsValid(
            EffectOperation operation,
            SubjectType subjectType,
            EffectNodeDto node)
        {
            return GetDescriptor(operation).IsValid(
                subjectType,
                node);
        }

        /// <summary>연산의 실행기와 validator를 함께 조회한다.</summary>
        public EffectOperationDescriptor GetDescriptor(
            EffectOperation operation)
        {
            if (!IsRegistered(operation))
            {
                throw new InvalidOperationException(
                    "No effect descriptor registered for " +
                    operation + ".");
            }

            return descriptors[(int)operation];
        }

        /// <summary>
        /// 카드 실행에 사용할 executor를 가져온다.
        /// 미등록 연산을 조용히 무시하지 않고 즉시 예외로 알려 콘텐츠/코드 불일치를 드러낸다.
        /// </summary>
        public IEffectExecutor Get(EffectOperation operation)
        {
            return GetDescriptor(operation).Executor;
        }
    }

    // 분열은 단순 수치 변경이 아니라 대상 수와 이후 카드 흐름을 둘로 나누므로
    // 전용 결과(EffectExecutionOutcome.Split)를 반환하는 제어 흐름 executor다.
    internal sealed class SplitEffectExecutor : IEffectExecutor
    {
        /// <summary>현재 문맥에 따라 탄환 또는 적 분열을 요청한다.</summary>
        public EffectExecutionOutcome Execute(
            IEffectExecutionHost simulation,
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
            IEffectExecutionHost simulation,
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
            IEffectExecutionHost simulation,
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
            IEffectExecutionHost simulation,
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
            IEffectExecutionHost simulation,
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
            IEffectExecutionHost simulation,
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
            IEffectExecutionHost simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ApplyDirectEnemyEffect(context, operation, node);
            return EffectExecutionOutcome.Continue();
        }
    }
}
