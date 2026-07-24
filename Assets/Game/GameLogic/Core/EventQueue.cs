using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// Fixed-capacity stable priority queue ordered by
    /// SimulationTick, EventPhase, then EnqueueSequence.
    /// </summary>
    /// <remarks>
    /// 카드 효과가 다른 효과를 즉시 함수 호출로 재귀 실행하지 않고 이 큐에 작업으로 등록된다.
    /// 이렇게 하면 긴 연쇄도 한곳에서 예산을 검사할 수 있고, 처리 순서도 플랫폼과 무관하게
    /// 고정할 수 있다. 내부 자료구조는 “가장 먼저 처리할 이벤트”를 루트에 두는 최소 힙이다.
    /// 배열 용량이 고정되어 있어 폭주 중에도 큐 메모리가 끝없이 늘어나지 않는다.
    /// </remarks>
    public sealed class EventQueue
    {
        private readonly GameEvent[] _heap;
        private int _count;
        private int _nextEventId;
        private ulong _nextSequence;
        private bool _identityExhausted;

        /// <summary>
        /// 지정한 최대 이벤트 수로 큐를 만들며 EventId와 등록 순번은 0부터 시작한다.
        /// </summary>
        public EventQueue(int capacity)
            : this(capacity, 0, 0UL)
        {
        }

        /// <summary>
        /// 큐 용량과 첫 EventId/등록 순번을 명시하여 큐를 만든다.
        /// 저장 상태 복원이나 결정성 테스트에서 연속된 ID를 이어 갈 때 사용할 수 있다.
        /// </summary>
        public EventQueue(int capacity, int firstEventId, ulong firstEnqueueSequence)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (firstEventId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(firstEventId));
            }

            _heap = new GameEvent[capacity];
            _nextEventId = firstEventId;
            _nextSequence = firstEnqueueSequence;
        }

        /// <summary>큐가 동시에 보유할 수 있는 최대 이벤트 수다.</summary>
        public int Capacity { get { return _heap.Length; } }
        /// <summary>현재 대기 중인 이벤트 수다.</summary>
        public int Count { get { return _count; } }
        /// <summary>현재 추가로 등록할 수 있는 물리적 빈 칸 수다.</summary>
        public int RemainingCapacity { get { return _heap.Length - _count; } }
        /// <summary>처리할 이벤트가 하나도 없으면 true다.</summary>
        public bool IsEmpty { get { return _count == 0; } }
        /// <summary>
        /// 빈 칸이 있고 EventId/등록 순번도 더 발급할 수 있으면 true다.
        /// </summary>
        public bool CanEnqueue
        {
            get { return _count < _heap.Length && !_identityExhausted; }
        }

        /// <summary>
        /// 이벤트 <paramref name="count"/>개를 ID까지 포함해 모두 등록할 여유가 있는지,
        /// 실제 상태를 바꾸지 않고 미리 확인한다.
        /// </summary>
        /// <remarks>
        /// 분열이나 범위 폭발처럼 여러 작업이 한 묶음인 경우 일부만 등록되면 게임 규칙이
        /// 깨질 수 있다. 그래서 효과를 적용하기 전에 이 메서드로 전체 묶음을 검사한다.
        /// </remarks>
        public bool CanEnqueueCount(int count)
        {
            if (count < 0 || count > _heap.Length - _count)
            {
                return false;
            }

            if (count == 0)
            {
                return true;
            }

            if (_identityExhausted)
            {
                return false;
            }

            int identityOffset = count - 1;
            return _nextEventId <= int.MaxValue - identityOffset &&
                   _nextSequence <= ulong.MaxValue - (ulong)identityOffset;
        }

        /// <summary>
        /// 이벤트에 고유 EventId와 등록 순번을 붙여 큐에 넣는다.
        /// 공간이나 ID가 부족하면 예외 없이 false를 반환한다.
        /// </summary>
        public bool TryEnqueue(in GameEvent gameEvent, out GameEvent scheduledEvent)
        {
            if (_count >= _heap.Length || _identityExhausted)
            {
                scheduledEvent = default(GameEvent);
                return false;
            }

            scheduledEvent = gameEvent.WithSchedule(
                new EventId(_nextEventId),
                _nextSequence);

            AdvanceIdentity();

            // 새 이벤트를 배열 끝에서 시작해 부모와 비교하며 위로 올린다.
            // Compare가 0 이상이면 부모가 같거나 더 먼저이므로 현재 위치에서 멈춘다.
            int childIndex = _count;
            _count++;
            while (childIndex > 0)
            {
                int parentIndex = (childIndex - 1) >> 1;
                if (Compare(in scheduledEvent, in _heap[parentIndex]) >= 0)
                {
                    break;
                }

                _heap[childIndex] = _heap[parentIndex];
                childIndex = parentIndex;
            }

            _heap[childIndex] = scheduledEvent;
            return true;
        }

        /// <summary>
        /// 이벤트를 반드시 등록하고, 등록된 EventId/순번을 포함한 값을 반환한다.
        /// 실패를 정상 흐름으로 처리해야 하는 게임 효과에서는 TryEnqueue를 사용해야 한다.
        /// </summary>
        public GameEvent Enqueue(in GameEvent gameEvent)
        {
            if (!TryEnqueue(in gameEvent, out GameEvent scheduledEvent))
            {
                if (_identityExhausted)
                {
                    throw new OverflowException("Event identity space has been exhausted.");
                }

                throw new InvalidOperationException("Event queue capacity has been reached.");
            }

            return scheduledEvent;
        }

        /// <summary>
        /// 다음에 처리될 이벤트를 제거하지 않고 확인한다. 비어 있으면 false다.
        /// </summary>
        public bool TryPeek(out GameEvent gameEvent)
        {
            if (_count == 0)
            {
                gameEvent = default(GameEvent);
                return false;
            }

            gameEvent = _heap[0];
            return true;
        }

        /// <summary>
        /// 다음 이벤트를 제거하지 않고 반환하며, 큐가 비어 있으면 예외를 던진다.
        /// </summary>
        public GameEvent Peek()
        {
            if (!TryPeek(out GameEvent gameEvent))
            {
                throw new InvalidOperationException("Event queue is empty.");
            }

            return gameEvent;
        }

        /// <summary>
        /// 가장 먼저 처리할 이벤트를 꺼낸다. 큐가 비어 있으면 false다.
        /// </summary>
        public bool TryDequeue(out GameEvent gameEvent)
        {
            if (_count == 0)
            {
                gameEvent = default(GameEvent);
                return false;
            }

            gameEvent = _heap[0];
            RemoveRoot();
            return true;
        }

        /// <summary>
        /// 지정한 틱까지 실행 시점이 도래한 이벤트만 꺼낸다.
        /// 미래 이벤트는 큐에 그대로 남기고 false를 반환한다.
        /// </summary>
        public bool TryDequeueDue(long inclusiveSimulationTick, out GameEvent gameEvent)
        {
            if (_count == 0 || _heap[0].SimulationTick > inclusiveSimulationTick)
            {
                gameEvent = default(GameEvent);
                return false;
            }

            gameEvent = _heap[0];
            RemoveRoot();
            return true;
        }

        /// <summary>
        /// 가장 먼저 처리할 이벤트를 반환하며, 큐가 비어 있으면 예외를 던진다.
        /// </summary>
        public GameEvent Dequeue()
        {
            if (!TryDequeue(out GameEvent gameEvent))
            {
                throw new InvalidOperationException("Event queue is empty.");
            }

            return gameEvent;
        }

        /// <summary>
        /// Removes queued events while retaining monotonic event identities.
        /// </summary>
        /// <remarks>
        /// 전투 중 큐를 비우더라도 이미 사용한 ID를 재사용하지 않는다.
        /// 로그와 부모-자식 이벤트 관계에서 같은 ID가 두 의미로 보이는 일을 막기 위해서다.
        /// </remarks>
        public void Clear()
        {
            Array.Clear(_heap, 0, _count);
            _count = 0;
        }

        /// <summary>
        /// Starts a new deterministic run and resets event identities.
        /// </summary>
        /// <remarks>새 시뮬레이션 런을 시작할 때만 ID 공간까지 되돌리는 초기화다.</remarks>
        public void Reset(int firstEventId = 0, ulong firstEnqueueSequence = 0UL)
        {
            if (firstEventId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(firstEventId));
            }

            Clear();
            _nextEventId = firstEventId;
            _nextSequence = firstEnqueueSequence;
            _identityExhausted = false;
        }

        /// <summary>
        /// 큐의 내부 상태와 대기 이벤트를 현재 배열 순서 그대로 상태 해시에 추가한다.
        /// </summary>
        /// <remarks>
        /// 다음에 발급할 ID까지 포함하므로, 현재 보이는 전투 상태가 같아도 앞으로의
        /// 이벤트 ID 흐름이 다른 두 시뮬레이션은 서로 다른 해시가 된다.
        /// </remarks>
        public void AppendStateHash(ref StableHashBuilder hash)
        {
            hash.Add(_count);
            hash.Add(_nextEventId);
            hash.Add(_nextSequence);
            hash.Add(_identityExhausted);
            for (int index = 0; index < _count; index++)
            {
                _heap[index].AppendHash(ref hash);
            }
        }

        private void RemoveRoot()
        {
            int lastIndex = _count - 1;
            GameEvent replacement = _heap[lastIndex];
            _heap[lastIndex] = default(GameEvent);
            _count = lastIndex;

            if (_count == 0)
            {
                return;
            }

            // 마지막 이벤트를 루트 후보로 가져온 뒤, 더 이른 자식과 교환하며 아래로 내린다.
            // 두 자식 중 Compare상 먼저인 쪽을 고르므로 최소 힙 규칙이 유지된다.
            int parentIndex = 0;
            while (true)
            {
                int leftChild = (parentIndex << 1) + 1;
                if (leftChild >= _count)
                {
                    break;
                }

                int rightChild = leftChild + 1;
                int selectedChild = leftChild;
                if (rightChild < _count &&
                    Compare(in _heap[rightChild], in _heap[leftChild]) < 0)
                {
                    selectedChild = rightChild;
                }

                if (Compare(in replacement, in _heap[selectedChild]) <= 0)
                {
                    break;
                }

                _heap[parentIndex] = _heap[selectedChild];
                parentIndex = selectedChild;
            }

            _heap[parentIndex] = replacement;
        }

        private void AdvanceIdentity()
        {
            // int/ulong 최대값을 넘겨 ID가 다시 작은 값으로 순환하면 안정 정렬과 추적이
            // 깨진다. 마지막 값을 발급한 뒤에는 큐를 “ID 소진” 상태로 잠근다.
            if (_nextEventId == int.MaxValue || _nextSequence == ulong.MaxValue)
            {
                _identityExhausted = true;
                return;
            }

            _nextEventId++;
            _nextSequence++;
        }

        private static int Compare(in GameEvent left, in GameEvent right)
        {
            // 1순위: 더 이른 시뮬레이션 틱.
            int tickComparison = left.SimulationTick.CompareTo(right.SimulationTick);
            if (tickComparison != 0)
            {
                return tickComparison;
            }

            // 2순위: 같은 틱 안의 고정 단계. 예를 들어 Damage는 Death보다 먼저다.
            int phaseComparison = ((int)left.Phase).CompareTo((int)right.Phase);
            if (phaseComparison != 0)
            {
                return phaseComparison;
            }

            // 3순위: 위 두 값도 같으면 먼저 등록된 이벤트가 먼저 실행된다.
            // 이 마지막 기준 덕분에 우선순위가 같은 이벤트의 순서도 항상 안정적이다.
            return left.EnqueueSequence.CompareTo(right.EnqueueSequence);
        }
    }
}
