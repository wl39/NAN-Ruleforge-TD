# Ruleforge TD Headless Balance Results

## 판정

**RELEASE NOT READY.** 순수 .NET headless 실행기, 결정적 정책, replay, 카드 실험, 난이도 gate, 제한 optimizer와 JSON/CSV/Markdown 산출물은 동작한다. 그러나 Easy Holdout의 runtime-valid gate, Medium의 핵심 정책 목표, Hard의 승률·체력·시너지 제거 대조·Oracle 목표가 실패했다. 성공으로 승격할 수 없다.

모든 최종 난이도 평가는 같은 동결 입력을 사용했다.

| 입력 | SHA-256 |
|---|---|
| balance targets | `ccd6bb0aa834b9f57683bc0c64b91eed1057bce383fddea24a6b71e055911bde` |
| seed sets (64/64/128) | `41956e0ae5e655af10a9d9882a4c3858faf2f4bcb2d9c7a13b4d89a4709f70aa` |
| policy lock | `b6f1e40a540a1fa1143438ce66adbb1fdb75cefba83bac2158b8322c86498fd0` |
| Easy profile | `9c330f5ae99f89482c6de74bf0e203a82f29dcfbc90a77088b74c3140f9a499e` |
| Medium profile | `5e81c3468359490986c5f91e9e9c866a5f528fd2c5e91c6f118c17049ca71cd5` |
| Hard profile | `48e758fbf16abe35b70e172057e6e7fd126a993469355954d2770c7bd83e7cb0` |

## Current baseline

Train 8의 `current` identity profile에서 10개 독립 정책은 모두 0/8 승리였다. 각 승률의 Wilson 95% 상한은 0.3244이고 runtime failure는 없었다.

| 정책 | 승리 | 평균 클리어 웨이브 |
|---|---:|---:|
| adversarial-random | 0/8 | 1.25 |
| good-standalone | 0/8 | 2.125 |
| no-spend | 0/8 | 2.0 |
| novice 3종 | 각각 0/8 | 각각 2.0 |
| synergy-tactical | 0/8 | 2.0 |
| synergy-no-combat-build | 0/8 | 2.125 |
| synergy-disabled | 0/8 | 2.0 |
| oracle-search | 0/8 | 3.0 |

권위 파일은 `Artifacts/Balance/final/current-baseline/*/report.json`이며 같은 폴더에 CSV와 Markdown 요약이 있다.

## Validation 및 Holdout

| 난이도 | Validation 64 | Holdout 128 | 최종 판정 |
|---|---|---|---|
| Easy | 10/10 gate PASS | 9/10 gate PASS | FAIL: Holdout runtime-valid |
| Medium | 1/5 gate PASS | 1/5 gate PASS | FAIL |
| Hard | 5/11 gate PASS | 5/11 gate PASS | FAIL |

### Easy

- Validation novice ensemble: 190/192 = 0.989583, Wilson 95% 0.962821–0.997139, 승리 HP median/P10 = 4/4.
- Holdout novice ensemble: 유효 승리 373/384 = 0.971354, Wilson 95% 0.949440–0.983931, 승리 HP median/P10 = 5/4.
- Holdout에는 random 2건, upgrade-first 5건의 runtime failure가 있어 `runtime-valid`가 실패했다. 이 런들은 유효 승률에서 패배로 처리됐다.
- NoSpend는 Validation/Holdout 모두 0승이다. starting-card fixture는 각각 64/64, 128/128이다.
- 별도 Validation 시작 타워 검사: Ballista는 유효 189/192(원시 승리 190/192, safety 1건), Mutation Obelisk는 0/192(Timeout 1건)이다. Easy 전체의 시작 분기 안정성은 통과하지 못했다.

### Medium

- Validation: GoodStandalone 2/64 = 0.03125 (Wilson 0.008612–0.106973), SynergyTactical 1/64 = 0.015625, novice ensemble 60/192 = 0.3125.
- Holdout: GoodStandalone 3/128 = 0.023438 (Wilson 0.008002–0.066644), SynergyTactical 8/128 = 0.0625, novice ensemble 124/384 = 0.322917.
- novice 범위 gate만 통과했다. GoodStandalone 목표 0.70, 승리 HP 2–6, SynergyTactical 목표 0.85와 runtime-valid는 실패했다.
- 주요 runtime failure: Validation Good 4/Tactical 15/novice ensemble 29, Holdout Good 14/Tactical 25/novice ensemble 43.

### Hard

- Validation SynergyTactical: 12/64 = 0.1875, Wilson 0.110646–0.299744, 승리 HP median 19, runtime failure 4.
- Holdout SynergyTactical: 24/128 = 0.1875, Wilson 0.129361–0.263849, 승리 HP median 19, runtime failure 11.
- GoodStandalone은 양쪽 모두 0승, novice ensemble은 Validation 2/192와 Holdout 0/384다. 이 두 상한 gate는 통과했다.
- Oracle은 Validation 6/64 = 0.09375, Holdout 9/128 = 0.070313으로 목표 0.90에 크게 미달했다.
- Hard Holdout은 runtime-valid, tactical 승률, tactical HP, no-combat drop, synergy-disabled drop, Oracle의 6개 gate가 실패했다.

권위 난이도 원장은 다음 경로의 `evaluation.json`이며 동일 폴더에 CSV/Markdown이 있다.

- `Artifacts/Balance/final/frozen-validation/{easy,medium,hard}/`
- `Artifacts/Balance/final/frozen-holdout/{easy,medium,hard}/`

## Hard 전투 중 건설 대조

| 표본 | Tactical | NoCombatBuild | 승률 drop | 목표 | SynergyDisabled | disabled drop |
|---|---:|---:|---:|---:|---:|---:|
| Validation | 12/64 (0.1875) | 0/64 | 0.1875 | >= 0.35 | 18/64 (0.28125) | -0.09375 |
| Holdout | 24/128 (0.1875) | 0/128 | 0.1875 | >= 0.35 | 32/128 (0.25) | -0.0625 |

Tactical의 runtime-clean 승리 런은 모두 mid-wave build를 사용해 비율 1.0이지만, NoCombatBuild 대비 drop이 목표보다 작고 SynergyDisabled가 오히려 더 많이 이겼다. 따라서 전투 중 건설의 인과적 필요성과 카드 시너지 필요성은 입증되지 않았다. Aggregate `evaluate` 산출물에는 개별 건설의 `GoldBefore/After`, slot, threat 원장이 없으므로 ratio/drop 이상은 감사할 수 없다.

## 카드 성능 및 시너지

- Easy coverage: 활성 카드 58/58이 합법·clearable viable path를 가졌고 232개 문맥을 Train 2로 검사했다. 비최적 Death Engine 문맥에서 Ouroboros safety 2건, Rebirth timeout 2건, Sacrifice safety 2건이 있었다.
- Medium/Hard strength: 각각 58개 evaluable, 18개 GoodStandalone. Medium 상위는 Airborne 11.81, Seal 10.0, Time Stop 7.86, Overload 7.79, Infinite Orbit 7.51, Bind 7.23이다. Hard 상위는 Airborne 10.97, Seal 10.0, Shrink 7.94, Time Stop 7.88, Overload 7.82, Infinite Orbit 7.02, Bind 6.75다.
- Hard triple-beam: Train 2, ordered pair 12개와 triple 4개. clean 양의 pair 4개는 lift 0.5, Airborne 반복 pair 4개는 두 seed 모두 decision timeout, triple 4개는 runtime-clean이지만 승리 0과 lift -0.5였다. 강한 시너지 증거가 아니다.
- 대용량 `card-experiment-enumeration.json` 원본은 중복 복원하지 않고 `Artifacts/Balance/frozen-input-snapshot-20260802/Artifacts/Balance/final/indices/` 아래의 동결 스냅샷에 보존했다.
- 인덱스 SHA-256: Easy coverage `5770fde61763bfa175e838a841754a60985d0fe52c9d3948125b00c5d6873d47`, Medium strength `e9a60840df37d948ab1dac181aeefeb09520a8bd09f8ea38929c61a2c21ac42b`, Hard strength `60c37dc56c00cb99bd938be4c0ed6cbd17b32a6cc48cb87ed35dbea4002f0e0b`, Hard triple-beam `3ecd7126bd967595a279d2dd65485ad2aeaa15de4fd16fc6e585a12eb2152937`.

## AI/optimizer

승인·적용된 변경은 0건이다. 보존된 Hard Train 2 / Validation 2 optimizer는 baseline penalty 97에서 두 후보를 검사했다.

- down 후보: penalty 97, 개선 없음으로 거절.
- up 후보: penalty 1097(runtime penalty 1000 포함), 개선 없음으로 거절.
- 선택 patch가 없어 Validation before/after와 matched-seed A/B는 비어 있으며 repository 적용도 false다.
- 이 optimizer 산출물의 policy-lock hash는 최종 동결 전 값이므로 최종 승인 증거가 아니다.

## 검증 결과

- .NET build: 성공, warning 0, error 0.
- 실행형 검증 하네스: 27/27 PASS. 실제 GameLogic load/compile, 합법 action/command, 카드 fixture와 순서, runtime failure 집계, 정책 결정성, replay timeout/error를 포함한다.
- `verify`: 6/6 PASS. 콘텐츠, 64/64/128 seed 분리, 11개 정책, 12개 frozen 파일, same-seed와 replay hash `2396B105E41DA2C1`.
- 새 simulate/replay smoke: Defeat outcome까지 동일하게 MATCH, final state hash `2396B105E41DA2C1`.
- 보존된 과거 Unity 증거(이번 작업에서 재실행하지 않음): EditMode 173/173, PlayMode 65/65.

## 미통과 및 해석 제한

- Easy Holdout runtime-valid와 Mutation Obelisk 시작 분기.
- Medium GoodStandalone/HP/SynergyTactical/runtime-valid.
- Hard Tactical 승률/HP, runtime-valid, 건설·시너지 제거 대조, Oracle.
- Train 64 목표와 달리 strength/coverage/synergy/optimizer는 Train 2, baseline은 Train 8이다.
- Strict index 검사는 선택 seed prefix fingerprint, runtime-clean, 양의 lift, pair/triple 각각의 존재를 보장하지 않는다.
- 27개 하네스에도 실제 safety-limit 발생 런, Draft 전용 흐름, 최종 보스 도달 전용 통합 검증은 빠져 있다. AdversarialRandom도 phase 정체 방지를 위해 12개 행동 뒤 진행 행동을 강제하므로 완전한 균등 무작위는 아니다.
- Hard Holdout 프로세스는 외부 중단 뒤 같은 동결 입력으로 처음부터 재실행됐다. 중간 튜닝은 없었지만 “단 한 번의 실행 시도”라는 운영적 의미는 완벽히 만족하지 않는다.
- CLI는 Unity presentation, WebGL/IL2CPP, 브라우저 성능을 검증하지 않는다.

세부 한계는 `Docs/BALANCE_LIMITATIONS.md`, 명령과 구조는 `Docs/BALANCE_CLI_README.md`를 따른다.
