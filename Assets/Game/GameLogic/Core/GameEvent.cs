using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// Immutable simulation work item. EventId and EnqueueSequence are assigned
    /// by EventQueue; all remaining fields are supplied by the producer.
    /// </summary>
    /// <remarks>
    /// GameEvent는 “이미 일어난 사실을 보여 주는 로그”만이 아니라 앞으로 처리할 작업 단위다.
    /// 값 형식(readonly struct)이어서 큐에 등록된 뒤 필드가 몰래 바뀌지 않는다.
    /// 새 효과가 또 다른 효과를 만들 때 즉시 재귀 호출하지 않고 새 GameEvent를 등록하므로
    /// 정렬, 연쇄 깊이, 이벤트 수 예산을 한 곳에서 통제할 수 있다.
    /// </remarks>
    public readonly struct GameEvent : IEquatable<GameEvent>
    {
        /// <summary>큐가 등록 시 부여하는 이벤트 고유 ID다. 등록 전에는 Invalid다.</summary>
        public EventId EventId { get; }
        /// <summary>이 이벤트가 속한 최상위 연쇄작용 ID다.</summary>
        public ChainId RootChainId { get; }
        /// <summary>이 이벤트를 직접 만들어 낸 부모 이벤트 ID다.</summary>
        public EventId ParentEventId { get; }
        /// <summary>카드 실행, 피해 요청, 사망 등 사건의 구체적인 종류다.</summary>
        public EventType EventType { get; }
        /// <summary>같은 틱에서 이 이벤트가 처리될 고정 단계다.</summary>
        public EventPhase Phase { get; }
        /// <summary><see cref="Phase"/>의 의미를 명확히 드러내는 읽기 전용 별칭이다.</summary>
        public EventPhase EventPhase { get { return Phase; } }
        /// <summary>효과를 발생시킨 타워 인스턴스 ID다.</summary>
        public TowerId SourceTowerId { get; }
        /// <summary>효과를 발생시킨 카드 정의 ID다.</summary>
        public CardId SourceCardId { get; }
        /// <summary>효과를 발생시킨 적 또는 탄환 엔티티 ID다.</summary>
        public EntityId SourceEntityId { get; }
        /// <summary>효과가 적용될 적 또는 탄환 엔티티 ID다.</summary>
        public EntityId SubjectEntityId { get; }
        /// <summary>대상을 탄환 프로그램과 적 프로그램 중 어느 쪽으로 해석할지 나타낸다.</summary>
        public SubjectType SubjectType { get; }
        /// <summary>루트 이벤트에서 몇 단계 파생되었는지 나타내며 연쇄 깊이 제한에 사용한다.</summary>
        public int Depth { get; }
        /// <summary>분열·복제 등으로 원본에서 몇 세대 떨어졌는지를 나타낸다.</summary>
        public int Generation { get; }
        /// <summary>범위, 지속 피해, 경제 등 이벤트의 복수 성격 표식이다.</summary>
        public EventTags Tags { get; }
        /// <summary>보상 이벤트일 때 골드의 출처다. 경제 재트리거 방지에 사용한다.</summary>
        public RewardOrigin RewardOrigin { get; }
        /// <summary>이 이벤트가 실행될 게임 시간이다. 단위는 프레임이 아닌 고정 시뮬레이션 틱이다.</summary>
        public long SimulationTick { get; }
        /// <summary>한 번의 타워/카드 발동을 묶어 반복 토큰을 공유하기 위한 ID다.</summary>
        public ActivationId ActivationId { get; }
        /// <summary>큐에 들어간 순번이다. 틱과 단계가 같을 때 안정적인 최종 정렬 기준이다.</summary>
        public ulong EnqueueSequence { get; }

        // 이벤트마다 필요한 작은 수치 자료를 담는 공용 슬롯이다.
        // 각 슬롯의 정확한 의미는 EventType 처리 코드가 결정한다. 공용 컨테이너를 쓰면
        // 이벤트 종류마다 객체를 생성하지 않아도 되어 WebGL 메모리와 GC 부담을 줄일 수 있다.
        /// <summary>이벤트 종류별 첫 번째 정수 페이로드다.</summary>
        public int PayloadA { get; }
        /// <summary>이벤트 종류별 두 번째 정수 페이로드다.</summary>
        public int PayloadB { get; }
        /// <summary>이벤트 종류별 세 번째 정수 페이로드다.</summary>
        public int PayloadC { get; }
        /// <summary>피해량 등 int보다 큰 범위가 필요한 이벤트별 정수 페이로드다.</summary>
        public long PayloadValue { get; }

        /// <summary>
        /// 큐에서 EventId를 받았고 None이 아닌 실제 이벤트이면 true다.
        /// </summary>
        public bool IsScheduled
        {
            get { return EventId.IsValid && EventType != EventType.None; }
        }

        /// <summary>
        /// 아직 큐에 등록되지 않은 이벤트 작업을 만든다.
        /// EventId와 EnqueueSequence는 이후 <see cref="EventQueue"/>가 부여한다.
        /// </summary>
        public GameEvent(
            long simulationTick,
            EventPhase phase,
            EventType eventType,
            ChainId rootChainId,
            EventId parentEventId,
            ActivationId activationId,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            EntityId subjectEntityId,
            SubjectType subjectType,
            int depth,
            int generation,
            EventTags tags,
            RewardOrigin rewardOrigin,
            int payloadA = 0,
            int payloadB = 0,
            int payloadC = 0,
            long payloadValue = 0L)
            : this(
                EventId.Invalid,
                rootChainId,
                parentEventId,
                eventType,
                phase,
                sourceTowerId,
                sourceCardId,
                sourceEntityId,
                subjectEntityId,
                subjectType,
                depth,
                generation,
                tags,
                rewardOrigin,
                simulationTick,
                activationId,
                0UL,
                payloadA,
                payloadB,
                payloadC,
                payloadValue)
        {
            // 시간, 깊이, 세대는 음수가 될 수 없다. 잘못된 이벤트를 큐에 넣기 전에
            // 생성 지점에서 즉시 거절하면 이후 시스템이 단순하고 예측 가능해진다.
            if (simulationTick < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(simulationTick));
            }

            if (eventType == EventType.None)
            {
                throw new ArgumentOutOfRangeException(nameof(eventType));
            }

            if (depth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(depth));
            }

            if (generation < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }
        }

        private GameEvent(
            EventId eventId,
            ChainId rootChainId,
            EventId parentEventId,
            EventType eventType,
            EventPhase phase,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            EntityId subjectEntityId,
            SubjectType subjectType,
            int depth,
            int generation,
            EventTags tags,
            RewardOrigin rewardOrigin,
            long simulationTick,
            ActivationId activationId,
            ulong enqueueSequence,
            int payloadA,
            int payloadB,
            int payloadC,
            long payloadValue)
        {
            EventId = eventId;
            RootChainId = rootChainId;
            ParentEventId = parentEventId;
            EventType = eventType;
            Phase = phase;
            SourceTowerId = sourceTowerId;
            SourceCardId = sourceCardId;
            SourceEntityId = sourceEntityId;
            SubjectEntityId = subjectEntityId;
            SubjectType = subjectType;
            Depth = depth;
            Generation = generation;
            Tags = tags;
            RewardOrigin = rewardOrigin;
            SimulationTick = simulationTick;
            ActivationId = activationId;
            EnqueueSequence = enqueueSequence;
            PayloadA = payloadA;
            PayloadB = payloadB;
            PayloadC = payloadC;
            PayloadValue = payloadValue;
        }

        /// <summary>
        /// 큐가 부여한 ID와 등록 순번을 반영한 복사본을 만든다.
        /// 이벤트 생산자가 임의 순번을 주입하지 못하도록 GameLogic 내부에만 공개한다.
        /// </summary>
        internal GameEvent WithSchedule(EventId eventId, ulong enqueueSequence)
        {
            return new GameEvent(
                eventId,
                RootChainId,
                ParentEventId,
                EventType,
                Phase,
                SourceTowerId,
                SourceCardId,
                SourceEntityId,
                SubjectEntityId,
                SubjectType,
                Depth,
                Generation,
                Tags,
                RewardOrigin,
                SimulationTick,
                ActivationId,
                enqueueSequence,
                PayloadA,
                PayloadB,
                PayloadC,
                PayloadValue);
        }

        /// <summary>
        /// 이후 결과에 영향을 줄 수 있는 이벤트의 모든 필드를 정해진 순서로 상태 해시에 넣는다.
        /// </summary>
        public void AppendHash(ref StableHashBuilder hash)
        {
            hash.Add(EventId);
            hash.Add(RootChainId);
            hash.Add(ParentEventId);
            hash.Add((int)EventType);
            hash.Add((int)Phase);
            hash.Add(SourceTowerId);
            hash.Add(SourceCardId);
            hash.Add(SourceEntityId);
            hash.Add(SubjectEntityId);
            hash.Add((int)SubjectType);
            hash.Add(Depth);
            hash.Add(Generation);
            hash.Add((ulong)Tags);
            hash.Add((int)RewardOrigin);
            hash.Add(SimulationTick);
            hash.Add(ActivationId);
            hash.Add(EnqueueSequence);
            hash.Add(PayloadA);
            hash.Add(PayloadB);
            hash.Add(PayloadC);
            hash.Add(PayloadValue);
        }

        /// <summary>모든 필드가 같은 이벤트인지 비교한다.</summary>
        public bool Equals(GameEvent other)
        {
            return EventId == other.EventId &&
                   RootChainId == other.RootChainId &&
                   ParentEventId == other.ParentEventId &&
                   EventType == other.EventType &&
                   Phase == other.Phase &&
                   SourceTowerId == other.SourceTowerId &&
                   SourceCardId == other.SourceCardId &&
                   SourceEntityId == other.SourceEntityId &&
                   SubjectEntityId == other.SubjectEntityId &&
                   SubjectType == other.SubjectType &&
                   Depth == other.Depth &&
                   Generation == other.Generation &&
                   Tags == other.Tags &&
                   RewardOrigin == other.RewardOrigin &&
                   SimulationTick == other.SimulationTick &&
                   ActivationId == other.ActivationId &&
                   EnqueueSequence == other.EnqueueSequence &&
                   PayloadA == other.PayloadA &&
                   PayloadB == other.PayloadB &&
                   PayloadC == other.PayloadC &&
                   PayloadValue == other.PayloadValue;
        }

        /// <summary>object로 전달된 값이 같은 GameEvent인지 비교한다.</summary>
        public override bool Equals(object obj)
        {
            return obj is GameEvent other && Equals(other);
        }

        /// <summary>
        /// 컬렉션 사용을 위한 32비트 해시를 반환한다. 결정적 상태 해시는 별도로 AppendHash를 사용한다.
        /// </summary>
        public override int GetHashCode()
        {
            StableHashBuilder builder = default(StableHashBuilder);
            AppendHash(ref builder);
            ulong hash = builder.Finish();
            return unchecked((int)(hash ^ (hash >> 32)));
        }

        /// <summary>두 이벤트의 모든 필드가 같으면 true다.</summary>
        public static bool operator ==(GameEvent left, GameEvent right)
        {
            return left.Equals(right);
        }

        /// <summary>두 이벤트의 필드 중 하나라도 다르면 true다.</summary>
        public static bool operator !=(GameEvent left, GameEvent right)
        {
            return !left.Equals(right);
        }
    }
}
