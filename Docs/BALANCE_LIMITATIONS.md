# Balance CLI Limitations

이 문서는 구조적으로 보장하지 못하는 부분과 현재 결과를 해석할 때의 한계를 명시한다. 실제 통과·실패 수치와 증거의 최신성은 `BALANCE_RESULTS.md`와 그 문서가 링크한 JSON 권위 원장에서 확인한다. CSV는 평탄화된 분석용 결과, Markdown은 요약이므로 해시·run 세부 근거를 대체하지 않는다.

## 정책 모델의 한계

- 결정적 정책은 정의된 플레이 스타일의 대리 지표이며 실제 인간의 학습, 실수, 발견 과정 전체를 대표하지 않는다.
- 현재 `OracleSearchPolicy`는 공개 Snapshot의 합법 행동을 점수화하는 공격적 feasibility 휴리스틱이다. replay clone/beam coordinator는 아직 연결되어 있지 않고 `OracleActionScores`도 기본 CLI 경로에서는 채워지지 않는다. 따라서 Oracle 패배는 논리적 불가능을 증명하지 않으며, Oracle 승리도 일반 인간 난이도를 보장하지 않는다.
- 초기 제품 설명은 Oracle을 gate 밖 진단 정책으로 설명했지만, 현재 `balance-targets.json`, `EvaluationCommand`, `DIFFICULTY_DEFINITIONS.md`는 `oracleSearchPolicy.winRateMin`을 Hard 필수 **feasibility acceptance gate**로 취급한다. 이 gate는 인간 skill metric이 아니며 Oracle 패배가 논리적 불가능을 증명하지도 않는다.
- card-strength와 synergy index는 측정한 타워, 슬롯, SubjectType, 순서, 레벨, 난이도, seed 문맥에만 유효하다. 미측정 조합으로의 일반화는 추론이다.
- novice의 “아무 카드나”는 모든 카드에 최소 하나의 합법적 비자해 viable path가 있다는 뜻이다. 모든 SubjectType과 모든 순서, 고의적 자해 조합의 승리를 뜻하지 않는다.
- `AdversarialRandomPolicy`도 한 phase에서 12개 행동 뒤에는 진행 정체를 피하려고 `StartWave`/resume 행동을 강제한다. 따라서 모든 합법 행동을 끝까지 균등하게 고르는 순수 무작위 정책은 아니다.
- Easy Validation gate 통과는 시작 타워 분기 전체의 통과를 뜻하지 않으며, Holdout은 safety 오염 때문에 `runtime-valid` gate를 실패했다. 시작 타워와 실제 배치 타워를 모두 고정한 별도 Validation 64-seed 분기에서 Ballista novice ensemble은 유효 승리 189/192(원시 승리 190/192, safety 1건)지만 Mutation Obelisk는 0/192(Timeout 1건)다. 후자는 미해결 온보딩·시작 규칙 문제다.

## 통계의 한계

- 64/64/128 seed 분할은 재현 가능한 표본이지 전체 seed 공간의 완전 탐색이 아니다. 작은 승률 차이는 Wilson 구간과 함께 해석해야 한다.
- `balance-targets.json`의 목표 크기는 Train 64, Validation 64, Holdout 128이지만 현재 baseline과 주요 튜닝 화면은 Train 8, card-strength·coverage·synergy index와 optimizer는 Train 2만 사용했다. `--minimum-index-samples 2`는 schema 최소치일 뿐 Train 64 완료 증거가 아니다. 각 결과에는 실제 `n`을 병기하고 Train 2/8 자료를 최종 통계적 확증으로 표현하지 않는다.
- matched-seed 비교는 분산을 줄이지만 정책 행동이 candidate에서 갈라질 수 있으므로 모든 런이 쌍별로 동일 경로를 밟는 것은 아니다.
- Holdout은 한 번만 사용한다. Holdout을 본 뒤 다시 튜닝하면 최종 수치는 탐색적으로 오염된다.
- 이번 Hard Holdout의 첫 프로세스는 외부 작업에 의해 중간 종료됐고, 정책·프로필·인덱스·seed를 바꾸지 않은 동결 스냅샷에서 128개 전체를 처음부터 다시 실행했다. 중간 튜닝이나 seed 선별은 없었지만, “단 한 번의 실행 시도”라는 운영적 의미는 완벽히 만족하지 않는다.
- 승률 집계에서 `Error`, `Timeout`, safety 진단 또는 거절 command가 있는 런은 raw 결과가 Victory여도 유효 패배다. 원본 outcome과 진단은 보존하며, `runtime-valid` gate도 별도로 실패한다. Replay 불일치는 batch 승률 항목이 아니라 `replay`/`verify` 명령 자체의 실패로 보고한다.
- `successfulRunsWithMidWaveBuildRatio`의 분모는 runtime-clean 유효 승리 런뿐이다. 승리 수가 0이면 비율도 0이며, 전체 런의 건설률과 혼동하면 안 된다. 이 비율만으로 건설의 인과적 필요성을 주장하지 않고 `SynergyNoCombatBuildPolicy`와의 matched win-rate drop을 함께 본다.
- 현재 Easy coverage는 58/58 카드에 대해 232개 대표 문맥을 Train 2 seed로 검사한 결과다. 이는 카드마다 적어도 하나의 테스트된 viable path가 있다는 증거이지 모든 타워·레벨·슬롯·SubjectType·순서가 안전하다는 증거가 아니다.
- 현재 최종 Hard synergy 자료는 Train 2의 제한된 12개 ordered pair와 4개 triple이다. 20,000개 pair enumeration 한도에서 잘린 부분집합이며, 네 pair 문맥은 두 seed 모두 runtime 오염이 있다. 측정된 triple은 runtime-clean이지만 승리가 없고 interaction lift도 음수이므로 강한 일반 시너지를 입증하지 않는다.

## 산출물과 재현 증거의 한계

- `evaluate`의 JSON/CSV/Markdown은 정책별 aggregate와 gate만 저장하며 개별 seed의 `SimulationResult`, command stream, replay를 보존하지 않는다. 개별 런은 `batch.json`으로 남길 수 있고, command와 `Step()` operation replay는 `batch --replays` 또는 `simulate --replay`를 사용해야 한다.
- 현재 시작 타워 Validation 디렉터리에는 aggregate report/CSV만 복원되어 있고 대용량 `batch.json` 원장은 없다. 따라서 Ballista/Mutation 분기 수치는 재현 가능한 aggregate 증거지만 개별 seed의 command/telemetry를 이 경로에서 감사할 수 없다.
- 카드·시너지의 대용량 experiment-enumeration 원본은 현재 `final/indices`에 중복 복원하지 않았고 동결 입력 스냅샷의 `Artifacts/Balance/final/indices`에 보존되어 있다. 인덱스 열거 문맥을 감사할 때는 그 스냅샷 경로를 사용해야 한다.
- `runs.csv`는 간단한 운영 요약으로, 정책 버전, 콘텐츠·프로필·시나리오 해시, replay 경로, 카드/타워 최종 상태와 상세 telemetry를 모두 포함하지 않는다. 완전한 필드는 같은 batch의 JSON을 확인한다.
- batch Markdown의 `Median HP`와 `P10 HP`는 전체 런 분포지만 difficulty gate의 체력 조건은 유효 승리 런만의 분포다. 최종 판정에는 `VictoryRemainingHealth`가 들어 있는 JSON과 gate CSV를 사용한다.
- `verify`는 콘텐츠 로드, 64/64/128 seed 분할, 정책 registry, 12개 frozen 파일, 한 current-profile seed의 재실행, 한 정상 replay를 검사하는 smoke command다. 전체 단위·통합 테스트, 세 난이도 gate, 모든 seed, card discovery, optimizer를 대신하지 않는다.
- 27개 .NET 하네스는 실제 GameSimulation으로 terminal/replay/card fixture를 검사하지만, 실제 `SafetyLimitReached`를 유발하는 전용 통합 런, Draft 선택만을 격리한 검증, 명시적으로 최종 보스까지 도달하는 전용 테스트는 없다. 일부 safety 집계 검사는 합성 `SimulationResult`를 사용한다.
- policy lock은 프로필·목표·seed·정책 파일·프롬프트를 고정하지만 GameLogic 전체, CLI 전체 source와 실행 DLL hash를 기록하지 않는다. 최종 증거는 clean rebuild, test 결과, lock 검증과 산출물 hash를 함께 봐야 한다.
- discovery 인덱스의 `seedSetHash`는 선택된 seed prefix의 fingerprint가 아니라 `seed-sets.json` 전체 파일 해시다. 현재 strict evaluator는 이 해시를 현재 seed 파일과 대조하지 않고 seed-set 이름·선택 개수·순서도 인덱스에서 검증하지 않는다. Train 2 자료를 Train 64 자료로 오인하지 않도록 실행 명령과 실제 entry sample 수를 별도로 감사해야 한다.
- 현재 strict evaluator는 Hard synergy entry가 존재하고 최소 sample 수와 content hash가 맞는지만 확인한다. clean runtime, 양의 lift, pair와 triple 각각의 존재를 강제하지 않으며, `--allow-bootstrap-indices`도 Holdout에서 기술적으로 차단되지 않는다. 이 옵션을 쓰지 않은 명령 기록과 인덱스의 runtime/triple 내용을 사람이 확인해야 한다.
- 최종 Hard strict 평가에서는 `Artifacts/Balance/final/indices/hard/triple-beam/card-synergy-index.json`을 명시해야 한다. 기본 `Artifacts/Balance/final/indices/hard/card-synergy-index.json`은 현재 최종 근거가 아닌 오래된 자료다.

## Telemetry의 한계

현재 GameLogic이 직접 노출하는 이벤트만 정확히 귀속할 수 있다. 완전한 카드별 피해·처치 기여나 연쇄 인과 원장이 없으므로 실행 횟수와 공개 이벤트 기반 진단을 실제 피해 기여로 과대해석하지 않는다.

현재 `CommandSupport`는 scenario의 `CaptureTelemetry`를 항상 켜고 `HeadlessRunDriver`도 항상 telemetry sink를 붙인다. 낮은 수준의 관측 불변성 테스트는 있지만 CLI의 한 run을 telemetry on/off로 전환해 비교하는 경로는 없다. 따라서 telemetry 비활성화 비교를 완료된 CLI 기능으로 주장하지 않는다.

## Replay의 한계

- 신규 replay는 schema v2로 `TotalDecisions`와 command/Step 스트림을 기록한다. Reader는 schema v1을 읽는 호환성을 유지하지만, v1은 v2의 결정 횟수 재구성 교차 검증이 없는 구형 증거다.
- 권위 `GameCommand`의 거절은 replay할 수 있지만 command가 만들어지기 전의 정책 선택 예외, host 예외, 취소는 command 스트림으로 독립 재현할 수 없다. Runner는 기록된 `Result=Error`를 그대로 복사해 MATCH로 인증하지 않는다.

## 데이터 및 콘텐츠 한계

- 현재 권위 콘텐츠는 문서의 초기 MVP 설명과 다를 수 있다. 알려진 차이는 `BALANCE_CLI_AUDIT.md`에 기록하며 GameLogic과 컴파일된 JSON이 실행 판정의 우선권을 가진다.
- 프로필의 `baseContentHash`가 현재 콘텐츠와 다르면 이전 결과를 그대로 비교할 수 없다.
- 새 카드 모듈, 타워, 적, 웨이브가 추가되면 카드 커버리지, 정책 지식, strength/synergy index와 세 난이도 gate를 다시 실행해야 한다.
- normal balance run은 Test Lab의 debug gold, 체력 고정, 강제 inventory/enemy 조작을 사용하지 않는다. Sandbox는 명시된 고정 카드 coverage fixture에만 제한한다.

## LLM과 자동 패치의 한계

LLM은 법적 actionId 선택과 정성적 제안만 수행하며 출시 gate를 결정하지 않는다. 외부 모델 호출의 가용성·비결정성은 C# 정책 평가와 분리된다. Director 제안은 근거가 있어도 자동 승인되지 않으며 source hash, old value, 허용 pointer, 변경 폭, matched-seed 개선, 다른 난이도 회귀를 모두 통과해야 한다. 구조 변경과 global card identity 변경은 인간 검토 대상이다.

현재 LLM adapter는 외부 provider를 연결하는 CLI command가 없는 library 경계다. 응답의 action ID, reason code, evidence metric을 검증하지만 evidence metric과 reason code는 최종 command log에 보존되지 않는다. 따라서 실제 LLM 플레이 실행이나 LLM 근거 원장을 완료된 기능으로 보지 않는다.

현재 `optimize` 명령의 내장 목적함수는 요청한 난이도와 주 정책을 중심으로 평가한다. Easy의 모든 novice/coverage, Medium의 novice 대조, Hard의 모든 제거 대조군, 카드 독점, 난이도 역전, 세 난이도 동시 회귀를 하나의 후보 목적함수에서 자동 평가하지 않는다. 따라서 optimizer의 `ValidationApproved`는 국소 승인 증거이며, 최종 승인은 별도의 세 난이도 strict evaluation 결과가 필요하다.

현재 보존된 Hard optimizer 자료는 Train 2에서 두 후보를 거절했고 선택 후보가 없어 `ValidationBefore`와 `ValidationAfter`가 모두 비어 있다. 승인·적용된 AI 변경은 0건이다. 거절 trial에는 proposal ID, 이유와 측정값은 있지만 patch change 목록이 보존되지 않아 정확한 거절 diff를 재구성할 수 없다.

## CLI 계약의 한계

- `--help`는 주요 옵션을 보여 주지만 `batch`에서도 동작하는 `--starting-tower`, `--placed-tower`, `--subject` 같은 공통 scenario 옵션을 모두 나열하지 않는다.
- parser는 명령별 허용 옵션 목록을 검증하지 않으므로 오타 난 unknown option이 조용히 사용되지 않을 수 있다. 자동화는 산출물의 scenario/profile/index hash와 실제 선택 결과를 확인해야 한다.
- 종료 코드 `0`은 명령 완료를 의미한다. 정상 Defeat batch나 `--require-approval`이 없는 미승인 optimizer도 `0`일 수 있으므로 Victory, gate 통과, 후보 승인은 JSON 필드로 판정한다.

## 플랫폼 범위

CLI는 순수 GameLogic을 검증하며 Unity 프리팹, 렌더링, 입력, VFX/SFX, 카메라, 브라우저 메모리·성능을 검증하지 않는다. 이번 작업의 명시적 범위에서는 Unity/WebGL 빌드를 실행하지 않는다. 따라서 WebGL 프레임 시간, 직렬화/IL2CPP 차이, presentation 연결은 별도의 Unity 검증 단계가 필요하다.
