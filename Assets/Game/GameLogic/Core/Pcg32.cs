using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// PCG-XSH-RR 32-bit generator with explicit seed and stream selection.
    /// </summary>
    /// <remarks>
    /// “랜덤처럼 보이지만 같은 seed에서는 항상 같은 순서”를 만드는 의사 난수 생성기다.
    /// System.Random이나 UnityEngine.Random에 의존하지 않으므로 Editor와 WebGL 사이의
    /// 재현성을 직접 통제할 수 있다. struct이므로 메서드를 호출할 때 내부 State가 바뀐다.
    /// 복사본을 만들어 호출하면 원본과 별도의 난수 진행 상태가 생긴다는 점에 유의한다.
    /// </remarks>
    public struct Pcg32
    {
        /// <summary>stream을 따로 지정하지 않을 때 사용하는 기본 스트림 번호다.</summary>
        public const ulong DefaultStream = 54UL;

        private const ulong Multiplier = 6_364_136_223_846_793_005UL;

        private ulong _state;
        private ulong _increment;

        /// <summary>다음 난수를 결정하는 현재 내부 상태다. 저장/해시/복원에 포함한다.</summary>
        public ulong State { get { return _state; } }
        /// <summary>독립적인 난수 열을 구분하는 홀수 증가값이다.</summary>
        public ulong StreamIncrement { get { return _increment; } }

        /// <summary>seed와 기본 스트림으로 결정적 난수 생성기를 초기화한다.</summary>
        public Pcg32(ulong seed)
            : this(seed, DefaultStream)
        {
        }

        /// <summary>seed와 별도의 stream 번호로 결정적 난수 생성기를 초기화한다.</summary>
        /// <remarks>
        /// 같은 seed라도 stream이 다르면 다른 난수 열을 만든다. 전투, 웨이브, 드래프트를
        /// 서로 다른 스트림으로 나누면 한 영역의 난수 호출 추가가 다른 영역 결과를 흔들지 않는다.
        /// </remarks>
        public Pcg32(ulong seed, ulong stream)
        {
            _state = 0UL;
            _increment = unchecked((stream << 1) | 1UL);
            NextUInt();
            _state = unchecked(_state + seed);
            NextUInt();
        }

        private Pcg32(ulong state, ulong increment, bool restoreState)
        {
            _state = state;
            _increment = increment | 1UL;
        }

        /// <summary>
        /// 저장해 둔 내부 state와 stream 증가값으로 정확히 같은 다음 난수 위치를 복원한다.
        /// </summary>
        public static Pcg32 Restore(ulong state, ulong streamIncrement)
        {
            if ((streamIncrement & 1UL) == 0UL)
            {
                throw new ArgumentException("A PCG stream increment must be odd.", nameof(streamIncrement));
            }

            return new Pcg32(state, streamIncrement, true);
        }

        /// <summary>
        /// 하나의 런 seed에서 도메인 번호별로 독립된 난수 생성기를 파생한다.
        /// </summary>
        /// <remarks>
        /// 예를 들어 domain 1은 전투, 2는 웨이브, 3은 드래프트처럼 분리할 수 있다.
        /// SplitMix64는 가까운 도메인 숫자도 충분히 다른 seed/stream으로 섞어 준다.
        /// </remarks>
        public static Pcg32 ForDomain(ulong rootSeed, ulong domain)
        {
            ulong mixer = unchecked(rootSeed + 0x9E3779B97F4A7C15UL + domain);
            ulong seed = SplitMix64(ref mixer);
            ulong stream = SplitMix64(ref mixer);
            return new Pcg32(seed, stream);
        }

        /// <summary>
        /// 전체 uint 범위에서 다음 32비트 난수를 반환하고 내부 상태를 한 단계 전진시킨다.
        /// </summary>
        public uint NextUInt()
        {
            ulong previousState = _state;
            _state = unchecked((previousState * Multiplier) + _increment);

            uint xorShifted = (uint)(((previousState >> 18) ^ previousState) >> 27);
            int rotation = (int)(previousState >> 59);
            return (xorShifted >> rotation) |
                   (xorShifted << ((-rotation) & 31));
        }

        /// <summary>0 이상 <paramref name="exclusiveMax"/> 미만에서 균등한 난수를 반환한다.</summary>
        /// <remarks>
        /// 단순 나머지 연산만 쓰면 uint 범위가 exclusiveMax로 정확히 나누어지지 않을 때
        /// 일부 값이 조금 더 자주 나온다. threshold보다 작은 후보를 버리는 rejection
        /// sampling으로 그 편향을 제거한다.
        /// </remarks>
        public uint NextUInt(uint exclusiveMax)
        {
            if (exclusiveMax == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            }

            uint threshold = unchecked(0U - exclusiveMax) % exclusiveMax;
            while (true)
            {
                uint candidate = NextUInt();
                if (candidate >= threshold)
                {
                    return candidate % exclusiveMax;
                }
            }
        }

        /// <summary>0 이상 <paramref name="exclusiveMax"/> 미만의 정수 난수를 반환한다.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            }

            return (int)NextUInt((uint)exclusiveMax);
        }

        /// <summary>
        /// <paramref name="inclusiveMin"/> 이상 <paramref name="exclusiveMax"/> 미만의 정수 난수를 반환한다.
        /// </summary>
        public int NextInt(int inclusiveMin, int exclusiveMax)
        {
            long width = (long)exclusiveMax - inclusiveMin;
            if (width <= 0L || width > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            }

            return (int)(inclusiveMin + (long)NextUInt((uint)width));
        }

        /// <summary>다음 난수의 최하위 비트로 true 또는 false를 반환한다.</summary>
        public bool NextBool()
        {
            return (NextUInt() & 1U) != 0U;
        }

        /// <summary>
        /// 확률 판정에 사용할 0~9,999의 basis point 롤을 반환한다.
        /// 예를 들어 결과가 1,500 미만이면 15% 확률로 판정할 수 있다.
        /// </summary>
        public int NextBasisPoints()
        {
            return (int)NextUInt(DeterministicMath.BasisPointScale);
        }

        /// <summary>배열 전체를 결정적인 Fisher-Yates 방식으로 섞는다.</summary>
        public void Shuffle<T>(T[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            Shuffle(values, values.Length);
        }

        /// <summary>배열의 앞 <paramref name="count"/>개 원소만 결정적으로 섞는다.</summary>
        public void Shuffle<T>(T[] values, int count)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (count < 0 || count > values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            for (int index = count - 1; index > 0; index--)
            {
                // 아직 확정되지 않은 0..index 구간에서 하나를 뽑아 현재 끝 칸과 교환한다.
                int swapIndex = NextInt(index + 1);
                T temporary = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = temporary;
            }
        }

        /// <summary>
        /// 난수를 실제로 하나씩 만들지 않고 내부 상태를 <paramref name="delta"/>회 앞당긴다.
        /// </summary>
        /// <remarks>
        /// 선형 합동 생성기의 거듭제곱 합성을 이용하므로 매우 큰 이동도 delta 횟수만큼
        /// 반복하지 않고 로그 시간에 처리한다. 난수 스트림 분할이나 빠른 재생에 유용하다.
        /// </remarks>
        public void Advance(ulong delta)
        {
            ulong currentMultiplier = Multiplier;
            ulong currentIncrement = _increment;
            ulong accumulatedMultiplier = 1UL;
            ulong accumulatedIncrement = 0UL;

            while (delta > 0UL)
            {
                if ((delta & 1UL) != 0UL)
                {
                    accumulatedMultiplier = unchecked(accumulatedMultiplier * currentMultiplier);
                    accumulatedIncrement = unchecked(
                        (accumulatedIncrement * currentMultiplier) + currentIncrement);
                }

                currentIncrement = unchecked((currentMultiplier + 1UL) * currentIncrement);
                currentMultiplier = unchecked(currentMultiplier * currentMultiplier);
                delta >>= 1;
            }

            _state = unchecked(
                (accumulatedMultiplier * _state) + accumulatedIncrement);
        }

        private static ulong SplitMix64(ref ulong state)
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            ulong value = state;
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
            return value ^ (value >> 31);
        }
    }
}
