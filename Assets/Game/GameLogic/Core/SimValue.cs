using System;
using System.Globalization;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// Deterministic fixed-point value with 1/1000 unit precision.
    /// </summary>
    /// <remarks>
    /// 실수 1.234를 내부 정수 1,234로 저장하는 고정소수점 값이다.
    /// Unity의 float는 플랫폼과 연산 순서에 따라 마지막 소수점이 달라질 수 있으므로
    /// 전투 위치, 체력, 피해처럼 재현성이 중요한 값은 이 형식을 사용한다.
    /// readonly struct라서 기존 값을 수정하지 않고 계산 결과로 새 값을 만든다.
    /// </remarks>
    public readonly struct SimValue : IEquatable<SimValue>, IComparable<SimValue>
    {
        /// <summary>게임 단위 1을 표현하는 내부 milli-unit 수다. 1게임 단위 = 1,000이다.</summary>
        public const long MilliUnitsPerUnit = 1_000L;

        /// <summary>0을 나타내는 값이다.</summary>
        public static readonly SimValue Zero = new SimValue(0L);
        /// <summary>게임 단위 1.000을 나타내는 값이다.</summary>
        public static readonly SimValue One = new SimValue(MilliUnitsPerUnit);
        /// <summary>내부 long으로 표현 가능한 가장 작은 값이다.</summary>
        public static readonly SimValue MinValue = new SimValue(long.MinValue);
        /// <summary>내부 long으로 표현 가능한 가장 큰 값이다.</summary>
        public static readonly SimValue MaxValue = new SimValue(long.MaxValue);

        /// <summary>
        /// 1/1000 단위의 실제 저장 정수다. 예를 들어 2.5 게임 단위는 2,500이다.
        /// </summary>
        public long MilliUnits { get; }

        private SimValue(long milliUnits)
        {
            MilliUnits = milliUnits;
        }

        /// <summary>이미 1/1000 단위로 환산된 정수에서 값을 만든다.</summary>
        public static SimValue FromMilliUnits(long milliUnits)
        {
            return new SimValue(milliUnits);
        }

        /// <summary>소수 부분이 없는 게임 단위에서 값을 만든다. 2를 넣으면 내부 값은 2,000이다.</summary>
        public static SimValue FromWholeUnits(long wholeUnits)
        {
            checked
            {
                return new SimValue(wholeUnits * MilliUnitsPerUnit);
            }
        }

        /// <summary>게임 단위 1에 분수 numerator/denominator를 적용한 값을 만든다.</summary>
        /// <remarks>예를 들어 FromRatio(1, 2)는 0.500을 만든다.</remarks>
        public static SimValue FromRatio(int numerator, int denominator)
        {
            return new SimValue(
                DeterministicMath.MultiplyDivide(MilliUnitsPerUnit, numerator, denominator));
        }

        /// <summary>
        /// basis point 비율을 곱한 새 값을 만든다. 10,000은 100%, 6,500은 65%다.
        /// </summary>
        public SimValue MultiplyBasisPoints(int basisPoints)
        {
            return new SimValue(
                DeterministicMath.MultiplyBasisPoints(MilliUnits, basisPoints));
        }

        /// <summary>분수 numerator/denominator를 곱한 새 값을 만든다.</summary>
        public SimValue MultiplyRatio(int numerator, int denominator)
        {
            return new SimValue(
                DeterministicMath.MultiplyDivide(MilliUnits, numerator, denominator));
        }

        /// <summary>정수로 나눈 새 값을 만들며 남는 1/1000 미만 부분은 0 방향으로 버린다.</summary>
        public SimValue Divide(int divisor)
        {
            if (divisor == 0)
            {
                throw new DivideByZeroException();
            }

            return new SimValue(MilliUnits / divisor);
        }

        /// <summary>두 값 중 작은 값을 반환한다.</summary>
        public static SimValue Min(SimValue left, SimValue right)
        {
            return left.MilliUnits <= right.MilliUnits ? left : right;
        }

        /// <summary>두 값 중 큰 값을 반환한다.</summary>
        public static SimValue Max(SimValue left, SimValue right)
        {
            return left.MilliUnits >= right.MilliUnits ? left : right;
        }

        /// <summary>값을 minimum 이상 maximum 이하 범위로 제한한다.</summary>
        public static SimValue Clamp(SimValue value, SimValue minimum, SimValue maximum)
        {
            if (minimum > maximum)
            {
                throw new ArgumentException("Minimum cannot be greater than maximum.");
            }

            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }

        /// <summary>부호를 제거한 절댓값을 반환한다.</summary>
        /// <remarks>long.MinValue는 대응하는 양수가 없으므로 그 경우 예외를 던진다.</remarks>
        public static SimValue Abs(SimValue value)
        {
            if (value.MilliUnits == long.MinValue)
            {
                throw new OverflowException("The absolute value cannot be represented.");
            }

            return value.MilliUnits < 0L
                ? new SimValue(-value.MilliUnits)
                : value;
        }

        /// <summary>정렬을 위해 다른 값보다 작음, 같음, 큼을 비교한다.</summary>
        public int CompareTo(SimValue other)
        {
            return MilliUnits.CompareTo(other.MilliUnits);
        }

        /// <summary>내부 milli-unit 정수가 정확히 같은지 비교한다.</summary>
        public bool Equals(SimValue other)
        {
            return MilliUnits == other.MilliUnits;
        }

        /// <summary>object가 같은 SimValue를 담고 있는지 비교한다.</summary>
        public override bool Equals(object obj)
        {
            return obj is SimValue other && Equals(other);
        }

        /// <summary>일반 C# 컬렉션에서 사용할 해시 코드를 반환한다.</summary>
        public override int GetHashCode()
        {
            return MilliUnits.GetHashCode();
        }

        /// <summary>
        /// 문화권 설정과 무관하게 항상 소수점 세 자리 문자열로 표시한다.
        /// 예: 내부 값 1,250은 “1.250”이다.
        /// </summary>
        public override string ToString()
        {
            long whole = MilliUnits / MilliUnitsPerUnit;
            long fractional = MilliUnits % MilliUnitsPerUnit;
            if (fractional < 0L)
            {
                fractional = -fractional;
            }

            string prefix = MilliUnits < 0L && whole == 0L ? "-" : string.Empty;
            return prefix +
                   whole.ToString(CultureInfo.InvariantCulture) +
                   "." +
                   fractional.ToString("D3", CultureInfo.InvariantCulture);
        }

        /// <summary>두 값을 더한다. 표현 범위를 넘으면 예외를 던진다.</summary>
        public static SimValue operator +(SimValue left, SimValue right)
        {
            checked
            {
                return new SimValue(left.MilliUnits + right.MilliUnits);
            }
        }

        /// <summary>왼쪽 값에서 오른쪽 값을 뺀다. 표현 범위를 넘으면 예외를 던진다.</summary>
        public static SimValue operator -(SimValue left, SimValue right)
        {
            checked
            {
                return new SimValue(left.MilliUnits - right.MilliUnits);
            }
        }

        /// <summary>값의 부호를 뒤집는다. 표현 범위를 넘으면 예외를 던진다.</summary>
        public static SimValue operator -(SimValue value)
        {
            checked
            {
                return new SimValue(-value.MilliUnits);
            }
        }

        /// <summary>값에 정수 배율을 곱한다. 표현 범위를 넘으면 예외를 던진다.</summary>
        public static SimValue operator *(SimValue value, int multiplier)
        {
            checked
            {
                return new SimValue(value.MilliUnits * multiplier);
            }
        }

        /// <summary>정수 배율을 값에 곱한다. 피연산자 순서만 다른 편의 연산자다.</summary>
        public static SimValue operator *(int multiplier, SimValue value)
        {
            return value * multiplier;
        }

        /// <summary>값을 정수로 나눈다.</summary>
        public static SimValue operator /(SimValue value, int divisor)
        {
            return value.Divide(divisor);
        }

        /// <summary>두 값이 정확히 같으면 true다.</summary>
        public static bool operator ==(SimValue left, SimValue right)
        {
            return left.Equals(right);
        }

        /// <summary>두 값이 다르면 true다.</summary>
        public static bool operator !=(SimValue left, SimValue right)
        {
            return !left.Equals(right);
        }

        /// <summary>왼쪽 값이 더 작으면 true다.</summary>
        public static bool operator <(SimValue left, SimValue right)
        {
            return left.MilliUnits < right.MilliUnits;
        }

        /// <summary>왼쪽 값이 더 크면 true다.</summary>
        public static bool operator >(SimValue left, SimValue right)
        {
            return left.MilliUnits > right.MilliUnits;
        }

        /// <summary>왼쪽 값이 작거나 같으면 true다.</summary>
        public static bool operator <=(SimValue left, SimValue right)
        {
            return left.MilliUnits <= right.MilliUnits;
        }

        /// <summary>왼쪽 값이 크거나 같으면 true다.</summary>
        public static bool operator >=(SimValue left, SimValue right)
        {
            return left.MilliUnits >= right.MilliUnits;
        }
    }
}
