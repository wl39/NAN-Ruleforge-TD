using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 2차원 공간을 정사각형 칸으로 나눠 근처 적 후보를 빠르게 찾는 내부 인덱스다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 폭발이나 타워 사거리 검사 때 모든 적을 매번 순회하면 적 수가 늘수록 비용이
    /// 급격히 커진다. 이 구조는 먼저 검색 반경과 겹치는 칸의 적만 후보로 돌려준다.
    /// </para>
    /// <para>
    /// Query 결과는 "사각형 칸 안의 후보"이므로 호출자는 마지막에 실제 원 거리
    /// 검사를 해야 한다. 물리 판정 결과를 안정화하기 위해 반환 ID는 정렬한다.
    /// </para>
    /// </remarks>
    internal sealed class SpatialHashGrid
    {
        // 셀 한 변의 milli 길이와, (셀 X,Y)를 합친 키별 적 ID 목록이다.
        private readonly int cellSizeMilli;
        private readonly Dictionary<long, List<EntityId>> cells =
            new Dictionary<long, List<EntityId>>();
        // 매 틱 List를 새로 만들지 않도록 지난 틱의 버킷을 비워 재사용한다.
        // WebGL에서는 이런 작은 할당 감소가 가비지 컬렉션 멈춤을 줄이는 데 중요하다.
        private readonly List<List<EntityId>> bucketPool = new List<List<EntityId>>();
        private int usedBucketCount;

        /// <summary>사용할 셀 한 변의 길이를 지정해 빈 인덱스를 만든다.</summary>
        public SpatialHashGrid(int cellSizeMilli)
        {
            if (cellSizeMilli <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeMilli));
            }

            this.cellSizeMilli = cellSizeMilli;
        }

        /// <summary>
        /// 살아 있는 모든 적의 현재 Position을 기준으로 공간 인덱스를 다시 만든다.
        /// 적 이동이 끝난 뒤, 타워와 탄환 충돌 판정 전에 호출한다.
        /// </summary>
        public void Rebuild(List<EnemyState> enemies)
        {
            // 이전 프레임 버킷의 용량은 유지하고 내용만 비운다.
            for (int i = 0; i < usedBucketCount; i++)
            {
                bucketPool[i].Clear();
            }

            usedBucketCount = 0;
            cells.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (!enemy.Alive)
                {
                    continue;
                }

                // 월드 좌표를 정수 셀 좌표로 변환한다.
                int x = FloorDivide(enemy.Position.X.MilliUnits, cellSizeMilli);
                int y = FloorDivide(enemy.Position.Y.MilliUnits, cellSizeMilli);
                long key = MakeKey(x, y);
                if (!cells.TryGetValue(key, out List<EntityId> bucket))
                {
                    if (usedBucketCount < bucketPool.Count)
                    {
                        bucket = bucketPool[usedBucketCount];
                    }
                    else
                    {
                        bucket = new List<EntityId>(8);
                        bucketPool.Add(bucket);
                    }

                    usedBucketCount++;
                    cells.Add(key, bucket);
                }

                bucket.Add(enemy.Id);
            }
        }

        /// <summary>
        /// 중심과 반경을 감싸는 셀에 들어 있는 적 ID 후보를 오름차순으로 반환한다.
        /// </summary>
        /// <param name="center">검색 원의 중심 논리 좌표다.</param>
        /// <param name="radiusMilli">검색 반경의 milli 거리다.</param>
        /// <param name="results">내용을 비운 뒤 후보 ID를 채울 재사용 목록이다.</param>
        public void Query(
            SimPosition center,
            int radiusMilli,
            List<EntityId> results)
        {
            results.Clear();
            // 원을 감싸는 축 정렬 사각형이 걸치는 셀 범위를 계산한다.
            int minX = FloorDivide(center.X.MilliUnits - radiusMilli, cellSizeMilli);
            int maxX = FloorDivide(center.X.MilliUnits + radiusMilli, cellSizeMilli);
            int minY = FloorDivide(center.Y.MilliUnits - radiusMilli, cellSizeMilli);
            int maxY = FloorDivide(center.Y.MilliUnits + radiusMilli, cellSizeMilli);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!cells.TryGetValue(MakeKey(x, y), out List<EntityId> bucket))
                    {
                        continue;
                    }

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        results.Add(bucket[i]);
                    }
                }
            }

            // Dictionary와 셀 순회 구현 세부가 전투 결과에 영향을 주지 않도록
            // 항상 안정적인 EntityId 순서로 정렬한다.
            results.Sort((left, right) => left.Value.CompareTo(right.Value));
        }

        /// <summary>
        /// 음수 좌표도 수학적 바닥 나눗셈이 되도록 보정해 셀 경계를 계산한다.
        /// </summary>
        /// <remarks>
        /// C# 정수 나눗셈은 0 방향으로 버린다. 예를 들어 -1 / 2000은 0이지만
        /// 공간 셀에서는 -1 칸이어야 하므로 나머지가 있는 음수 값을 한 칸 내린다.
        /// </remarks>
        private static int FloorDivide(long value, int divisor)
        {
            long quotient = value / divisor;
            long remainder = value % divisor;
            if (remainder != 0 && value < 0)
            {
                quotient--;
            }

            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, quotient));
        }

        /// <summary>
        /// 두 32비트 셀 좌표를 충돌 없이 하나의 64비트 Dictionary 키로 합친다.
        /// </summary>
        private static long MakeKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }
}
