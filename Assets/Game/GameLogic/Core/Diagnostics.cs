using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// 시뮬레이션이 거절하거나 제한한 작업 한 건을 설명하는 불변 진단 기록이다.
    /// </summary>
    /// <remarks>
    /// 진단은 게임 결과를 바꾸는 이벤트가 아니라, “어떤 카드 연쇄가 왜 잘렸는가”를
    /// 개발자와 디자이너가 추적하기 위한 기록이다. 문자열 대신 enum과 ID, 정수 상세값을
    /// 보관하므로 로그 생성 자체도 작고 결정적이다.
    /// </remarks>
    public readonly struct DiagnosticRecord
    {
        /// <summary>문제가 관찰된 시뮬레이션 틱이다. 30틱 설정에서는 30틱이 게임 시간 1초다.</summary>
        public long SimulationTick { get; }
        /// <summary>진단 버퍼에 기록된 전역 순번이다. 같은 틱의 기록 순서를 구분한다.</summary>
        public ulong Sequence { get; }
        /// <summary>정보, 경고, 오류 중 이 기록의 중요도다.</summary>
        public DiagnosticSeverity Severity { get; }
        /// <summary>예산 초과, 잘못된 이벤트 등 기계적으로 판별 가능한 원인 코드다.</summary>
        public DiagnosticCode Code { get; }
        /// <summary>문제를 일으킨 이벤트 종류다.</summary>
        public EventType EventType { get; }
        /// <summary>연쇄작용 전체를 묶는 최초 행동의 ID다.</summary>
        public ChainId RootChainId { get; }
        /// <summary>원인이 된 타워의 런타임 ID다. 해당되지 않으면 Invalid일 수 있다.</summary>
        public TowerId SourceTowerId { get; }
        /// <summary>원인이 된 카드 정의 ID다. 해당되지 않으면 Invalid일 수 있다.</summary>
        public CardId SourceCardId { get; }
        /// <summary>영향을 받거나 작업 대상이었던 적/탄환의 엔티티 ID다.</summary>
        public EntityId SubjectEntityId { get; }
        /// <summary>원인에 따라 추가로 기록하는 정수 값이다. 해석은 <see cref="Code"/>에 달려 있다.</summary>
        public int DetailValue { get; }

        /// <summary>
        /// 진단 정보를 만든다. Sequence는 보통 버퍼에 들어갈 때 자동 부여되므로 기본값 0을 사용한다.
        /// </summary>
        public DiagnosticRecord(
            long simulationTick,
            DiagnosticSeverity severity,
            DiagnosticCode code,
            EventType eventType,
            ChainId rootChainId,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId subjectEntityId,
            int detailValue = 0,
            ulong sequence = 0UL)
        {
            SimulationTick = simulationTick;
            Sequence = sequence;
            Severity = severity;
            Code = code;
            EventType = eventType;
            RootChainId = rootChainId;
            SourceTowerId = sourceTowerId;
            SourceCardId = sourceCardId;
            SubjectEntityId = subjectEntityId;
            DetailValue = detailValue;
        }

        /// <summary>
        /// 나머지 정보는 유지한 채 기록 순번만 부여한 새 값을 반환한다.
        /// </summary>
        /// <remarks>
        /// readonly struct는 생성 뒤 내부 값을 바꿀 수 없으므로, 변경 대신 복사본을 만든다.
        /// </remarks>
        public DiagnosticRecord WithSequence(ulong sequence)
        {
            return new DiagnosticRecord(
                SimulationTick,
                Severity,
                Code,
                EventType,
                RootChainId,
                SourceTowerId,
                SourceCardId,
                SubjectEntityId,
                DetailValue,
                sequence);
        }
    }

    /// <summary>
    /// Fixed-capacity chronological log. New entries overwrite the oldest.
    /// </summary>
    /// <remarks>
    /// 고정 크기 원형 버퍼를 사용하므로 사기 조합이 수많은 경고를 만들어도 메모리가
    /// 무한히 증가하지 않는다. 가득 찬 뒤 새 기록이 들어오면 가장 오래된 기록부터
    /// 덮어쓰며, 인덱스로 읽을 때는 언제나 “현재 남아 있는 기록 중 오래된 것부터” 보인다.
    /// </remarks>
    public sealed class DiagnosticRingBuffer
    {
        private readonly DiagnosticRecord[] _records;
        private int _start;
        private int _count;
        private ulong _totalWritten;

        /// <summary>보관할 최대 기록 수를 정해 빈 진단 버퍼를 만든다.</summary>
        public DiagnosticRingBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _records = new DiagnosticRecord[capacity];
        }

        /// <summary>동시에 보관 가능한 최대 기록 수다.</summary>
        public int Capacity { get { return _records.Length; } }
        /// <summary>현재 버퍼에 남아 있는 기록 수다.</summary>
        public int Count { get { return _count; } }
        /// <summary>덮어써진 기록까지 포함하여 생성 이후 기록된 총 횟수다.</summary>
        public ulong TotalWritten { get { return _totalWritten; } }

        /// <summary>
        /// 현재 보관된 기록을 시간순 인덱스로 읽는다. 0은 현재 남은 기록 중 가장 오래된 항목이다.
        /// </summary>
        public DiagnosticRecord this[int chronologicalIndex]
        {
            get
            {
                if (chronologicalIndex < 0 || chronologicalIndex >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(chronologicalIndex));
                }

                int index = (_start + chronologicalIndex) % _records.Length;
                return _records[index];
            }
        }

        /// <summary>
        /// 진단 한 건을 추가하고 단조 증가하는 Sequence를 부여한다.
        /// 버퍼가 가득 찼으면 가장 오래된 항목을 덮어쓴다.
        /// </summary>
        public void Add(in DiagnosticRecord record)
        {
            DiagnosticRecord sequenced = record.WithSequence(_totalWritten);

            if (_count < _records.Length)
            {
                int index = (_start + _count) % _records.Length;
                _records[index] = sequenced;
                _count++;
            }
            else
            {
                // _start가 가장 오래된 칸을 가리킨다. 그 칸을 새 값으로 교체한 뒤
                // 시작점을 한 칸 옮기면 외부에서는 계속 시간순으로 읽을 수 있다.
                _records[_start] = sequenced;
                _start = (_start + 1) % _records.Length;
            }

            // 진단은 시뮬레이션 안전장치이므로 극단적으로 ulong 전체를 소진해도
            // 전투를 멈추지 않고 0으로 순환하도록 의도적으로 unchecked를 사용한다.
            _totalWritten = unchecked(_totalWritten + 1UL);
        }

        /// <summary>
        /// 시간순 인덱스의 기록을 읽는다. 범위를 벗어나면 예외 대신 false를 반환한다.
        /// </summary>
        public bool TryGet(int chronologicalIndex, out DiagnosticRecord record)
        {
            if (chronologicalIndex < 0 || chronologicalIndex >= _count)
            {
                record = default(DiagnosticRecord);
                return false;
            }

            record = this[chronologicalIndex];
            return true;
        }

        /// <summary>
        /// 현재 남아 있는 모든 진단을 오래된 순서대로 외부 배열에 복사하고 복사 개수를 반환한다.
        /// </summary>
        public int CopyTo(DiagnosticRecord[] destination, int destinationIndex)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destinationIndex < 0 || destinationIndex > destination.Length - _count)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            }

            for (int index = 0; index < _count; index++)
            {
                destination[destinationIndex + index] = this[index];
            }

            return _count;
        }

        /// <summary>
        /// 모든 기록과 순번을 지워 새 런처럼 초기화한다.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_records, 0, _records.Length);
            _start = 0;
            _count = 0;
            _totalWritten = 0UL;
        }
    }
}
