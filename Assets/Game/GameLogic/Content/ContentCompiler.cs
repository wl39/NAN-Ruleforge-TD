using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Content
{
    /// <summary>
    /// 사람이 편집한 ContentCatalogDto를 검증하고 시뮬레이션 전용 CompiledContent로 변환한다.
    /// 이 경계를 통과한 뒤에는 전투 코드가 문자열 파싱, null 처리, 잘못된 참조 확인을
    /// 반복하지 않아도 된다. 같은 입력 순서와 값은 항상 같은 정수 ID와 콘텐츠 해시를 만든다.
    /// </summary>
    public static class ContentCompiler
    {
        // 이 상수들은 밸런스 권장값이 아니라 잘못된 JSON 한 개가 거대한 배열,
        // 지나치게 긴 이벤트 체인 또는 정수 오버플로를 만드는 것을 막는 입력 안전선이다.
        // 따라서 디자이너가 콘텐츠를 늘릴 때는 실제 필요를 검토한 뒤 의도적으로 조정해야 한다.
        /// <summary>한 콘텐츠 카탈로그에 허용하는 카드 정의 수의 기술적 상한이다.</summary>
        private const int MaxCards = 512;

        /// <summary>한 콘텐츠 카탈로그에 허용하는 타워 정의 수의 기술적 상한이다.</summary>
        private const int MaxTowers = 128;

        /// <summary>한 콘텐츠 카탈로그에 허용하는 적 원형 수의 기술적 상한이다.</summary>
        private const int MaxEnemies = 256;

        /// <summary>한 런 카탈로그에 허용하는 웨이브 수의 기술적 상한이다.</summary>
        private const int MaxWaves = 256;

        /// <summary>카드의 탄환 또는 적 해석 한쪽에 들어갈 수 있는 효과 노드 수다.</summary>
        private const int MaxEffectsPerInterpretation = 16;

        /// <summary>카드 한 장에 붙일 수 있는 시너지 태그 수다.</summary>
        private const int MaxTagsPerCard = 16;

        /// <summary>범위 효과가 입력할 수 있는 최대 milli 반지름이다.</summary>
        private const int MaxSpatialRadiusMilli = 100_000;

        /// <summary>맵 좌표 한 축의 원점 기준 최대 절댓값이다.</summary>
        private const int MaxCoordinateMilli = 500_000;

        /// <summary>지속시간·간격 입력에 허용하는 최대 정수 틱 수다.</summary>
        private const int MaxDurationTicks = 36_000;

        /// <summary>단일 상태 효과가 입력할 수 있는 최대 중첩 수다.</summary>
        private const int MaxStacks = 256;

        /// <summary>피해 등 범용 정수 매개변수의 오버플로 방지 상한이다.</summary>
        private const int MaxScalar = 1_000_000_000;

        /// <summary>웨이브 하나에 예약할 수 있는 적 총수의 기술적 상한이다.</summary>
        private const int MaxSpawnsPerWave = 100_000;

        /// <summary>
        /// JSON에서 읽은 전체 카탈로그를 한 번에 검증하고 런타임 콘텐츠로 컴파일한다.
        /// 가능한 오류를 모두 수집한 후 한 예외로 보고하므로, 디자이너가 여러 문제를
        /// 한 번에 수정할 수 있다. source 자체가 null인 경우만 즉시 실패한다.
        /// </summary>
        /// <param name="source">역직렬화가 끝난 변경 가능한 JSON 입력 객체다.</param>
        /// <param name="isOperationRegistered">
        /// 각 EffectOperation에 실제 executor가 등록되었는지 확인하는 선택적 함수다.
        /// EffectRegistry.IsRegistered를 넘기면 데이터와 코드 구현의 누락까지 로딩 시 잡는다.
        /// </param>
        /// <returns>검증과 정수 ID 변환이 끝난 시뮬레이션 입력이다.</returns>
        /// <exception cref="ContentValidationException">
        /// 필수 정의, 참조, 범위, 이중 해석 또는 안전 예산 중 하나라도 잘못됐을 때 발생한다.
        /// </exception>
        public static CompiledContent Compile(
            ContentCatalogDto source,
            Func<EffectOperation, bool> isOperationRegistered = null)
        {
            if (source == null)
            {
                throw new ContentValidationException("Content catalog is null.");
            }

            // 첫 오류에서 중단하지 않고 리스트에 계속 추가한다.
            // 아래 반복문이 null 항목을 건너뛰는 이유도 남은 정의를 최대한 더 검사하기 위해서다.
            var errors = new List<string>();

            // JSON에서 배열 자체가 빠졌을 때 null을 빈 배열로 정규화한다.
            // 이렇게 하면 이후 검사는 null 분기 대신 "필수 배열이 비었음"으로 일관되게 보고한다.
            CardDefinitionDto[] cardDtos = source.cards ?? Array.Empty<CardDefinitionDto>();
            TowerDefinitionDto[] towerDtos = source.towers ?? Array.Empty<TowerDefinitionDto>();
            EnemyDefinitionDto[] enemyDtos = source.enemies ?? Array.Empty<EnemyDefinitionDto>();
            WaveDefinitionDto[] waveDtos = source.waves ?? Array.Empty<WaveDefinitionDto>();
            if (source.version <= 0)
            {
                errors.Add("Content version must be positive.");
            }
            if (cardDtos.Length == 0 ||
                towerDtos.Length == 0 ||
                enemyDtos.Length == 0 ||
                waveDtos.Length == 0)
            {
                errors.Add(
                    "Content needs at least one card, tower, enemy, and wave.");
            }
            if (cardDtos.Length > MaxCards ||
                towerDtos.Length > MaxTowers ||
                enemyDtos.Length > MaxEnemies ||
                waveDtos.Length > MaxWaves)
            {
                errors.Add(
                    "Content exceeds catalog limits (cards " + MaxCards +
                    ", towers " + MaxTowers + ", enemies " + MaxEnemies +
                    ", waves " + MaxWaves + ").");
            }

            // 문자열 안정 ID를 현재 카탈로그 배열 인덱스 기반 정수 ID로 바꾼다.
            // stableId는 저장/JSON 참조에 적합하고, CardId 같은 정수 ID는 매 틱 배열 조회에 적합하다.
            // StringComparer.Ordinal을 사용하므로 운영체제 언어나 문화권에 따라 비교가 달라지지 않는다.
            var cardIds = BuildIds<CardDefinitionDto, CardId>(
                cardDtos,
                item => item == null ? null : item.id,
                value => new CardId(value),
                "card",
                errors);
            var towerIds = BuildIds<TowerDefinitionDto, TowerDefinitionId>(
                towerDtos,
                item => item == null ? null : item.id,
                value => new TowerDefinitionId(value),
                "tower",
                errors);
            var enemyIds = BuildIds<EnemyDefinitionDto, EnemyDefinitionId>(
                enemyDtos,
                item => item == null ? null : item.id,
                value => new EnemyDefinitionId(value),
                "enemy",
                errors);

            // 카드 컴파일 -----------------------------------------------------
            // 각 카드의 공통 메타데이터와 두 해석 프로그램을 같은 정수 ID 아래 묶는다.
            var cards = new CompiledCardDefinition[cardDtos.Length];
            for (int i = 0; i < cardDtos.Length; i++)
            {
                CardDefinitionDto dto = cardDtos[i];
                if (dto == null)
                {
                    errors.Add("Card entry is null at index " + i + ".");
                    continue;
                }

                if (!Enum.IsDefined(typeof(CardTier), dto.tier))
                {
                    errors.Add("Card '" + dto.id + "' has invalid tier " + dto.tier + ".");
                }

                if (dto.computeCost <= 0 ||
                    dto.computeCost > 1_000 ||
                    dto.slotCost <= 0 ||
                    dto.slotCost > 16)
                {
                    errors.Add("Card '" + dto.id + "' must have positive compute/slot costs.");
                }

                if (dto.tags == null || dto.tags.Length == 0)
                {
                    errors.Add("Card '" + dto.id + "' must define at least one tag.");
                }
                else
                {
                    if (dto.tags.Length > MaxTagsPerCard)
                    {
                        errors.Add(
                            "Card '" + dto.id + "' has too many tags.");
                    }

                    var uniqueTags = new HashSet<string>(
                        StringComparer.Ordinal);
                    for (int tagIndex = 0;
                         tagIndex < dto.tags.Length;
                         tagIndex++)
                    {
                        string tag = dto.tags[tagIndex];
                        if (string.IsNullOrWhiteSpace(tag) ||
                            !uniqueTags.Add(tag))
                        {
                            errors.Add(
                                "Card '" + dto.id +
                                "' has an empty or duplicate tag.");
                        }
                    }
                }

                // 두 배열 모두 CompileEffects를 통과한다. 비어 있는 한쪽 해석은 오류이므로
                // 어떤 타워에 장착해도 "효과 없음"인 카드가 만들어질 수 없다.
                CompiledEffectNode[] projectile = CompileEffects(
                    dto.id,
                    "projectile",
                    dto.projectileEffects,
                    isOperationRegistered,
                    errors);
                CompiledEffectNode[] enemy = CompileEffects(
                    dto.id,
                    "enemy",
                    dto.enemyEffects,
                    isOperationRegistered,
                    errors);

                // i를 정수 ID로 사용한다. 이후 배열 정렬을 끼워 넣지 않는 것이
                // 같은 콘텐츠가 같은 ID와 해시를 갖게 하는 결정성 계약의 일부다.
                cards[i] = new CompiledCardDefinition
                {
                    Id = new CardId(i),
                    StableId = dto.id,
                    DisplayNameKey = dto.displayNameKey ?? string.Empty,
                    Tier = (CardTier)dto.tier,
                    ComputeCost = dto.computeCost,
                    SlotCost = dto.slotCost,
                    Tags = dto.tags ?? Array.Empty<string>(),
                    ProjectileEffects = projectile,
                    EnemyEffects = enemy
                };
            }

            // 타워 컴파일 -----------------------------------------------------
            // JSON 문자열 trigger/subject/selector를 enum으로 바꾸고 슬롯 및 전투 수치를 검증한다.
            var towers = new CompiledTowerDefinition[towerDtos.Length];
            for (int i = 0; i < towerDtos.Length; i++)
            {
                TowerDefinitionDto dto = towerDtos[i];
                if (dto == null)
                {
                    errors.Add("Tower entry is null at index " + i + ".");
                    continue;
                }

                if (!TryParseEnum(dto.trigger, out TowerTrigger trigger))
                {
                    errors.Add("Tower '" + dto.id + "' has invalid trigger '" + dto.trigger + "'.");
                }

                if (!TryParseEnum(dto.subjectTypeMode, out SubjectTypeMode subjectMode))
                {
                    errors.Add(
                        "Tower '" + dto.id + "' has invalid subject mode '" +
                        dto.subjectTypeMode + "'.");
                }

                if (!TryParseEnum(dto.selector, out SubjectSelector selector))
                {
                    errors.Add(
                        "Tower '" + dto.id + "' has invalid selector '" + dto.selector + "'.");
                }

                if (dto.slotCount <= 0 ||
                    dto.slotCount > 16 ||
                    dto.computeCapacity <= 0 ||
                    dto.computeCapacity > 1_000)
                {
                    errors.Add("Tower '" + dto.id + "' must have slots and compute capacity.");
                }
                if (dto.cooldownTicks < 0 ||
                    dto.cooldownTicks > MaxDurationTicks ||
                    dto.rangeMilli < 0 ||
                    dto.rangeMilli > MaxSpatialRadiusMilli ||
                    dto.baseDamageMilli < 0 ||
                    dto.baseDamageMilli > MaxScalar ||
                    dto.projectileSpeedMilliPerTick < 0 ||
                    dto.projectileSpeedMilliPerTick > 100_000 ||
                    dto.projectileLifetimeTicks <= 0 ||
                    dto.projectileLifetimeTicks > MaxDurationTicks ||
                    dto.selectorRadiusMilli < 0 ||
                    dto.selectorRadiusMilli > MaxSpatialRadiusMilli ||
                    dto.targetLimit <= 0 ||
                    dto.targetLimit > 1_024 ||
                    dto.perTargetCooldownTicks < 0 ||
                    dto.perTargetCooldownTicks > MaxDurationTicks)
                {
                    errors.Add(
                        "Tower '" + dto.id +
                        "' has a negative or invalid combat value.");
                }

                towers[i] = new CompiledTowerDefinition
                {
                    Id = new TowerDefinitionId(i),
                    StableId = dto.id,
                    DisplayNameKey = dto.displayNameKey ?? string.Empty,
                    Trigger = trigger,
                    SubjectTypeMode = subjectMode,
                    Selector = selector,
                    SlotCount = dto.slotCount,
                    ComputeCapacity = dto.computeCapacity,
                    CooldownTicks = dto.cooldownTicks,
                    RangeMilli = dto.rangeMilli,
                    BaseDamageMilli = dto.baseDamageMilli,
                    ProjectileSpeedMilliPerTick =
                        dto.projectileSpeedMilliPerTick,
                    ProjectileLifetimeTicks = dto.projectileLifetimeTicks,
                    SelectorRadiusMilli = dto.selectorRadiusMilli,
                    TargetLimit = dto.targetLimit,
                    PerTargetCooldownTicks = dto.perTargetCooldownTicks
                };
            }

            // 적 컴파일 -------------------------------------------------------
            // 적 원형의 체력, 이동, 저항, 보상 가계 예산과 제어 게이지 범위를 확인한다.
            var enemies = new CompiledEnemyDefinition[enemyDtos.Length];
            for (int i = 0; i < enemyDtos.Length; i++)
            {
                EnemyDefinitionDto dto = enemyDtos[i];
                if (dto == null)
                {
                    errors.Add("Enemy entry is null at index " + i + ".");
                    continue;
                }

                if (!TryParseEnum(dto.rank, out EnemyRank rank))
                {
                    errors.Add("Enemy '" + dto.id + "' has invalid rank '" + dto.rank + "'.");
                }

                if (dto.maxHealthMilli <= 0 ||
                    dto.maxHealthMilli > MaxScalar ||
                    dto.speedMilliPerTick <= 0 ||
                    dto.speedMilliPerTick > 100_000)
                {
                    errors.Add("Enemy '" + dto.id + "' needs positive health and speed.");
                }
                if (dto.armor < 0 ||
                    dto.armor > 1_000_000 ||
                    dto.rewardBudget < 0 ||
                    dto.rewardBudget > 1_000_000 ||
                    dto.waveProgressBudget < 0 ||
                    dto.waveProgressBudget > 1_000_000 ||
                    dto.leakDamage < 0 ||
                    dto.leakDamage > 1_000_000 ||
                    dto.fireResistanceBps < -10000 ||
                    dto.fireResistanceBps > 10000 ||
                    dto.poisonResistanceBps < -10000 ||
                    dto.poisonResistanceBps > 10000 ||
                    dto.controlGaugeThreshold <= 0 ||
                    dto.controlGaugeThreshold > 100_000 ||
                    dto.controlGaugeStep < 0 ||
                    dto.controlGaugeStep > 100_000)
                {
                    errors.Add(
                        "Enemy '" + dto.id +
                        "' has a negative or out-of-range combat value.");
                }

                enemies[i] = new CompiledEnemyDefinition
                {
                    Id = new EnemyDefinitionId(i),
                    StableId = dto.id,
                    DisplayNameKey = dto.displayNameKey ?? string.Empty,
                    Rank = rank,
                    MaxHealthMilli = dto.maxHealthMilli,
                    Armor = dto.armor,
                    SpeedMilliPerTick = dto.speedMilliPerTick,
                    RewardBudget = dto.rewardBudget,
                    WaveProgressBudget = dto.waveProgressBudget,
                    LeakDamage = dto.leakDamage,
                    FireResistanceBps = dto.fireResistanceBps,
                    PoisonResistanceBps = dto.poisonResistanceBps,
                    ControlGaugeThreshold = dto.controlGaugeThreshold,
                    ControlGaugeStep = dto.controlGaugeStep
                };
            }

            // 웨이브 컴파일 ---------------------------------------------------
            // 문자열 enemyId를 EnemyDefinitionId로 해석하여 전투 중 문자열 검색을 없앤다.
            var waves = new CompiledWaveDefinition[waveDtos.Length];
            var waveStableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < waveDtos.Length; i++)
            {
                WaveDefinitionDto dto = waveDtos[i];
                if (dto == null)
                {
                    errors.Add("Wave entry is null at index " + i + ".");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(dto.id) ||
                    !waveStableIds.Add(dto.id))
                {
                    errors.Add(
                        "Wave at index " + i +
                        " has an empty or duplicate id.");
                }

                WaveSpawnDto[] spawnDtos = dto.spawns ?? Array.Empty<WaveSpawnDto>();
                if (spawnDtos.Length == 0)
                {
                    errors.Add("Wave '" + dto.id + "' has no spawns.");
                }
                var spawns = new CompiledWaveSpawn[spawnDtos.Length];

                // 각 묶음의 count가 개별 상한 이하여도 여러 묶음의 합이 지나치게
                // 클 수 있으므로 long으로 총합을 따로 계산한다.
                long totalSpawnCount = 0;
                for (int spawnIndex = 0; spawnIndex < spawnDtos.Length; spawnIndex++)
                {
                    WaveSpawnDto spawn = spawnDtos[spawnIndex];
                    if (spawn == null ||
                        !enemyIds.TryGetValue(spawn == null ? null : spawn.enemyId, out EnemyDefinitionId enemyId))
                    {
                        errors.Add(
                            "Wave '" + dto.id + "' references unknown enemy at spawn " +
                            spawnIndex + ".");
                        continue;
                    }

                    if (spawn.count <= 0 ||
                        spawn.count > MaxSpawnsPerWave ||
                        spawn.firstSpawnTick < 0 ||
                        spawn.firstSpawnTick > MaxDurationTicks ||
                        spawn.intervalTicks <= 0 ||
                        spawn.intervalTicks > MaxDurationTicks)
                    {
                        errors.Add(
                            "Wave '" + dto.id + "' spawn " + spawnIndex +
                            " needs positive count and interval.");
                    }
                    totalSpawnCount += Math.Max(0, spawn.count);

                    spawns[spawnIndex] = new CompiledWaveSpawn(
                        enemyId,
                        spawn.count,
                        spawn.firstSpawnTick,
                        spawn.intervalTicks);
                }
                if (totalSpawnCount > MaxSpawnsPerWave)
                {
                    errors.Add(
                        "Wave '" + dto.id +
                        "' exceeds the total spawn limit.");
                }

                waves[i] = new CompiledWaveDefinition
                {
                    Id = new WaveId(i),
                    StableId = dto.id,
                    Spawns = spawns
                };
            }

            // 카탈로그 항목이 모두 정수 ID로 준비된 다음, 그것들을 참조하는
            // 런 설정과 안전 예산을 컴파일한다.
            CompiledRunDefinition run = CompileRun(source.run, cardIds, towerIds, errors);
            SafetyLimits safety = CompileSafety(source.safety, errors);

            // 각 개별 값이 int 범위여도 전체 웨이브 보상 합은 넘칠 수 있다.
            // 완성된 카드/적/웨이브를 함께 보는 전역 경제 검사를 마지막에 수행한다.
            ValidateEconomyUpperBound(
                cards,
                enemies,
                waves,
                run,
                errors);

            if (errors.Count > 0)
            {
                // 잘못된 중간 객체는 절대로 반환하지 않는다.
                // 따라서 GameSimulation은 CompiledContent를 받았다면 기본 계약이
                // 모두 성립한다고 믿고 빠른 경로로 실행할 수 있다.
                throw new ContentValidationException(string.Join("\n", errors));
            }

            // 해시는 검증 성공 뒤에만 계산한다. 콘텐츠 버전만 비교하는 것보다
            // 실제 모든 전투 값을 포함하는 해시가 리플레이 불일치를 더 정확히 찾는다.
            ulong contentHash = ComputeContentHash(
                source.version,
                cards,
                towers,
                enemies,
                waves,
                safety,
                run);
            return new CompiledContent(
                source.version,
                contentHash,
                cards,
                towers,
                enemies,
                waves,
                safety,
                run,
                cardIds,
                towerIds,
                enemyIds);
        }

        // 콘텐츠 지문(fingerprint) 계산 --------------------------------------
        // StableHashBuilder에 모든 값을 "정해진 순서"로 넣는다.
        // Dictionary 열거 순서는 런타임에 따라 달라질 여지가 있으므로 사용하지 않고,
        // 컴파일된 배열과 그 안의 배열을 원래 콘텐츠 순서대로 순회한다.
        // 표시 키도 포함하므로 이 값은 순수 전투 밸런스 해시라기보다 카탈로그 전체 지문이다.
        private static ulong ComputeContentHash(
            int version,
            CompiledCardDefinition[] cards,
            CompiledTowerDefinition[] towers,
            CompiledEnemyDefinition[] enemies,
            CompiledWaveDefinition[] waves,
            SafetyLimits safety,
            CompiledRunDefinition run)
        {
            StableHashBuilder hash = default(StableHashBuilder);

            // 길이를 먼저 넣으면 [A, BC]와 [AB, C]처럼 값 연결만으로는
            // 구분하기 어려운 구조도 서로 다른 해시 입력이 된다.
            hash.Add(version);
            hash.Add(cards.Length);
            for (int i = 0; i < cards.Length; i++)
            {
                CompiledCardDefinition card = cards[i];
                hash.Add(card.Id);
                hash.Add(card.StableId);
                hash.Add(card.DisplayNameKey);
                hash.Add((int)card.Tier);
                hash.Add(card.ComputeCost);
                hash.Add(card.SlotCost);
                hash.Add(card.Tags.Length);
                for (int tagIndex = 0;
                     tagIndex < card.Tags.Length;
                     tagIndex++)
                {
                    hash.Add(card.Tags[tagIndex]);
                }

                AppendEffectNodes(ref hash, card.ProjectileEffects);
                AppendEffectNodes(ref hash, card.EnemyEffects);
            }

            hash.Add(towers.Length);
            for (int i = 0; i < towers.Length; i++)
            {
                CompiledTowerDefinition tower = towers[i];
                hash.Add(tower.Id.Value);
                hash.Add(tower.StableId);
                hash.Add(tower.DisplayNameKey);
                hash.Add((int)tower.Trigger);
                hash.Add((int)tower.SubjectTypeMode);
                hash.Add((int)tower.Selector);
                hash.Add(tower.SlotCount);
                hash.Add(tower.ComputeCapacity);
                hash.Add(tower.CooldownTicks);
                hash.Add(tower.RangeMilli);
                hash.Add(tower.BaseDamageMilli);
                hash.Add(tower.ProjectileSpeedMilliPerTick);
                hash.Add(tower.ProjectileLifetimeTicks);
                hash.Add(tower.SelectorRadiusMilli);
                hash.Add(tower.TargetLimit);
                hash.Add(tower.PerTargetCooldownTicks);
            }

            hash.Add(enemies.Length);
            for (int i = 0; i < enemies.Length; i++)
            {
                CompiledEnemyDefinition enemy = enemies[i];
                hash.Add(enemy.Id.Value);
                hash.Add(enemy.StableId);
                hash.Add(enemy.DisplayNameKey);
                hash.Add((int)enemy.Rank);
                hash.Add(enemy.MaxHealthMilli);
                hash.Add(enemy.Armor);
                hash.Add(enemy.SpeedMilliPerTick);
                hash.Add(enemy.RewardBudget);
                hash.Add(enemy.WaveProgressBudget);
                hash.Add(enemy.LeakDamage);
                hash.Add(enemy.FireResistanceBps);
                hash.Add(enemy.PoisonResistanceBps);
                hash.Add(enemy.ControlGaugeThreshold);
                hash.Add(enemy.ControlGaugeStep);
            }

            hash.Add(waves.Length);
            for (int i = 0; i < waves.Length; i++)
            {
                CompiledWaveDefinition wave = waves[i];
                hash.Add(wave.Id.Value);
                hash.Add(wave.StableId);
                hash.Add(wave.Spawns.Length);
                for (int spawnIndex = 0;
                     spawnIndex < wave.Spawns.Length;
                     spawnIndex++)
                {
                    CompiledWaveSpawn spawn = wave.Spawns[spawnIndex];
                    hash.Add(spawn.EnemyId.Value);
                    hash.Add(spawn.Count);
                    hash.Add(spawn.FirstSpawnTick);
                    hash.Add(spawn.IntervalTicks);
                }
            }

            // 안전 예산이 달라지면 같은 카드 조합도 어느 지점에서 거절되는지가
            // 달라질 수 있으므로 반드시 콘텐츠 해시에 포함한다.
            hash.Add(safety.MaxChainDepth);
            hash.Add(safety.MaxEventsPerChain);
            hash.Add(safety.MaxProjectileSpawnsPerChain);
            hash.Add(safety.MaxEnemySplitsPerLineage);
            hash.Add(safety.MaxEnemiesPerLineage);
            hash.Add(safety.MaxRicochetsPerProjectile);
            hash.Add(safety.MaxPiercesPerProjectile);
            hash.Add(safety.MaxProjectileLifetimeTicks);
            hash.Add(safety.MaxEventsPerTick);
            hash.Add(safety.MaxQueuedEvents);
            hash.Add(safety.MaxCardTriggersPerChain);
            hash.Add(safety.MaxRecursionsPerChain);
            hash.Add(safety.MaxMythicRepeatsPerChain);
            hash.Add(safety.MaxActiveHazards);
            hash.Add(safety.DiagnosticCapacity);

            // 경로, 시작 카드, 드래프트 가중치 같은 런 규칙도 전투와 난수 소비 순서를
            // 바꾸므로 모두 해시에 포함한다.
            hash.Add(run.TickRate);
            hash.Add(run.BaseHealth);
            hash.Add(run.StartingGold);
            hash.Add(run.StartingTowerChoices.Length);
            for (int i = 0; i < run.StartingTowerChoices.Length; i++)
            {
                hash.Add(run.StartingTowerChoices[i].Value);
            }

            hash.Add(run.InitiallyUnlockedTowers.Length);
            for (int i = 0; i < run.InitiallyUnlockedTowers.Length; i++)
            {
                hash.Add(run.InitiallyUnlockedTowers[i].Value);
            }

            hash.Add(run.StartingCards.Length);
            for (int i = 0; i < run.StartingCards.Length; i++)
            {
                hash.Add(run.StartingCards[i]);
            }

            AppendPositions(ref hash, run.BuildSpots);
            int[] buildSpotUnlockCosts = run.BuildSpotUnlockCosts;
            hash.Add(buildSpotUnlockCosts.Length);
            for (int i = 0; i < buildSpotUnlockCosts.Length; i++)
            {
                hash.Add(buildSpotUnlockCosts[i]);
            }
            AppendPositions(ref hash, run.PathPoints);
            hash.Add(run.DraftOfferCount);
            hash.Add(run.TierWeights.Length);
            for (int i = 0; i < run.TierWeights.Length; i++)
            {
                hash.Add(run.TierWeights[i]);
            }
            hash.Add(run.CriticalDamageBps);
            hash.Add(run.ControlInterruptTicks);
            hash.Add(run.MaxControlGaugeThreshold);
            hash.Add(run.EnemyBaseHitRadiusMilli);

            // Finish는 지금까지 넣은 순서와 값을 하나의 64비트 지문으로 확정한다.
            return hash.Finish();
        }

        // 효과 노드는 공통 필드가 많으므로 카드의 두 해석 모두 같은 순서로 추가한다.
        private static void AppendEffectNodes(
            ref StableHashBuilder hash,
            CompiledEffectNode[] nodes)
        {
            hash.Add(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                CompiledEffectNode node = nodes[i];
                hash.Add((int)node.Operation);
                hash.Add(node.Amount);
                hash.Add(node.Amount2);
                hash.Add(node.Amount3);
                hash.Add(node.DurationTicks);
                hash.Add(node.IntervalTicks);
                hash.Add(node.MaxStacks);
                hash.Add(node.RadiusMilli);
                hash.Add(node.Limit);
                hash.Add(node.ChanceBps);
                hash.Add(node.ReferenceId);
            }
        }

        // 좌표는 부동소수점 Vector가 아니라 정수 SimPosition 그대로 해시에 넣는다.
        private static void AppendPositions(
            ref StableHashBuilder hash,
            SimPosition[] positions)
        {
            hash.Add(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                hash.Add(positions[i]);
            }
        }

        // 카드 해석 하나를 컴파일한다. interpretation 문자열은 오류 메시지에서
        // projectile 쪽인지 enemy 쪽인지 디자이너가 바로 알 수 있도록 전달된다.
        private static CompiledEffectNode[] CompileEffects(
            string cardId,
            string interpretation,
            EffectNodeDto[] source,
            Func<EffectOperation, bool> isOperationRegistered,
            List<string> errors)
        {
            if (source == null || source.Length == 0)
            {
                // 핵심 설계 원칙: 모든 카드는 두 문맥에서 모두 의미가 있어야 한다.
                // 빈 배열을 임시 허용해서 아무 일도 하지 않는 카드로 처리하지 않는다.
                errors.Add(
                    "Card '" + cardId + "' has an empty " + interpretation + " interpretation.");
                return Array.Empty<CompiledEffectNode>();
            }
            if (source.Length > MaxEffectsPerInterpretation)
            {
                errors.Add(
                    "Card '" + cardId + "' has too many " +
                    interpretation + " effects.");
            }

            // 효과 순서는 카드 문장의 왼쪽→오른쪽 실행 의미 그 자체이므로 보존한다.
            var result = new CompiledEffectNode[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                EffectNodeDto dto = source[i];
                if (dto == null || !TryParseEnum(
                        dto == null ? null : dto.operation,
                        out EffectOperation operation))
                {
                    errors.Add(
                        "Card '" + cardId + "' has invalid " + interpretation +
                        " operation at " + i + ".");
                    continue;
                }

                // enum 파싱 성공은 이름을 안다는 뜻일 뿐 실제 코드가 있다는 뜻은 아니다.
                // registry 검사를 함께 사용하면 새 JSON 연산의 구현 누락을 즉시 발견한다.
                if (isOperationRegistered != null && !isOperationRegistered(operation))
                {
                    errors.Add(
                        "Card '" + cardId + "' references unregistered operation " +
                        operation + ".");
                }
                if (dto.amount < 0 ||
                    dto.amount > MaxScalar ||
                    dto.amount2 < 0 ||
                    dto.amount2 > MaxScalar ||
                    dto.amount3 < 0 ||
                    dto.amount3 > MaxScalar ||
                    dto.durationTicks < 0 ||
                    dto.durationTicks > MaxDurationTicks ||
                    dto.intervalTicks < 0 ||
                    dto.intervalTicks > MaxDurationTicks ||
                    dto.maxStacks < 0 ||
                    dto.maxStacks > MaxStacks ||
                    dto.radiusMilli < 0 ||
                    dto.radiusMilli > MaxSpatialRadiusMilli ||
                    dto.limit < 0 ||
                    dto.limit > MaxScalar ||
                    dto.chanceBps < 0 ||
                    dto.chanceBps > 10000)
                {
                    errors.Add(
                        "Card '" + cardId + "' has an invalid " +
                        interpretation + " value at effect " + i + ".");
                }
                // 위 검사는 모든 효과 노드가 공유하는 절대 범위이고,
                // 아래 검사는 Split, Slow, 배율처럼 연산 의미에 맞춘 추가 계약이다.
                ValidateOperationValues(
                    cardId,
                    interpretation,
                    i,
                    operation,
                    dto,
                    errors);
                if (operation == EffectOperation.Split &&
                    source.Length != 1)
                {
                    // Phase 1 Split executor는 카드 프로그램의 나머지를 두 가지에
                    // 이어 붙이는 특별 제어 흐름이다. 같은 해석 안의 다른 노드와
                    // 섞으면 continuation 예약 의미가 모호해져 단독 노드로 제한한다.
                    errors.Add(
                        "Card '" + cardId + "' must use Split as its " +
                        "only " + interpretation + " effect.");
                }

                // referenceId의 null 정규화를 포함한 읽기 전용 값으로 변환한다.
                result[i] = new CompiledEffectNode(
                    operation,
                    dto.amount,
                    dto.amount2,
                    dto.amount3,
                    dto.durationTicks,
                    dto.intervalTicks,
                    dto.maxStacks,
                    dto.radiusMilli,
                    dto.limit,
                    dto.chanceBps,
                    dto.referenceId);
            }

            return result;
        }

        // 런 설정 컴파일 ------------------------------------------------------
        // 시작 카드/타워의 문자열 참조를 위에서 만든 정수 ID 표로 해결하고,
        // X/Y 평행 배열은 SimPosition 배열로 묶는다.
        private static CompiledRunDefinition CompileRun(
            RunDefinitionDto dto,
            Dictionary<string, CardId> cardIds,
            Dictionary<string, TowerDefinitionId> towerIds,
            List<string> errors)
        {
            if (dto == null)
            {
                errors.Add("Run definition is missing.");
                return new CompiledRunDefinition();
            }
            if (dto.tickRate != SafetyLimits.DefaultTicksPerSecond)
            {
                // 현재 시뮬레이션의 모든 durationTicks, 속도, 기준 리플레이는
                // 30Hz를 전제로 하므로 JSON에서 임의의 틱률 변경을 허용하지 않는다.
                errors.Add("Run tick rate must be exactly 30.");
            }
            if (dto.baseHealth <= 0 ||
                dto.baseHealth > 1_000_000 ||
                dto.startingGold < 0 ||
                dto.startingGold > MaxScalar ||
                dto.draftOfferCount <= 0 ||
                dto.draftOfferCount > 10 ||
                dto.criticalDamageBps < 10000 ||
                dto.criticalDamageBps > 100_000 ||
                dto.controlInterruptTicks <= 0 ||
                dto.controlInterruptTicks > MaxDurationTicks ||
                dto.maxControlGaugeThreshold <= 0 ||
                dto.maxControlGaugeThreshold > 100_000 ||
                dto.enemyBaseHitRadiusMilli <= 0 ||
                dto.enemyBaseHitRadiusMilli > 10_000)
            {
                errors.Add(
                    "Run health/draft values must be positive and gold non-negative.");
            }

            // 시작 "선택지"는 플레이어가 이 중 하나를 골라 주력 타워로 해금하는 목록이다.
            string[] towerChoices = dto.startingTowerChoices ?? Array.Empty<string>();
            if (towerChoices.Length == 0)
            {
                errors.Add("Run needs at least one starting tower choice.");
            }
            var compiledTowerChoices = new TowerDefinitionId[towerChoices.Length];
            var uniqueStartingTowers = new HashSet<int>();
            for (int i = 0; i < towerChoices.Length; i++)
            {
                if (!towerIds.TryGetValue(towerChoices[i], out compiledTowerChoices[i]))
                {
                    errors.Add("Run references unknown starting tower '" + towerChoices[i] + "'.");
                }
                else if (!uniqueStartingTowers.Add(
                             compiledTowerChoices[i].Value))
                {
                    errors.Add(
                        "Run repeats starting tower '" +
                        towerChoices[i] + "'.");
                }
            }

            // initiallyUnlocked는 선택 없이 처음부터 건설 가능한 지원 타워다.
            // 선택지와 목적이 다르므로 별도 배열과 중복 검사 집합을 사용한다.
            string[] initiallyUnlocked =
                dto.initiallyUnlockedTowers ?? Array.Empty<string>();
            var compiledInitiallyUnlocked =
                new TowerDefinitionId[initiallyUnlocked.Length];
            var uniqueInitialTowers = new HashSet<int>();
            for (int i = 0; i < initiallyUnlocked.Length; i++)
            {
                if (!towerIds.TryGetValue(
                        initiallyUnlocked[i],
                        out compiledInitiallyUnlocked[i]))
                {
                    errors.Add(
                        "Run references unknown initially unlocked tower '" +
                        initiallyUnlocked[i] + "'.");
                    continue;
                }

                if (!uniqueInitialTowers.Add(
                        compiledInitiallyUnlocked[i].Value))
                {
                    errors.Add(
                        "Run repeats initially unlocked tower '" +
                        initiallyUnlocked[i] + "'.");
                }
            }

            // 시작 카드 배열은 같은 카드 여러 장을 지급하는 설계를 허용하므로
            // 타워 선택지와 달리 중복을 금지하지 않는다.
            string[] startingCards = dto.startingCards ?? Array.Empty<string>();
            if (startingCards.Length == 0)
            {
                errors.Add("Run needs at least one starting card.");
            }
            var compiledCards = new CardId[startingCards.Length];
            for (int i = 0; i < startingCards.Length; i++)
            {
                if (!cardIds.TryGetValue(startingCards[i], out compiledCards[i]))
                {
                    errors.Add("Run references unknown starting card '" + startingCards[i] + "'.");
                }
            }

            // JSON에서는 단순한 정수 X/Y 배열이지만, 여기부터는 X와 Y가 항상
            // 함께 움직이는 SimPosition 값 하나로 취급한다.
            SimPosition[] buildSpots = CompilePositions(
                dto.buildSpotXMilli,
                dto.buildSpotYMilli,
                "build spots",
                errors);
            int[] buildSpotUnlockCosts =
                dto.buildSpotUnlockCosts ?? Array.Empty<int>();
            SimPosition[] pathPoints = CompilePositions(
                dto.pathPointXMilli,
                dto.pathPointYMilli,
                "path points",
                errors);
            if (pathPoints.Length < 2)
            {
                errors.Add("Run path needs at least two points.");
            }
            if (buildSpots.Length == 0)
            {
                errors.Add("Run needs at least one build spot.");
            }
            if (buildSpotUnlockCosts.Length != buildSpots.Length)
            {
                errors.Add(
                    "Run build-spot unlock costs must match build spots.");
            }
            for (int i = 0; i < buildSpotUnlockCosts.Length; i++)
            {
                if (buildSpotUnlockCosts[i] < 0 ||
                    buildSpotUnlockCosts[i] > MaxScalar)
                {
                    errors.Add(
                        "Run build-spot unlock costs must be non-negative " +
                        "and within scalar limits.");
                }
            }
            if (buildSpots.Length > 128 ||
                pathPoints.Length > 1_024)
            {
                errors.Add(
                    "Run exceeds build-spot or path-point limits.");
            }

            // 배열 인덱스 0~4가 Common~Mythic에 정확히 대응하므로 항목 수는 5개다.
            // 가중치는 확률 백분율일 필요가 없고 합이 양수인 상대 비율이면 된다.
            if (dto.tierWeights == null || dto.tierWeights.Length != 5)
            {
                errors.Add("Run tier weights must contain five entries.");
            }
            else
            {
                int totalWeight = 0;
                for (int i = 0; i < dto.tierWeights.Length; i++)
                {
                    if (dto.tierWeights[i] < 0)
                    {
                        errors.Add("Run tier weights cannot be negative.");
                    }
                    totalWeight += Math.Max(0, dto.tierWeights[i]);
                }

                if (totalWeight <= 0)
                {
                    errors.Add("Run tier weights need a positive total.");
                }
            }

            // setter가 internal인 이유는 컴파일 단계에서만 값을 채우고,
            // 외부 UI나 전투 중에는 같은 정의를 다시 쓰지 못하게 하기 위해서다.
            return new CompiledRunDefinition
            {
                TickRate = dto.tickRate,
                BaseHealth = dto.baseHealth,
                StartingGold = dto.startingGold,
                StartingTowerChoices = compiledTowerChoices,
                InitiallyUnlockedTowers = compiledInitiallyUnlocked,
                StartingCards = compiledCards,
                BuildSpots = buildSpots,
                BuildSpotUnlockCosts = buildSpotUnlockCosts,
                PathPoints = pathPoints,
                DraftOfferCount = dto.draftOfferCount,
                TierWeights = dto.tierWeights ?? new[] { 48, 30, 15, 6, 1 },
                CriticalDamageBps = dto.criticalDamageBps,
                ControlInterruptTicks = dto.controlInterruptTicks,
                MaxControlGaugeThreshold =
                    dto.maxControlGaugeThreshold,
                EnemyBaseHitRadiusMilli =
                    dto.enemyBaseHitRadiusMilli
            };
        }

        // 안전 예산 컴파일 ----------------------------------------------------
        // 모든 제한은 양수여야 하며, 너무 큰 "제한" 자체가 메모리 고갈을 만들지 않도록
        // 다시 절대 상한을 둔다. queue는 한 틱 예산 이상을 담을 수 있어야 한다.
        private static SafetyLimits CompileSafety(
            SafetyLimitsDto dto,
            List<string> errors)
        {
            if (dto == null)
            {
                // 기본값 객체로 계속 검사하고 결과를 만들어 추가 오류를 모으되,
                // missing 오류가 남으므로 최종 CompiledContent는 반환되지 않는다.
                errors.Add("Safety limits are missing.");
                dto = new SafetyLimitsDto();
            }

            if (dto.maxChainDepth <= 0 ||
                dto.maxEventsPerChain <= 0 ||
                dto.maxProjectileSpawnsPerChain <= 0 ||
                dto.maxEnemySplitCount <= 0 ||
                dto.maxEnemyLineageMembers <= 0 ||
                dto.maxProjectileBounces <= 0 ||
                dto.maxProjectilePierces <= 0 ||
                dto.maxProjectileLifetimeTicks <= 0 ||
                dto.maxRecursiveTriggersPerChain <= 0 ||
                dto.maxEventsPerTick <= 0 ||
                dto.maxQueuedEvents < dto.maxEventsPerTick ||
                dto.maxCardTriggersPerChain <= 0 ||
                dto.maxMythicRepeatsPerChain <= 0 ||
                dto.maxActiveHazards <= 0 ||
                dto.diagnosticCapacity <= 0 ||
                dto.maxChainDepth > 64 ||
                dto.maxEventsPerChain > 100_000 ||
                dto.maxProjectileSpawnsPerChain > 10_000 ||
                dto.maxEnemySplitCount > 16 ||
                dto.maxEnemyLineageMembers > 1_024 ||
                dto.maxProjectileBounces > 1_024 ||
                dto.maxProjectilePierces > 1_024 ||
                dto.maxProjectileLifetimeTicks > MaxDurationTicks ||
                dto.maxEventsPerTick > 100_000 ||
                dto.maxQueuedEvents > 1_000_000 ||
                dto.maxCardTriggersPerChain > 100_000 ||
                dto.maxRecursiveTriggersPerChain > 1_024 ||
                dto.maxMythicRepeatsPerChain > 1_024 ||
                dto.maxActiveHazards > 100_000 ||
                dto.diagnosticCapacity > 100_000)
            {
                errors.Add(
                    "Safety limits must be positive and queue capacity " +
                    "must cover the per-tick budget.");
            }

            // 검증 오류가 있어도 생성자에는 최소 1 이상의 값만 전달한다.
            // 이것은 잘못된 콘텐츠를 허용하는 보정이 아니라, 이후 전역 검사가
            // 0이나 음수 때문에 별도의 예외로 중단되지 않게 하는 오류 수집 장치다.
            return new SafetyLimits(
                Math.Max(1, dto.maxChainDepth),
                Math.Max(1, dto.maxEventsPerChain),
                Math.Max(1, dto.maxProjectileSpawnsPerChain),
                Math.Max(1, dto.maxEnemySplitCount),
                Math.Max(1, dto.maxEnemyLineageMembers),
                Math.Max(1, dto.maxProjectileBounces),
                Math.Max(1, dto.maxProjectilePierces),
                Math.Max(1, dto.maxProjectileLifetimeTicks),
                Math.Max(1, dto.maxEventsPerTick),
                Math.Max(
                    Math.Max(1, dto.maxEventsPerTick),
                    dto.maxQueuedEvents),
                Math.Max(1, dto.maxCardTriggersPerChain),
                Math.Max(1, dto.maxRecursiveTriggersPerChain),
                Math.Max(1, dto.maxMythicRepeatsPerChain),
                Math.Max(1, dto.maxActiveHazards),
                Math.Max(1, dto.diagnosticCapacity));
        }

        // 여러 정의 종류가 같은 "stable string ID → 배열 인덱스 ID" 규칙을 쓰므로
        // 제네릭 도우미 하나로 카드/타워/적의 검사를 동일하게 유지한다.
        private static Dictionary<string, TId> BuildIds<TSource, TId>(
            TSource[] items,
            Func<TSource, string> getId,
            Func<int, TId> createId,
            string kind,
            List<string> errors)
        {
            // Ordinal 비교는 한국어/영어 OS 설정과 무관하게 같은 바이트 의미로 비교한다.
            // 대소문자도 구분되므로 콘텐츠 ID 표기는 정확히 유지해야 한다.
            var result = new Dictionary<string, TId>(StringComparer.Ordinal);
            for (int i = 0; i < items.Length; i++)
            {
                string id = getId(items[i]);
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add("A " + kind + " at index " + i + " has no id.");
                    continue;
                }

                if (result.ContainsKey(id))
                {
                    errors.Add("Duplicate " + kind + " id '" + id + "'.");
                    continue;
                }

                // 정수 ID는 입력 배열의 현재 위치 i다. stableId와 달리 저장 파일에
                // 영구 기록하기 위한 값이 아니며 해당 CompiledContent 안에서만 유효하다.
                result.Add(id, createId(i));
            }

            return result;
        }

        // 서로 분리된 JSON X/Y 배열을 값 타입 좌표 배열로 결합한다.
        private static SimPosition[] CompilePositions(
            int[] x,
            int[] y,
            string label,
            List<string> errors)
        {
            if (x == null || y == null || x.Length != y.Length)
            {
                // 길이가 다르면 어느 X와 어느 Y를 짝지을지 결정할 수 없으므로
                // 일부만 잘라 쓰지 않고 전체 위치 목록을 잘못된 것으로 본다.
                errors.Add("Run " + label + " need matching x/y arrays.");
                return Array.Empty<SimPosition>();
            }

            var result = new SimPosition[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                // int.MinValue에 Math.Abs(int)를 직접 쓰면 오버플로할 수 있으므로
                // long으로 넓힌 다음 절댓값 범위를 검사한다.
                if (Math.Abs((long)x[i]) > MaxCoordinateMilli ||
                    Math.Abs((long)y[i]) > MaxCoordinateMilli)
                {
                    errors.Add(
                        "Run " + label +
                        " contains an out-of-range coordinate.");
                }
                result[i] = new SimPosition(x[i], y[i]);
            }

            return result;
        }

        // JSON enum 문자열은 사람이 쓰기 편하게 대소문자를 무시해 파싱하지만,
        // 숫자 문자열이나 정의되지 않은 enum 값은 IsDefined로 다시 거절한다.
        private static bool TryParseEnum<T>(string value, out T result)
            where T : struct
        {
            return Enum.TryParse(value, true, out result) &&
                   Enum.IsDefined(typeof(T), result);
        }

        // 공통 숫자 범위만으로는 "Split은 정확히 두 갈래", "배율은 0이 아님" 같은
        // 의미 규칙을 표현할 수 없으므로 연산별 추가 검사를 한곳에 모은다.
        private static void ValidateOperationValues(
            string cardId,
            string interpretation,
            int effectIndex,
            EffectOperation operation,
            EffectNodeDto dto,
            List<string> errors)
        {
            bool invalid = false;
            switch (operation)
            {
                case EffectOperation.Split:
                    // Phase 1 분열은 원본 가지 + 추가 가지, 총 2개로 고정한다.
                    // amount2는 분열 후 위력 배율이므로 0보다 커야 한다.
                    invalid = dto.amount != 2 ||
                              dto.amount2 <= 0 ||
                              dto.amount2 > 30_000;
                    break;
                case EffectOperation.AddPierce:
                    invalid = dto.amount > 10_000 ||
                              dto.amount2 > 10_000;
                    break;
                case EffectOperation.ModifyProjectileSlow:
                case EffectOperation.EnlargeProjectile:
                case EffectOperation.EnlargeEnemy:
                case EffectOperation.ShrinkProjectile:
                case EffectOperation.ShrinkEnemy:
                    // 크기·속도·피해를 0배로 만들어 엔티티 의미를 없애거나
                    // 3배를 넘어 폭증시키는 잘못된 배율을 차단한다.
                    invalid = !IsBoundedMultiplier(dto.amount) ||
                              !IsBoundedMultiplier(dto.amount2) ||
                              !IsBoundedMultiplier(dto.amount3);
                    break;
                case EffectOperation.ApplySlow:
                    invalid = dto.amount > 10_000 ||
                              dto.limit > 10_000;
                    break;
                case EffectOperation.BindExplosion:
                    invalid = dto.amount > 100_000;
                    break;
                case EffectOperation.BindKnockback:
                case EffectOperation.ApplyKnockback:
                    invalid = dto.amount > MaxSpatialRadiusMilli ||
                              dto.amount2 > MaxScalar;
                    break;
                case EffectOperation.BindMark:
                case EffectOperation.ApplyMark:
                    invalid = dto.amount > 10_000 ||
                              dto.limit > 30_000;
                    break;
                case EffectOperation.IncreaseReward:
                    invalid = dto.amount > 100_000 ||
                              dto.limit > 100_000;
                    break;
            }

            if (invalid)
            {
                errors.Add(
                    "Card '" + cardId + "' has out-of-range " +
                    interpretation + " values for " + operation +
                    " at effect " + effectIndex + ".");
            }
        }

        // basis point 배율: 10000 = 1배, 허용 범위는 0 초과 30000(3배) 이하다.
        private static bool IsBoundedMultiplier(int value)
        {
            return value > 0 && value <= 30_000;
        }

        // 전체 런 경제 오버플로 검사 -----------------------------------------
        // 개별 적 보상이 안전해도 웨이브 수, 적 수, 카드 보너스를 곱한 최종 골드는
        // int 범위를 넘을 수 있다. 가능한 최댓값을 보수적으로 계산해 로딩 시 거절한다.
        private static void ValidateEconomyUpperBound(
            CompiledCardDefinition[] cards,
            CompiledEnemyDefinition[] enemies,
            CompiledWaveDefinition[] waves,
            CompiledRunDefinition run,
            List<string> errors)
        {
            // 중간 계산은 int보다 넓은 long을 사용한다.
            long baseEnemyRewards = 0;
            for (int waveIndex = 0;
                 waveIndex < waves.Length;
                 waveIndex++)
            {
                CompiledWaveDefinition wave = waves[waveIndex];
                if (wave == null)
                {
                    continue;
                }

                CompiledWaveSpawn[] spawns = wave.SpawnsInternal;
                for (int spawnIndex = 0;
                     spawnIndex < spawns.Length;
                     spawnIndex++)
                {
                    int enemyIndex = spawns[spawnIndex].EnemyId.Value;
                    if (enemyIndex < 0 ||
                        enemyIndex >= enemies.Length ||
                        enemies[enemyIndex] == null)
                    {
                        continue;
                    }

                    // 분열체는 lineage 원장을 나누므로 원본 적의 RewardBudget을
                    // 웨이브 출현 수만큼만 센다. 분열 횟수로 추가 곱하지 않는다.
                    baseEnemyRewards +=
                        (long)Math.Max(0, spawns[spawnIndex].Count) *
                        Math.Max(
                            0,
                            enemies[enemyIndex].RewardBudget);
                }
            }

            // 모든 카드를 동시에 무한 중첩한다고 가정하지 않고, 현재 Phase 1 규칙에서
            // 적용 가능한 가장 큰 적 보상 보너스와 타워별 웨이브 현상금을 찾는다.
            int maximumRewardBonusBps = 0;
            int maximumBountyPerTowerWave = 0;
            for (int cardIndex = 0;
                 cardIndex < cards.Length;
                 cardIndex++)
            {
                CompiledCardDefinition card = cards[cardIndex];
                if (card == null)
                {
                    continue;
                }

                CompiledEffectNode[] enemyEffects =
                    card.EnemyEffectsInternal;
                for (int effectIndex = 0;
                     effectIndex < enemyEffects.Length;
                     effectIndex++)
                {
                    CompiledEffectNode node =
                        enemyEffects[effectIndex];
                    if (node.Operation ==
                        EffectOperation.IncreaseReward)
                    {
                        maximumRewardBonusBps = Math.Max(
                            maximumRewardBonusBps,
                            node.Limit);
                    }
                    else if (node.Operation ==
                             EffectOperation.EnlargeEnemy)
                    {
                        maximumRewardBonusBps = Math.Max(
                            maximumRewardBonusBps,
                            10_000);
                    }
                }

                CompiledEffectNode[] projectileEffects =
                    card.ProjectileEffectsInternal;
                for (int effectIndex = 0;
                     effectIndex < projectileEffects.Length;
                     effectIndex++)
                {
                    CompiledEffectNode node =
                        projectileEffects[effectIndex];
                    if (node.Operation ==
                        EffectOperation.BindGoldOnHit)
                    {
                        maximumBountyPerTowerWave = Math.Max(
                            maximumBountyPerTowerWave,
                            node.Amount2);
                    }
                }
            }

            // 적 보상 보너스, 모든 건설 지점의 웨이브별 현상금, 시작 골드를 합친
            // 보수적 상한이 실제 Gold 저장형(int)을 넘는지 확인한다.
            long augmentedEnemyRewards =
                DeterministicMath.MultiplyBasisPoints(
                    baseEnemyRewards,
                    checked(10_000 + maximumRewardBonusBps));
            long bountyRewards =
                (long)maximumBountyPerTowerWave *
                run.BuildSpotsInternal.Length *
                waves.Length;
            long maximumGold = run.StartingGold +
                               augmentedEnemyRewards +
                               bountyRewards;
            if (maximumGold > int.MaxValue)
            {
                errors.Add(
                    "Run economy can exceed the 32-bit gold ledger.");
            }
        }

    }
}
