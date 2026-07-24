using System;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 여러 경유점을 이은 고정 적 이동 경로를 "시작점부터의 거리 하나"로 다룬다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 적의 이동 원본을 2차원 Transform으로 저장하지 않고 PathProgressMilli라는
    /// 단일 정수로 저장한다. 그래서 밀치기, 공포, 역행, 순간이동을 모두 진행도
    /// 증감이라는 같은 규칙으로 처리할 수 있다.
    /// </para>
    /// <para>
    /// 이 클래스의 모든 기하 계산은 정수만 사용한다. 같은 경로와 진행도라면
    /// Editor, Windows, WebGL 어디서나 같은 좌표가 나오게 하기 위해서다.
    /// </para>
    /// </remarks>
    internal sealed class PathModel
    {
        // points는 경로 경유점, segmentLengths는 경유점 사이 거리,
        // segmentStarts는 각 구간이 전체 진행도에서 시작하는 누적 거리다.
        private readonly SimPosition[] points;
        private readonly long[] segmentLengths;
        private readonly long[] segmentStarts;

        /// <summary>
        /// 최소 두 경유점으로 경로를 만들고 각 구간 길이와 누적 시작점을 미리 계산한다.
        /// </summary>
        public PathModel(SimPosition[] sourcePoints)
        {
            if (sourcePoints == null || sourcePoints.Length < 2)
            {
                throw new ArgumentException("A path requires at least two points.", nameof(sourcePoints));
            }

            // 호출자가 원본 배열을 바꿔도 전투 중 경로가 변하지 않도록 복사한다.
            points = new SimPosition[sourcePoints.Length];
            Array.Copy(sourcePoints, points, sourcePoints.Length);
            segmentLengths = new long[points.Length - 1];
            segmentStarts = new long[points.Length - 1];

            long total = 0;
            for (int i = 0; i < segmentLengths.Length; i++)
            {
                segmentStarts[i] = total;
                long dx = points[i + 1].X.MilliUnits - points[i].X.MilliUnits;
                long dy = points[i + 1].Y.MilliUnits - points[i].Y.MilliUnits;
                // 피타고라스 거리의 제곱근도 부동소수점 Math.Sqrt 대신
                // 정수 제곱근을 사용해 플랫폼별 반올림 차이를 없앤다.
                long length = (long)IntegerSqrt(
                    DeterministicMath.SaturatingSquareSum(dx, dy));
                segmentLengths[i] = Math.Max(1, length);
                total = checked(total + segmentLengths[i]);
            }

            TotalLengthMilli = total;
        }

        /// <summary>경로 시작부터 끝까지의 총 milli 거리다.</summary>
        public long TotalLengthMilli { get; }

        /// <summary>
        /// 시작점부터의 진행 거리를 경로 위 2차원 논리 좌표로 바꾼다.
        /// </summary>
        /// <param name="progressMilli">
        /// 경로 시작점부터 이동한 milli 거리. 범위 밖 값은 양 끝점으로 고정한다.
        /// </param>
        public SimPosition GetPosition(long progressMilli)
        {
            if (progressMilli <= 0)
            {
                return points[0];
            }

            if (progressMilli >= TotalLengthMilli)
            {
                return points[points.Length - 1];
            }

            // 진행도가 어느 선분에 속하는지 누적 시작 거리로 찾는다.
            int segment = 0;
            while (segment + 1 < segmentStarts.Length &&
                   progressMilli >= segmentStarts[segment + 1])
            {
                segment++;
            }

            long local = progressMilli - segmentStarts[segment];
            long length = segmentLengths[segment];
            long startX = points[segment].X.MilliUnits;
            long startY = points[segment].Y.MilliUnits;
            long dx = points[segment + 1].X.MilliUnits - startX;
            long dy = points[segment + 1].Y.MilliUnits - startY;
            // local / length 비율만큼 시작점에서 끝점 쪽으로 정수 보간한다.
            long x = startX + DeterministicMath.MultiplyDivide(dx, (int)local, (int)length);
            long y = startY + DeterministicMath.MultiplyDivide(dy, (int)local, (int)length);
            return SimPosition.FromMilliUnits(x, y);
        }

        /// <summary>
        /// 특정 진행도에서 경로가 향하는 접선 방향을 basis point 벡터로 구한다.
        /// </summary>
        /// <remarks>
        /// 탄환 밀치기 방향을 경로 진행도로 투영할 때 사용한다. 예를 들어 탄환이
        /// 경로 진행 방향과 같으면 약 +10,000, 반대면 약 -10,000의 내적이 나온다.
        /// </remarks>
        public void GetDirectionBasisPoints(
            long progressMilli,
            out int xBasisPoints,
            out int yBasisPoints)
        {
            long clamped = Math.Max(
                0,
                Math.Min(TotalLengthMilli - 1, progressMilli));
            int segment = 0;
            while (segment + 1 < segmentStarts.Length &&
                   clamped >= segmentStarts[segment + 1])
            {
                segment++;
            }

            long dx =
                points[segment + 1].X.MilliUnits -
                points[segment].X.MilliUnits;
            long dy =
                points[segment + 1].Y.MilliUnits -
                points[segment].Y.MilliUnits;
            int length = checked((int)segmentLengths[segment]);
            xBasisPoints = checked((int)
                DeterministicMath.MultiplyDivide(
                    dx,
                    DeterministicMath.BasisPointScale,
                    length));
            yBasisPoints = checked((int)
                DeterministicMath.MultiplyDivide(
                    dy,
                    DeterministicMath.BasisPointScale,
                    length));
        }

        /// <summary>
        /// 경로의 startProgress에서 endProgress까지 강제 이동할 때 원형 대상과
        /// 처음 접촉하는 이동 거리를 구한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 최종 위치만 검사하면 중간에 통과한 적을 놓치므로, 이동이 걸치는 모든
        /// 경로 선분을 검사한다. 경로가 자기 교차하는 경우도 월드 좌표 접촉을 잡는다.
        /// </para>
        /// <para>
        /// 반환 거리는 대상 중심까지가 아니라 반경 경계에 처음 닿는 지점까지다.
        /// 따라서 큰 적이 조금 멀리 있어도 작은 적보다 먼저 닿는 상황을 올바르게
        /// 정렬할 수 있다.
        /// </para>
        /// </remarks>
        /// <param name="point">충돌 검사 대상 원의 중심 좌표다.</param>
        /// <param name="startProgress">강제 이동 시작 경로 진행도다.</param>
        /// <param name="endProgress">강제 이동 종료 경로 진행도다.</param>
        /// <param name="radiusMilli">두 개체 반경을 합친 접촉 거리다.</param>
        /// <param name="travelDistance">접촉 시점까지 경로를 따라 이동한 거리다.</param>
        /// <returns>이동 구간에서 접촉하면 true다.</returns>
        public bool TryGetSweepContactDistance(
            SimPosition point,
            long startProgress,
            long endProgress,
            int radiusMilli,
            out long travelDistance)
        {
            if (radiusMilli < 0)
            {
                travelDistance = 0;
                return false;
            }

            long start = Math.Max(
                0,
                Math.Min(TotalLengthMilli, startProgress));
            long end = Math.Max(
                0,
                Math.Min(TotalLengthMilli, endProgress));
            // 역방향 밀치기도 같은 구간 집합을 검사하되, 실제 주행 방향은 아래의
            // forward 값으로 되돌려 "처음 닿는 순서"를 계산한다.
            long lower = Math.Min(start, end);
            long upper = Math.Max(start, end);
            long nearestTravel = long.MaxValue;

            for (int segment = 0;
                 segment < segmentLengths.Length;
                 segment++)
            {
                // 전체 이동 범위와 현재 경로 선분이 겹치는 부분만 잘라 검사한다.
                long segmentStart = segmentStarts[segment];
                long segmentEnd = checked(
                    segmentStart + segmentLengths[segment]);
                long overlapStart = Math.Max(lower, segmentStart);
                long overlapEnd = Math.Min(upper, segmentEnd);
                if (overlapStart > overlapEnd)
                {
                    continue;
                }

                bool forward = end >= start;
                long fromProgress = forward
                    ? overlapStart
                    : overlapEnd;
                long toProgress = forward
                    ? overlapEnd
                    : overlapStart;
                SimPosition from = GetPosition(fromProgress);
                SimPosition to = GetPosition(toProgress);
                if (!TryGetSegmentFirstContactDistance(
                        from,
                        to,
                        point,
                        radiusMilli,
                        out long segmentContactDistance,
                        out long segmentLength))
                {
                    continue;
                }

                // 직선 위 접촉 거리를 전체 경로 진행 거리 단위로 환산한다.
                long progressSpan =
                    Math.Abs(toProgress - fromProgress);
                long contactProgressDistance =
                    segmentLength <= 0
                        ? 0
                        : checked(
                            progressSpan *
                            segmentContactDistance) /
                          segmentLength;
                long candidateTravel =
                    Math.Abs(fromProgress - start) +
                    contactProgressDistance;
                nearestTravel = Math.Min(
                    nearestTravel,
                    candidateTravel);
            }

            travelDistance = nearestTravel;
            return nearestTravel != long.MaxValue;
        }

        /// <summary>두 논리 좌표 사이의 정수 직선거리를 milli 단위로 반환한다.</summary>
        public static long DistanceMilli(SimPosition left, SimPosition right)
        {
            return (long)IntegerSqrt(left.DistanceSquaredRaw(right));
        }

        /// <summary>두 좌표 사이 거리가 주어진 원 반경 안인지 제곱거리로 검사한다.</summary>
        public static bool IsWithin(SimPosition left, SimPosition right, int radiusMilli)
        {
            if (radiusMilli < 0)
            {
                return false;
            }

            // 제곱근을 구하지 않고 양쪽을 제곱해 비교하는 편이 빠르고 정확하다.
            ulong square = (ulong)((long)radiusMilli * radiusMilli);
            return left.DistanceSquaredRaw(right) <= square;
        }

        /// <summary>
        /// 현재 좌표에서 목표를 향해 최대 거리만큼 이동한 새 좌표를 계산한다.
        /// 목표가 더 가까우면 정확히 목표 좌표를 반환한다.
        /// </summary>
        public static SimPosition MoveTowards(
            SimPosition current,
            SimPosition target,
            int maxDistanceMilli)
        {
            long dx = target.X.MilliUnits - current.X.MilliUnits;
            long dy = target.Y.MilliUnits - current.Y.MilliUnits;
            long distance = (long)IntegerSqrt(
                DeterministicMath.SaturatingSquareSum(dx, dy));
            if (distance <= maxDistanceMilli || distance == 0)
            {
                return target;
            }

            long moveX = DeterministicMath.MultiplyDivide(
                dx,
                maxDistanceMilli,
                (int)Math.Min(int.MaxValue, distance));
            long moveY = DeterministicMath.MultiplyDivide(
                dy,
                maxDistanceMilli,
                (int)Math.Min(int.MaxValue, distance));
            return SimPosition.FromMilliUnits(
                current.X.MilliUnits + moveX,
                current.Y.MilliUnits + moveY);
        }

        /// <summary>
        /// 하나의 직선 선분을 따라 이동할 때 원과 처음 닿는 직선 이동 거리를 구한다.
        /// </summary>
        private static bool TryGetSegmentFirstContactDistance(
            SimPosition start,
            SimPosition end,
            SimPosition point,
            int radiusMilli,
            out long contactDistance,
            out long segmentLength)
        {
            long vx =
                end.X.MilliUnits - start.X.MilliUnits;
            long vy =
                end.Y.MilliUnits - start.Y.MilliUnits;
            long wx =
                point.X.MilliUnits - start.X.MilliUnits;
            long wy =
                point.Y.MilliUnits - start.Y.MilliUnits;
            segmentLength = (long)IntegerSqrt(
                DeterministicMath.SaturatingSquareSum(
                    vx,
                    vy));
            if (segmentLength <= 0)
            {
                contactDistance = 0;
                return IsWithin(start, point, radiusMilli);
            }

            // 내적은 대상 중심이 선분 진행 방향으로 얼마나 앞에 있는지,
            // 외적 크기는 직선에서 옆으로 얼마나 떨어졌는지를 나타낸다.
            long dot = checked((wx * vx) + (wy * vy));
            long cross = checked((vx * wy) - (vy * wx));
            long crossMagnitude = Math.Abs(cross);
            long perpendicularDistance =
                crossMagnitude / segmentLength;
            if (perpendicularDistance > radiusMilli)
            {
                contactDistance = 0;
                return false;
            }

            long centerProjection = dot / segmentLength;
            long radiusSquared = checked(
                (long)radiusMilli * radiusMilli);
            long perpendicularSquared = checked(
                perpendicularDistance *
                perpendicularDistance);
            // 원을 직선으로 잘랐을 때 생기는 현의 절반 길이를 구한다.
            // 중심 투영점에서 이 길이만큼 앞이 최초 접촉점이다.
            long halfChord = (long)IntegerSqrt(
                (ulong)Math.Max(
                    0,
                    radiusSquared -
                    perpendicularSquared));
            long firstContact = centerProjection - halfChord;
            long lastContact = centerProjection + halfChord;
            if (lastContact < 0 ||
                firstContact > segmentLength)
            {
                contactDistance = 0;
                return false;
            }

            contactDistance = Math.Max(
                0,
                Math.Min(segmentLength, firstContact));
            return true;
        }

        /// <summary>
        /// 음이 아닌 정수의 제곱근을 내림한 값을 부동소수점 없이 구한다.
        /// </summary>
        /// <remarks>
        /// 2비트씩 처리하는 표준 정수 제곱근 알고리즘이다. 결과가 항상 동일하고
        /// ulong 전체 범위에서 중간 곱셈 오버플로를 만들지 않는다.
        /// </remarks>
        private static ulong IntegerSqrt(ulong value)
        {
            ulong result = 0;
            ulong bit = 1UL << 62;
            while (bit > value)
            {
                bit >>= 2;
            }

            while (bit != 0)
            {
                if (value >= result + bit)
                {
                    value -= result + bit;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }

                bit >>= 2;
            }

            return result;
        }
    }
}
