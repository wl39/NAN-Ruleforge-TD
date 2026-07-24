using System;
using System.Collections.Generic;

namespace RuleforgeTD.GameLogic.Persistence
{
    // GameLogic은 파일 경로, PlayerPrefs, 브라우저 IndexedDB 같은 Unity/플랫폼 API를 모른다.
    // 대신 아래 interface(포트)만 정의하고, Unity Runtime 계층이 실제 저장 방식을 구현한다.
    // 이 구조 덕분에 같은 전투 로직을 Editor 테스트, WebGL, 데스크톱에서 재사용할 수 있다.

    /// <summary>
    /// 설정, 메타 진행도, 완료된 런 기록을 키로 저장하는 플랫폼 독립 저장소 계약이다.
    /// Phase 1에서는 전투 도중의 전체 시뮬레이션 저장/재개를 보장하지 않는다.
    /// </summary>
    public interface ISaveRepository
    {
        /// <summary>
        /// 값을 지정한 키에 저장한다. 같은 키가 이미 있을 때의 덮어쓰기와 실제
        /// 직렬화 형식은 Unity Runtime 쪽 구현체가 책임진다.
        /// </summary>
        /// <typeparam name="T">저장할 데이터 계약 타입이다.</typeparam>
        /// <param name="key">예: settings, meta-progress, run-history 같은 논리 키다.</param>
        /// <param name="value">직렬화 가능한 저장 값이다.</param>
        void Save<T>(string key, T value);

        /// <summary>
        /// 키에 저장된 값을 읽어 본다. 데이터가 없으면 예외 대신 false를 반환한다.
        /// 구현체는 손상된 데이터나 역직렬화 실패를 어떤 방식으로 보고할지 명확히 정해야 한다.
        /// </summary>
        /// <typeparam name="T">저장할 때 사용한 것과 호환되는 데이터 타입이다.</typeparam>
        /// <param name="key">조회할 논리 키다.</param>
        /// <param name="value">성공하면 읽은 값을 받는다.</param>
        /// <returns>호환되는 값이 존재해 성공적으로 읽혔으면 true다.</returns>
        bool TryLoad<T>(string key, out T value);

        /// <summary>지정한 키의 저장 데이터를 제거한다. 없는 키의 처리는 구현체가 정한다.</summary>
        /// <param name="key">삭제할 논리 키다.</param>
        void Delete(string key);
    }

    /// <summary>
    /// 저장 payload와 그 구조의 버전을 함께 보관하는 봉투다.
    /// 콘텐츠 밸런스 버전과 저장 스키마 버전은 목적이 다를 수 있으므로 호출부가
    /// 어떤 버전을 넣는지 명시적으로 관리해야 한다.
    /// </summary>
    [Serializable]
    public sealed class VersionedSave<T>
    {
        /// <summary>Payload가 현재 따르는 저장 데이터 구조 버전이다.</summary>
        public int Version;

        /// <summary>실제 설정, 메타 진행도 또는 기록 데이터다.</summary>
        public T Payload;
    }

    /// <summary>
    /// 저장 payload를 정확히 한 버전에서 다음 버전으로 바꾸는 마이그레이션 계약이다.
    /// 여러 버전을 건너뛰는 업그레이드는 SaveMigrationRegistry가 작은 단계를 순서대로 연결한다.
    /// </summary>
    public interface ISaveMigration<T>
    {
        /// <summary>이 변환이 입력으로 받는 저장 스키마 버전이다.</summary>
        int FromVersion { get; }

        /// <summary>변환 결과 버전이며 반드시 FromVersion + 1이어야 한다.</summary>
        int ToVersion { get; }

        /// <summary>이전 구조의 payload를 다음 구조에 맞는 payload로 변환한다.</summary>
        /// <param name="source">이전 버전 규칙을 따르는 데이터다.</param>
        /// <returns>다음 버전 규칙을 따르는 데이터다.</returns>
        T Migrate(T source);
    }

    /// <summary>
    /// 저장 버전별 마이그레이션을 등록하고 필요한 단계를 순서대로 실행한다.
    /// 각 시작 버전에 변환 하나만 허용해 업그레이드 경로가 실행마다 달라지는 것을 막는다.
    /// </summary>
    public sealed class SaveMigrationRegistry<T>
    {
        // key는 FromVersion이다. 예를 들어 1→2 변환은 key 1에 저장된다.
        private readonly Dictionary<int, ISaveMigration<T>> migrations =
            new Dictionary<int, ISaveMigration<T>>();

        /// <summary>
        /// 연속한 한 버전짜리 마이그레이션을 등록한다.
        /// 같은 FromVersion에서 서로 다른 갈래가 생기는 등록은 거절한다.
        /// </summary>
        /// <param name="migration">FromVersion에서 ToVersion으로 가는 변환 구현이다.</param>
        public void Register(ISaveMigration<T> migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (migration.ToVersion != migration.FromVersion + 1)
            {
                // 1→3 같은 큰 점프를 허용하면 중간 버전 규칙이 누락되고,
                // 1→2→3 경로와 결과가 달라질 수 있어 정확히 한 단계만 허용한다.
                throw new ArgumentException(
                    "Save migrations must advance exactly one version.",
                    nameof(migration));
            }

            if (migrations.ContainsKey(migration.FromVersion))
            {
                // 한 버전에서 갈 수 있는 경로를 하나로 고정해야 같은 저장 데이터가
                // 언제나 같은 결과로 업그레이드된다.
                throw new InvalidOperationException(
                    "A migration from version " + migration.FromVersion +
                    " is already registered.");
            }

            migrations.Add(migration.FromVersion, migration);
        }

        /// <summary>
        /// 저장 데이터를 현재 버전부터 목표 버전까지 순차적으로 업그레이드한다.
        /// 원본 VersionedSave 객체는 바꾸지 않고 새 봉투를 반환한다.
        /// </summary>
        /// <param name="source">읽어 온 버전 표시 저장 데이터다.</param>
        /// <param name="targetVersion">코드가 현재 기대하는 저장 스키마 버전이다.</param>
        /// <returns>모든 중간 변환을 적용한 새 버전 표시 저장 데이터다.</returns>
        public VersionedSave<T> Upgrade(
            VersionedSave<T> source,
            int targetVersion)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.Version > targetVersion)
            {
                // 새 코드의 데이터를 옛 구조로 되돌리는 과정은 정보 손실 가능성이 있어
                // 이 계약에서는 지원하지 않는다.
                throw new InvalidOperationException(
                    "Downgrading save data is not supported.");
            }

            T payload = source.Payload;
            int version = source.Version;
            // 예: 1→4 요청은 등록된 1→2, 2→3, 3→4를 정확한 순서로 실행한다.
            while (version < targetVersion)
            {
                if (!migrations.TryGetValue(version, out ISaveMigration<T> migration))
                {
                    // 중간 단계가 하나라도 없으면 일부만 변환한 데이터를 저장하지 않고
                    // 호출자에게 명확한 실패를 알린다.
                    throw new InvalidOperationException(
                        "No save migration is registered from version " + version + ".");
                }

                payload = migration.Migrate(payload);
                version = migration.ToVersion;
            }

            // source 자체를 변경하지 않으므로 실패 전 원본을 보존하기 쉽다.
            return new VersionedSave<T>
            {
                Version = version,
                Payload = payload
            };
        }
    }

    /// <summary>
    /// 런이 승리 또는 패배로 끝난 뒤 통계/기록 화면과 회귀 검증에 남길 요약이다.
    /// 실행 중인 적·탄환 전체를 저장하는 체크포인트가 아니므로 전투 재개 용도가 아니다.
    /// </summary>
    [Serializable]
    public sealed class RunRecord
    {
        /// <summary>이 런이 사용한 콘텐츠 버전이다.</summary>
        public int ContentVersion;

        /// <summary>웨이브·전투·드래프트 난수의 출발점이 된 시드다.</summary>
        public ulong Seed;

        /// <summary>본진이 생존하고 마지막 웨이브를 끝냈는지 나타낸다.</summary>
        public bool Victory;

        /// <summary>종료 시점의 0 기반 웨이브 인덱스다.</summary>
        public int FinalWaveIndex;

        /// <summary>종료 시점까지 진행한 고정 시뮬레이션 틱 수다.</summary>
        public long FinalTick;

        /// <summary>런 동안 획득한 골드 통계다.</summary>
        public int GoldEarned;

        /// <summary>런 동안 확정 사망 처리된 적 수다.</summary>
        public int EnemiesDefeated;

        /// <summary>
        /// 종료 상태 전체의 결정적 해시다.
        /// 같은 콘텐츠·시드·명령 로그 재생 결과가 일치하는지 확인하는 데 사용할 수 있다.
        /// </summary>
        public ulong FinalStateHash;
    }
}
