using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// 결정적 시뮬레이션 공간의 2차원 위치다.
    /// </summary>
    /// <remarks>
    /// Unity의 Vector2나 Transform 위치가 아니라 <see cref="SimValue"/> 두 개로 좌표를 보관한다.
    /// 따라서 렌더링 프레임률이나 플랫폼 부동소수점 차이가 전투 판정에 들어오지 않는다.
    /// 화면 계층은 이 값을 읽어 스프라이트 위치로 변환할 뿐, 원본 상태를 소유하지 않는다.
    /// </remarks>
    public readonly struct SimPosition : IEquatable<SimPosition>
    {
        /// <summary>(0, 0) 위치다.</summary>
        public static readonly SimPosition Origin = new SimPosition(SimValue.Zero, SimValue.Zero);

        /// <summary>가로 좌표다. 내부 정밀도는 1/1000 게임 단위다.</summary>
        public SimValue X { get; }
        /// <summary>세로 좌표다. 내부 정밀도는 1/1000 게임 단위다.</summary>
        public SimValue Y { get; }

        /// <summary>두 고정소수점 좌표로 위치를 만든다.</summary>
        public SimPosition(SimValue x, SimValue y)
        {
            X = x;
            Y = y;
        }

        /// <summary>1/1000 단위 정수 좌표로 위치를 만든다. 1,000은 게임 공간 1칸이다.</summary>
        public SimPosition(long xMilliUnits, long yMilliUnits)
            : this(
                SimValue.FromMilliUnits(xMilliUnits),
                SimValue.FromMilliUnits(yMilliUnits))
        {
        }

        /// <summary>1/1000 단위 정수 x, y로 위치를 만드는 이름 있는 팩토리 메서드다.</summary>
        public static SimPosition FromMilliUnits(long x, long y)
        {
            return new SimPosition(x, y);
        }

        /// <summary>
        /// 다른 위치까지 거리의 제곱을 원시 milli-unit² 단위로 반환한다.
        /// </summary>
        /// <remarks>
        /// 사거리 비교는 제곱근 없이 “거리 제곱 ≤ 사거리 제곱”으로 할 수 있다.
        /// 극단적인 좌표 차이는 되감기 대신 ulong 최대값으로 포화된다.
        /// </remarks>
        public ulong DistanceSquaredRaw(SimPosition other)
        {
            long deltaX = SubtractForDistance(X.MilliUnits, other.X.MilliUnits);
            long deltaY = SubtractForDistance(Y.MilliUnits, other.Y.MilliUnits);
            return DeterministicMath.SaturatingSquareSum(deltaX, deltaY);
        }

        /// <summary>x와 y가 모두 같은지 비교한다.</summary>
        public bool Equals(SimPosition other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <summary>object가 같은 위치 값을 담고 있는지 비교한다.</summary>
        public override bool Equals(object obj)
        {
            return obj is SimPosition other && Equals(other);
        }

        /// <summary>일반 C# 컬렉션에서 사용할 위치 해시 코드를 반환한다.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        /// <summary>디버깅을 위해 “(x, y)” 형태로 표시한다.</summary>
        public override string ToString()
        {
            return "(" + X + ", " + Y + ")";
        }

        /// <summary>위치에 이동 벡터를 더해 새 위치를 만든다.</summary>
        public static SimPosition operator +(SimPosition position, SimVector offset)
        {
            return new SimPosition(position.X + offset.X, position.Y + offset.Y);
        }

        /// <summary>위치에서 이동 벡터를 빼 새 위치를 만든다.</summary>
        public static SimPosition operator -(SimPosition position, SimVector offset)
        {
            return new SimPosition(position.X - offset.X, position.Y - offset.Y);
        }

        /// <summary>두 위치의 차이를 right에서 left로 향하는 벡터로 반환한다.</summary>
        public static SimVector operator -(SimPosition left, SimPosition right)
        {
            return new SimVector(left.X - right.X, left.Y - right.Y);
        }

        /// <summary>두 위치가 같으면 true다.</summary>
        public static bool operator ==(SimPosition left, SimPosition right)
        {
            return left.Equals(right);
        }

        /// <summary>두 위치가 다르면 true다.</summary>
        public static bool operator !=(SimPosition left, SimPosition right)
        {
            return !left.Equals(right);
        }

        private static long SubtractForDistance(long left, long right)
        {
            // 좌표 차이가 long 범위를 넘을 때 반대 부호로 되감기지 않도록
            // 가장 가까운 극값으로 고정한다. 이어지는 제곱 합 계산도 포화 방식이다.
            if (right > 0L && left < long.MinValue + right)
            {
                return long.MinValue;
            }

            if (right < 0L && left > long.MaxValue + right)
            {
                return long.MaxValue;
            }

            return left - right;
        }
    }

    /// <summary>
    /// 결정적 시뮬레이션 공간의 방향 또는 이동량을 나타내는 2차원 벡터다.
    /// </summary>
    /// <remarks>
    /// 위치와 달리 “어디에 있다”가 아니라 “얼마나 어느 방향으로 움직인다”를 뜻한다.
    /// 정규화된 실수 방향 대신 고정소수점 성분을 그대로 유지한다.
    /// </remarks>
    public readonly struct SimVector : IEquatable<SimVector>
    {
        /// <summary>이동량이 없는 (0, 0) 벡터다.</summary>
        public static readonly SimVector Zero = new SimVector(SimValue.Zero, SimValue.Zero);

        /// <summary>가로 방향 이동량이다.</summary>
        public SimValue X { get; }
        /// <summary>세로 방향 이동량이다.</summary>
        public SimValue Y { get; }

        /// <summary>두 고정소수점 성분으로 벡터를 만든다.</summary>
        public SimVector(SimValue x, SimValue y)
        {
            X = x;
            Y = y;
        }

        /// <summary>1/1000 단위 정수 성분으로 벡터를 만든다.</summary>
        public SimVector(long xMilliUnits, long yMilliUnits)
            : this(
                SimValue.FromMilliUnits(xMilliUnits),
                SimValue.FromMilliUnits(yMilliUnits))
        {
        }

        /// <summary>1/1000 단위 정수 x, y로 벡터를 만든다.</summary>
        public static SimVector FromMilliUnits(long x, long y)
        {
            return new SimVector(x, y);
        }

        /// <summary>
        /// 벡터 길이의 제곱을 원시 milli-unit² 단위로 반환한다.
        /// 길이 비교에 제곱근이 필요 없도록 제공한다.
        /// </summary>
        public ulong MagnitudeSquaredRaw
        {
            get
            {
                return DeterministicMath.SaturatingSquareSum(
                    X.MilliUnits,
                    Y.MilliUnits);
            }
        }

        /// <summary>두 성분에 같은 basis point 비율을 적용한 새 벡터를 반환한다.</summary>
        public SimVector MultiplyBasisPoints(int basisPoints)
        {
            return new SimVector(
                X.MultiplyBasisPoints(basisPoints),
                Y.MultiplyBasisPoints(basisPoints));
        }

        /// <summary>x와 y 성분이 모두 같은지 비교한다.</summary>
        public bool Equals(SimVector other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <summary>object가 같은 벡터 값을 담고 있는지 비교한다.</summary>
        public override bool Equals(object obj)
        {
            return obj is SimVector other && Equals(other);
        }

        /// <summary>일반 C# 컬렉션에서 사용할 벡터 해시 코드를 반환한다.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        /// <summary>디버깅을 위해 “(x, y)” 형태로 표시한다.</summary>
        public override string ToString()
        {
            return "(" + X + ", " + Y + ")";
        }

        /// <summary>두 이동 벡터를 성분별로 더한다.</summary>
        public static SimVector operator +(SimVector left, SimVector right)
        {
            return new SimVector(left.X + right.X, left.Y + right.Y);
        }

        /// <summary>오른쪽 이동 벡터를 왼쪽 벡터에서 성분별로 뺀다.</summary>
        public static SimVector operator -(SimVector left, SimVector right)
        {
            return new SimVector(left.X - right.X, left.Y - right.Y);
        }

        /// <summary>방향을 정확히 반대로 뒤집는다.</summary>
        public static SimVector operator -(SimVector value)
        {
            return new SimVector(-value.X, -value.Y);
        }

        /// <summary>두 성분을 같은 정수 배율로 확대한다.</summary>
        public static SimVector operator *(SimVector value, int multiplier)
        {
            return new SimVector(value.X * multiplier, value.Y * multiplier);
        }

        /// <summary>두 성분을 같은 정수로 나누며 소수 부분은 0 방향으로 버린다.</summary>
        public static SimVector operator /(SimVector value, int divisor)
        {
            return new SimVector(value.X / divisor, value.Y / divisor);
        }

        /// <summary>두 벡터가 같으면 true다.</summary>
        public static bool operator ==(SimVector left, SimVector right)
        {
            return left.Equals(right);
        }

        /// <summary>두 벡터가 다르면 true다.</summary>
        public static bool operator !=(SimVector left, SimVector right)
        {
            return !left.Equals(right);
        }
    }
}
