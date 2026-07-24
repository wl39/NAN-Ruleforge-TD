using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// Integer-only helpers used by simulation value types.
    /// Division follows C# integer semantics and therefore rounds toward zero.
    /// </summary>
    /// <remarks>
    /// 이 클래스는 전투 수치 계산에서 부동소수점(float/double)을 사용하지 않도록 돕는
    /// 정수 전용 계산 도구 모음이다. 같은 입력과 같은 명령 순서라면 Editor, WebGL 등
    /// 실행 환경이 달라도 같은 결과를 얻는 것이 목적이다.
    /// </remarks>
    public static class DeterministicMath
    {
        /// <summary>
        /// 100%를 나타내는 basis point 기준값이다. 10,000 = 100%, 6,500 = 65%다.
        /// </summary>
        public const int BasisPointScale = 10_000;

        /// <summary>
        /// <paramref name="value"/>에 분수 numerator/denominator를 곱한다.
        /// 나눗셈의 소수 부분은 C# 정수 규칙에 따라 0 방향으로 버린다.
        /// </summary>
        /// <remarks>
        /// 먼저 값을 denominator로 나눈 뒤 나머지를 따로 계산하여,
        /// 단순히 value * numerator를 먼저 했을 때 생길 수 있는 중간 오버플로를 줄인다.
        /// 최종 결과가 long 범위를 벗어나면 checked 블록이 예외를 발생시켜 잘못된 값이
        /// 조용히 전투 상태에 섞이지 않게 한다.
        /// </remarks>
        public static long MultiplyDivide(long value, int numerator, int denominator)
        {
            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
            }

            // Splitting the value first avoids the common value * numerator
            // intermediate overflow while retaining exact round-toward-zero behavior.
            // 예: 1,000의 65%는 quotient와 remainder로 나눠 계산해 정확히 650이 된다.
            long quotient = value / denominator;
            long remainder = value % denominator;

            checked
            {
                return (quotient * numerator) + ((remainder * numerator) / denominator);
            }
        }

        /// <summary>
        /// 정수 값에 basis point 비율을 곱한다. 예를 들어 6,500은 원래 값의 65%다.
        /// </summary>
        public static long MultiplyBasisPoints(long value, int basisPoints)
        {
            return MultiplyDivide(value, basisPoints, BasisPointScale);
        }

        /// <summary>
        /// x²+y²을 제곱근 없이 계산한다. 계산 범위를 넘으면 <see cref="ulong.MaxValue"/>로 고정한다.
        /// </summary>
        /// <remarks>
        /// 거리의 대소 비교에는 실제 거리(제곱근)가 아니라 거리의 제곱만으로 충분하다.
        /// 제곱근과 실수를 피하면 결정성을 유지할 수 있고 계산도 더 저렴하다.
        /// 아주 큰 좌표가 들어와도 값이 되감기는 오버플로 대신 최대값으로 포화시킨다.
        /// </remarks>
        public static ulong SaturatingSquareSum(long x, long y)
        {
            ulong xMagnitude = AbsoluteAsUnsigned(x);
            ulong yMagnitude = AbsoluteAsUnsigned(y);
            ulong xSquared = SaturatingSquare(xMagnitude);
            ulong ySquared = SaturatingSquare(yMagnitude);

            if (ulong.MaxValue - xSquared < ySquared)
            {
                return ulong.MaxValue;
            }

            return xSquared + ySquared;
        }

        private static ulong AbsoluteAsUnsigned(long value)
        {
            if (value >= 0)
            {
                return (ulong)value;
            }

            // This form is valid for long.MinValue as well.
            // long.MinValue는 양수 long으로 표현할 수 없으므로 unsigned 형태로 안전하게 절댓값을 만든다.
            return (ulong)(-(value + 1L)) + 1UL;
        }

        private static ulong SaturatingSquare(ulong value)
        {
            if (value > uint.MaxValue)
            {
                return ulong.MaxValue;
            }

            return value * value;
        }
    }
}
