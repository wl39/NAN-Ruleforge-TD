using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// Allocation-free FNV-1a 64-bit state hash builder.
    /// Numeric values are encoded little-endian and strings as UTF-16 code units.
    /// </summary>
    /// <remarks>
    /// 상태 해시는 전체 전투 상태를 짧은 64비트 지문으로 요약한다. 같은 seed와 명령 로그를
    /// 두 번 재생했을 때 매 틱 해시가 같다면 결정성이 유지된 것이다. 이 값은 암호나 치트
    /// 방지용이 아니라 빠른 재현성 검사 용도다. Add 호출 순서도 해시에 포함되므로 모든
    /// 컬렉션은 안정적으로 정렬한 뒤 같은 필드 순서로 추가해야 한다.
    /// </remarks>
    public struct StableHashBuilder
    {
        /// <summary>FNV-1a 64비트 알고리즘의 표준 시작값이다.</summary>
        public const ulong OffsetBasis = 14_695_981_039_346_656_037UL;
        /// <summary>각 바이트를 섞을 때 사용하는 FNV-1a 64비트 소수다.</summary>
        public const ulong Prime = 1_099_511_628_211UL;

        private ulong _hash;
        private bool _initialized;

        /// <summary>현재까지 누적된 해시다. 아직 사용하지 않았다면 표준 시작값을 반환한다.</summary>
        public ulong Value
        {
            get { return _initialized ? _hash : OffsetBasis; }
        }

        /// <summary>표준 시작값 대신 명시한 seed에서 해시 누적을 시작한다.</summary>
        public StableHashBuilder(ulong seed)
        {
            _hash = seed;
            _initialized = true;
        }

        /// <summary>누적 내용을 버리고 표준 FNV 시작값으로 되돌린다.</summary>
        public void Reset()
        {
            _hash = OffsetBasis;
            _initialized = true;
        }

        /// <summary>누적 내용을 버리고 지정한 seed에서 다시 시작한다.</summary>
        public void Reset(ulong seed)
        {
            _hash = seed;
            _initialized = true;
        }

        /// <summary>바이트 하나를 FNV-1a 규칙으로 해시에 섞는다.</summary>
        public void Add(byte value)
        {
            EnsureInitialized();
            _hash = unchecked((_hash ^ value) * Prime);
        }

        /// <summary>false는 0, true는 1 바이트로 해시에 추가한다.</summary>
        public void Add(bool value)
        {
            Add(value ? (byte)1 : (byte)0);
        }

        /// <summary>부호 있는 16비트 정수를 원래 비트 그대로 추가한다.</summary>
        public void Add(short value)
        {
            Add(unchecked((ushort)value));
        }

        /// <summary>16비트 정수를 플랫폼과 무관한 little-endian 바이트 순서로 추가한다.</summary>
        public void Add(ushort value)
        {
            Add((byte)value);
            Add((byte)(value >> 8));
        }

        /// <summary>부호 있는 32비트 정수를 원래 비트 그대로 추가한다.</summary>
        public void Add(int value)
        {
            Add(unchecked((uint)value));
        }

        /// <summary>32비트 정수를 플랫폼과 무관한 little-endian 바이트 순서로 추가한다.</summary>
        public void Add(uint value)
        {
            Add((byte)value);
            Add((byte)(value >> 8));
            Add((byte)(value >> 16));
            Add((byte)(value >> 24));
        }

        /// <summary>부호 있는 64비트 정수를 원래 비트 그대로 추가한다.</summary>
        public void Add(long value)
        {
            Add(unchecked((ulong)value));
        }

        /// <summary>64비트 정수를 플랫폼과 무관한 little-endian 바이트 순서로 추가한다.</summary>
        public void Add(ulong value)
        {
            Add((byte)value);
            Add((byte)(value >> 8));
            Add((byte)(value >> 16));
            Add((byte)(value >> 24));
            Add((byte)(value >> 32));
            Add((byte)(value >> 40));
            Add((byte)(value >> 48));
            Add((byte)(value >> 56));
        }

        /// <summary>
        /// 문자열 길이와 각 UTF-16 코드 단위를 순서대로 추가한다.
        /// null은 길이 -1로 기록되어 빈 문자열과 구분된다.
        /// </summary>
        public void Add(string value)
        {
            if (value == null)
            {
                Add(-1);
                return;
            }

            Add(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                Add((ushort)value[index]);
            }
        }

        /// <summary>고정소수점 값의 원시 milli-unit 정수를 추가한다.</summary>
        public void Add(SimValue value)
        {
            Add(value.MilliUnits);
        }

        /// <summary>위치의 x, y를 순서대로 추가한다.</summary>
        public void Add(SimPosition value)
        {
            Add(value.X);
            Add(value.Y);
        }

        /// <summary>벡터의 x, y를 순서대로 추가한다.</summary>
        public void Add(SimVector value)
        {
            Add(value.X);
            Add(value.Y);
        }

        // 타입이 다른 ID는 모두 내부 int 값으로 기록한다. 호출 지점에서는 강한 타입을
        // 유지하므로 실수로 CardId 대신 EnemyId를 넘기는 문제를 컴파일러가 잡아 준다.
        /// <summary>엔티티 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(EntityId value) { Add(value.Value); }
        /// <summary>타워 인스턴스 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(TowerId value) { Add(value.Value); }
        /// <summary>타워 정의 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(TowerDefinitionId value) { Add(value.Value); }
        /// <summary>적 정의 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(EnemyDefinitionId value) { Add(value.Value); }
        /// <summary>카드 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(CardId value) { Add(value.Value); }
        /// <summary>상태 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(StatusId value) { Add(value.Value); }
        /// <summary>효과 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(EffectId value) { Add(value.Value); }
        /// <summary>이벤트 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(EventId value) { Add(value.Value); }
        /// <summary>연쇄 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(ChainId value) { Add(value.Value); }
        /// <summary>활성화 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(ActivationId value) { Add(value.Value); }
        /// <summary>적 가계 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(LineageId value) { Add(value.Value); }
        /// <summary>웨이브 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(WaveId value) { Add(value.Value); }
        /// <summary>건설 지점 ID의 내부 정수 값을 추가한다.</summary>
        public void Add(BuildPointId value) { Add(value.Value); }

        /// <summary>현재 최종 해시를 반환한다. 이후 Add를 계속 호출하는 것도 가능하다.</summary>
        public ulong Finish()
        {
            return Value;
        }

        /// <summary>문자열 하나의 안정적인 64비트 해시를 편리하게 계산한다.</summary>
        public static ulong HashString(string value)
        {
            StableHashBuilder builder = default(StableHashBuilder);
            builder.Add(value);
            return builder.Finish();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            // default(StableHashBuilder)로 만들어도 첫 Add에서 표준 시작값을 자동 적용한다.
            // 덕분에 호출자가 반드시 생성자를 호출해야 한다는 실수 여지가 없다.
            _hash = OffsetBasis;
            _initialized = true;
        }
    }
}
