using System;

namespace RuleforgeTD.GameLogic.Core
{
    // All IDs are compact compiled/runtime indices. Stable string IDs remain in
    // source content and are resolved to these values during content compilation.
    //
    // 아래 ID들은 내부적으로 모두 int지만 서로 다른 struct 타입으로 감싼다.
    // 그래서 “적 ID를 카드 ID 자리에 넣는” 실수를 컴파일 단계에서 막을 수 있다.
    // JSON의 "ballista" 같은 사람이 읽는 문자열 ID는 콘텐츠 컴파일 때 이 작은 정수로
    // 바뀌며, 런타임 비교·배열 접근·상태 해시에 사용된다. -1은 공통으로 Invalid다.

    /// <summary>전투 중 생성된 적, 탄환 등 개별 개체를 가리키는 런타임 ID다.</summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        /// <summary>실제 개체를 가리키지 않는 특수값이다.</summary>
        public static readonly EntityId Invalid = new EntityId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 엔티티 ID를 만든다.</summary>
        public EntityId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(EntityId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 엔티티 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(EntityId other) { return Value == other.Value; }
        /// <summary>object가 같은 엔티티 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is EntityId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 엔티티 ID가 같으면 true다.</summary>
        public static bool operator ==(EntityId left, EntityId right) { return left.Equals(right); }
        /// <summary>두 엔티티 ID가 다르면 true다.</summary>
        public static bool operator !=(EntityId left, EntityId right) { return !left.Equals(right); }
        /// <summary>왼쪽 엔티티 ID가 더 작으면 true다.</summary>
        public static bool operator <(EntityId left, EntityId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 엔티티 ID가 더 크면 true다.</summary>
        public static bool operator >(EntityId left, EntityId right) { return left.Value > right.Value; }
    }

    /// <summary>플레이어가 배치한 개별 타워 인스턴스를 가리키는 런타임 ID다.</summary>
    public readonly struct TowerId : IEquatable<TowerId>, IComparable<TowerId>
    {
        /// <summary>실제 타워를 가리키지 않는 특수값이다.</summary>
        public static readonly TowerId Invalid = new TowerId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 타워 인스턴스 ID를 만든다.</summary>
        public TowerId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(TowerId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 타워 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(TowerId other) { return Value == other.Value; }
        /// <summary>object가 같은 타워 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is TowerId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 타워 ID가 같으면 true다.</summary>
        public static bool operator ==(TowerId left, TowerId right) { return left.Equals(right); }
        /// <summary>두 타워 ID가 다르면 true다.</summary>
        public static bool operator !=(TowerId left, TowerId right) { return !left.Equals(right); }
        /// <summary>왼쪽 타워 ID가 더 작으면 true다.</summary>
        public static bool operator <(TowerId left, TowerId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 타워 ID가 더 크면 true다.</summary>
        public static bool operator >(TowerId left, TowerId right) { return left.Value > right.Value; }
    }

    /// <summary>발리스타 같은 타워 종류의 컴파일된 콘텐츠 정의를 가리키는 ID다.</summary>
    public readonly struct TowerDefinitionId : IEquatable<TowerDefinitionId>, IComparable<TowerDefinitionId>
    {
        /// <summary>어떤 타워 정의도 가리키지 않는 특수값이다.</summary>
        public static readonly TowerDefinitionId Invalid = new TowerDefinitionId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 타워 정의 ID를 만든다.</summary>
        public TowerDefinitionId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(TowerDefinitionId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 타워 정의 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(TowerDefinitionId other) { return Value == other.Value; }
        /// <summary>object가 같은 타워 정의 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is TowerDefinitionId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 타워 정의 ID가 같으면 true다.</summary>
        public static bool operator ==(TowerDefinitionId left, TowerDefinitionId right) { return left.Equals(right); }
        /// <summary>두 타워 정의 ID가 다르면 true다.</summary>
        public static bool operator !=(TowerDefinitionId left, TowerDefinitionId right) { return !left.Equals(right); }
        /// <summary>왼쪽 타워 정의 ID가 더 작으면 true다.</summary>
        public static bool operator <(TowerDefinitionId left, TowerDefinitionId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 타워 정의 ID가 더 크면 true다.</summary>
        public static bool operator >(TowerDefinitionId left, TowerDefinitionId right) { return left.Value > right.Value; }
    }

    /// <summary>고블린, 러너 같은 적 종류의 컴파일된 콘텐츠 정의를 가리키는 ID다.</summary>
    public readonly struct EnemyDefinitionId : IEquatable<EnemyDefinitionId>, IComparable<EnemyDefinitionId>
    {
        /// <summary>어떤 적 정의도 가리키지 않는 특수값이다.</summary>
        public static readonly EnemyDefinitionId Invalid = new EnemyDefinitionId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 적 정의 ID를 만든다.</summary>
        public EnemyDefinitionId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(EnemyDefinitionId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 적 정의 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(EnemyDefinitionId other) { return Value == other.Value; }
        /// <summary>object가 같은 적 정의 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is EnemyDefinitionId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 적 정의 ID가 같으면 true다.</summary>
        public static bool operator ==(EnemyDefinitionId left, EnemyDefinitionId right) { return left.Equals(right); }
        /// <summary>두 적 정의 ID가 다르면 true다.</summary>
        public static bool operator !=(EnemyDefinitionId left, EnemyDefinitionId right) { return !left.Equals(right); }
        /// <summary>왼쪽 적 정의 ID가 더 작으면 true다.</summary>
        public static bool operator <(EnemyDefinitionId left, EnemyDefinitionId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 적 정의 ID가 더 크면 true다.</summary>
        public static bool operator >(EnemyDefinitionId left, EnemyDefinitionId right) { return left.Value > right.Value; }
    }

    /// <summary>기본 적 데이터에 조합하는 엘리트 특성 정의를 가리키는 ID다.</summary>
    public readonly struct EliteTraitId : IEquatable<EliteTraitId>, IComparable<EliteTraitId>
    {
        public static readonly EliteTraitId Invalid = new EliteTraitId(-1);
        public int Value { get; }
        public bool IsValid { get { return Value >= 0; } }

        public EliteTraitId(int value) { Value = value; }
        public int CompareTo(EliteTraitId other) { return Value.CompareTo(other.Value); }
        public bool Equals(EliteTraitId other) { return Value == other.Value; }
        public override bool Equals(object obj) { return obj is EliteTraitId other && Equals(other); }
        public override int GetHashCode() { return Value; }
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        public static bool operator ==(EliteTraitId left, EliteTraitId right) { return left.Equals(right); }
        public static bool operator !=(EliteTraitId left, EliteTraitId right) { return !left.Equals(right); }
        public static bool operator <(EliteTraitId left, EliteTraitId right) { return left.Value < right.Value; }
        public static bool operator >(EliteTraitId left, EliteTraitId right) { return left.Value > right.Value; }
    }

    /// <summary>분열, 화상 같은 카드의 컴파일된 콘텐츠 정의를 가리키는 ID다.</summary>
    public readonly struct CardId : IEquatable<CardId>, IComparable<CardId>
    {
        /// <summary>어떤 카드도 가리키지 않는 특수값이다.</summary>
        public static readonly CardId Invalid = new CardId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 카드 ID를 만든다.</summary>
        public CardId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(CardId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 카드 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(CardId other) { return Value == other.Value; }
        /// <summary>object가 같은 카드 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is CardId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 카드 ID가 같으면 true다.</summary>
        public static bool operator ==(CardId left, CardId right) { return left.Equals(right); }
        /// <summary>두 카드 ID가 다르면 true다.</summary>
        public static bool operator !=(CardId left, CardId right) { return !left.Equals(right); }
        /// <summary>왼쪽 카드 ID가 더 작으면 true다.</summary>
        public static bool operator <(CardId left, CardId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 카드 ID가 더 크면 true다.</summary>
        public static bool operator >(CardId left, CardId right) { return left.Value > right.Value; }
    }

    /// <summary>화상, 중독, 둔화 같은 상태이상 정의를 가리키는 ID다.</summary>
    public readonly struct StatusId : IEquatable<StatusId>, IComparable<StatusId>
    {
        /// <summary>어떤 상태도 가리키지 않는 특수값이다.</summary>
        public static readonly StatusId Invalid = new StatusId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 상태 ID를 만든다.</summary>
        public StatusId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(StatusId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 상태 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(StatusId other) { return Value == other.Value; }
        /// <summary>object가 같은 상태 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is StatusId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 상태 ID가 같으면 true다.</summary>
        public static bool operator ==(StatusId left, StatusId right) { return left.Equals(right); }
        /// <summary>두 상태 ID가 다르면 true다.</summary>
        public static bool operator !=(StatusId left, StatusId right) { return !left.Equals(right); }
        /// <summary>왼쪽 상태 ID가 더 작으면 true다.</summary>
        public static bool operator <(StatusId left, StatusId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 상태 ID가 더 크면 true다.</summary>
        public static bool operator >(StatusId left, StatusId right) { return left.Value > right.Value; }
    }

    /// <summary>카드 프로그램 안의 실행 연산(executor)을 가리키는 컴파일된 ID다.</summary>
    public readonly struct EffectId : IEquatable<EffectId>, IComparable<EffectId>
    {
        /// <summary>어떤 효과 연산도 가리키지 않는 특수값이다.</summary>
        public static readonly EffectId Invalid = new EffectId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 효과 ID를 만든다.</summary>
        public EffectId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(EffectId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 효과 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(EffectId other) { return Value == other.Value; }
        /// <summary>object가 같은 효과 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is EffectId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 효과 ID가 같으면 true다.</summary>
        public static bool operator ==(EffectId left, EffectId right) { return left.Equals(right); }
        /// <summary>두 효과 ID가 다르면 true다.</summary>
        public static bool operator !=(EffectId left, EffectId right) { return !left.Equals(right); }
        /// <summary>왼쪽 효과 ID가 더 작으면 true다.</summary>
        public static bool operator <(EffectId left, EffectId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 효과 ID가 더 크면 true다.</summary>
        public static bool operator >(EffectId left, EffectId right) { return left.Value > right.Value; }
    }

    /// <summary>이벤트 큐에 실제 등록된 개별 작업을 가리키는 ID다.</summary>
    public readonly struct EventId : IEquatable<EventId>, IComparable<EventId>
    {
        /// <summary>등록된 이벤트를 가리키지 않는 특수값이다.</summary>
        public static readonly EventId Invalid = new EventId(-1);
        /// <summary>큐가 부여한 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 이벤트 ID를 만든다.</summary>
        public EventId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(EventId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 이벤트 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(EventId other) { return Value == other.Value; }
        /// <summary>object가 같은 이벤트 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is EventId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 이벤트 ID가 같으면 true다.</summary>
        public static bool operator ==(EventId left, EventId right) { return left.Equals(right); }
        /// <summary>두 이벤트 ID가 다르면 true다.</summary>
        public static bool operator !=(EventId left, EventId right) { return !left.Equals(right); }
        /// <summary>왼쪽 이벤트 ID가 더 작으면 true다.</summary>
        public static bool operator <(EventId left, EventId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 이벤트 ID가 더 크면 true다.</summary>
        public static bool operator >(EventId left, EventId right) { return left.Value > right.Value; }
    }

    /// <summary>한 번의 루트 행동에서 파생된 전체 연쇄작용을 묶는 ID다.</summary>
    public readonly struct ChainId : IEquatable<ChainId>, IComparable<ChainId>
    {
        /// <summary>어떤 연쇄도 가리키지 않는 특수값이다.</summary>
        public static readonly ChainId Invalid = new ChainId(-1);
        /// <summary>런타임 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 연쇄 ID를 만든다.</summary>
        public ChainId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(ChainId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 연쇄 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(ChainId other) { return Value == other.Value; }
        /// <summary>object가 같은 연쇄 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is ChainId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 연쇄 ID가 같으면 true다.</summary>
        public static bool operator ==(ChainId left, ChainId right) { return left.Equals(right); }
        /// <summary>두 연쇄 ID가 다르면 true다.</summary>
        public static bool operator !=(ChainId left, ChainId right) { return !left.Equals(right); }
        /// <summary>왼쪽 연쇄 ID가 더 작으면 true다.</summary>
        public static bool operator <(ChainId left, ChainId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 연쇄 ID가 더 크면 true다.</summary>
        public static bool operator >(ChainId left, ChainId right) { return left.Value > right.Value; }
    }

    /// <summary>타워/카드 프로그램이 한 번 활성화된 실행 묶음을 가리키는 ID다.</summary>
    public readonly struct ActivationId : IEquatable<ActivationId>, IComparable<ActivationId>
    {
        /// <summary>어떤 활성화도 가리키지 않는 특수값이다.</summary>
        public static readonly ActivationId Invalid = new ActivationId(-1);
        /// <summary>런타임 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 활성화 ID를 만든다.</summary>
        public ActivationId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(ActivationId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 활성화 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(ActivationId other) { return Value == other.Value; }
        /// <summary>object가 같은 활성화 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is ActivationId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 활성화 ID가 같으면 true다.</summary>
        public static bool operator ==(ActivationId left, ActivationId right) { return left.Equals(right); }
        /// <summary>두 활성화 ID가 다르면 true다.</summary>
        public static bool operator !=(ActivationId left, ActivationId right) { return !left.Equals(right); }
        /// <summary>왼쪽 활성화 ID가 더 작으면 true다.</summary>
        public static bool operator <(ActivationId left, ActivationId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 활성화 ID가 더 크면 true다.</summary>
        public static bool operator >(ActivationId left, ActivationId right) { return left.Value > right.Value; }
    }

    /// <summary>원본 적과 분열·복제된 모든 자손을 하나로 묶는 가계 ID다.</summary>
    public readonly struct LineageId : IEquatable<LineageId>, IComparable<LineageId>
    {
        /// <summary>어떤 가계도 가리키지 않는 특수값이다.</summary>
        public static readonly LineageId Invalid = new LineageId(-1);
        /// <summary>런타임 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 가계 ID를 만든다.</summary>
        public LineageId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(LineageId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 가계 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(LineageId other) { return Value == other.Value; }
        /// <summary>object가 같은 가계 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is LineageId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 가계 ID가 같으면 true다.</summary>
        public static bool operator ==(LineageId left, LineageId right) { return left.Equals(right); }
        /// <summary>두 가계 ID가 다르면 true다.</summary>
        public static bool operator !=(LineageId left, LineageId right) { return !left.Equals(right); }
        /// <summary>왼쪽 가계 ID가 더 작으면 true다.</summary>
        public static bool operator <(LineageId left, LineageId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 가계 ID가 더 크면 true다.</summary>
        public static bool operator >(LineageId left, LineageId right) { return left.Value > right.Value; }
    }

    /// <summary>런 데이터에 컴파일된 개별 웨이브를 가리키는 ID다.</summary>
    public readonly struct WaveId : IEquatable<WaveId>, IComparable<WaveId>
    {
        /// <summary>어떤 웨이브도 가리키지 않는 특수값이다.</summary>
        public static readonly WaveId Invalid = new WaveId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 웨이브 ID를 만든다.</summary>
        public WaveId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(WaveId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 웨이브 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(WaveId other) { return Value == other.Value; }
        /// <summary>object가 같은 웨이브 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is WaveId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 웨이브 ID가 같으면 true다.</summary>
        public static bool operator ==(WaveId left, WaveId right) { return left.Equals(right); }
        /// <summary>두 웨이브 ID가 다르면 true다.</summary>
        public static bool operator !=(WaveId left, WaveId right) { return !left.Equals(right); }
        /// <summary>왼쪽 웨이브 ID가 더 작으면 true다.</summary>
        public static bool operator <(WaveId left, WaveId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 웨이브 ID가 더 크면 true다.</summary>
        public static bool operator >(WaveId left, WaveId right) { return left.Value > right.Value; }
    }

    /// <summary>맵에 미리 정의된 고정 타워 건설 지점을 가리키는 ID다.</summary>
    public readonly struct BuildPointId : IEquatable<BuildPointId>, IComparable<BuildPointId>
    {
        /// <summary>어떤 건설 지점도 가리키지 않는 특수값이다.</summary>
        public static readonly BuildPointId Invalid = new BuildPointId(-1);
        /// <summary>컴파일된 정수 ID 값이다.</summary>
        public int Value { get; }
        /// <summary>0 이상의 실제 ID이면 true다.</summary>
        public bool IsValid { get { return Value >= 0; } }

        /// <summary>정수 값으로 건설 지점 ID를 만든다.</summary>
        public BuildPointId(int value) { Value = value; }
        /// <summary>안정 정렬을 위해 정수 ID 순서를 비교한다.</summary>
        public int CompareTo(BuildPointId other) { return Value.CompareTo(other.Value); }
        /// <summary>다른 건설 지점 ID와 값이 같은지 비교한다.</summary>
        public bool Equals(BuildPointId other) { return Value == other.Value; }
        /// <summary>object가 같은 건설 지점 ID인지 비교한다.</summary>
        public override bool Equals(object obj) { return obj is BuildPointId other && Equals(other); }
        /// <summary>내부 정수를 컬렉션용 해시 코드로 반환한다.</summary>
        public override int GetHashCode() { return Value; }
        /// <summary>유효하면 숫자, 아니면 “Invalid” 문자열을 반환한다.</summary>
        public override string ToString() { return IsValid ? Value.ToString() : "Invalid"; }
        /// <summary>두 건설 지점 ID가 같으면 true다.</summary>
        public static bool operator ==(BuildPointId left, BuildPointId right) { return left.Equals(right); }
        /// <summary>두 건설 지점 ID가 다르면 true다.</summary>
        public static bool operator !=(BuildPointId left, BuildPointId right) { return !left.Equals(right); }
        /// <summary>왼쪽 건설 지점 ID가 더 작으면 true다.</summary>
        public static bool operator <(BuildPointId left, BuildPointId right) { return left.Value < right.Value; }
        /// <summary>왼쪽 건설 지점 ID가 더 크면 true다.</summary>
        public static bool operator >(BuildPointId left, BuildPointId right) { return left.Value > right.Value; }
    }
}
