# Ruleforge TD Balance CLI

이 도구는 Unity 화면이나 WebGL 빌드 없이 현재 `GameSimulation`을 직접 실행하는 순수 .NET 8 밸런스 하네스다. 전투를 별도 DPS 공식으로 흉내 내지 않고, 콘텐츠 JSON을 합성·컴파일한 뒤 `SimulationSnapshot` 관찰, 합법 `GameCommand` 제출, 실제 `Step()` 호출로 한 런을 끝낸다.

## 빠른 스모크

저장소 루트에서 다음 형식으로 실행한다. `--repo`는 모든 명령의 공통 옵션이지만 현재 parser에서는 명령 뒤에 놓아야 한다. 생략하면 현재 디렉터리의 상위 경로에서 저장소를 찾는다.

```bash
dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  simulate \
  --difficulty current \
  --policy novice-random-spender \
  --game-seed 1001 \
  --policy-seed 2001

dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  batch \
  --difficulty easy \
  --policy novice-ensemble \
  --seed-set train \
  --limit 8

dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  replay \
  --replay Artifacts/Balance/runs/replays/current-novice-random-spender-1001-2001.json
```

`--limit` 스모크는 탐색·디버깅 증거일 뿐 최종 통과 판정이 아니다. 사용 가능한 전체 옵션은 `<command> --help`를 본다. 성공은 종료 코드 `0`, 사용법·데이터·시뮬레이션·gate·replay 실패는 해당 0이 아닌 코드로 보고한다.

## 실시간 터미널 상태판

저장소 루트에서 아래 스크립트를 실행하면 한 번의 권위 시뮬레이션을
터미널 한 화면에서 확인할 수 있다. 틱별 로그를 아래로 쌓지 않고 같은
텍스트 영역을 계속 갱신한다.

```bash
./Tools/Ruleforge.BalanceCli/scripts/run-live.sh
```

상태판은 현재 웨이브와 단계, 본진 체력, 현재·획득·소비 골드, 살아 있는
적, 처치·누수 수, 활성 탄환·상태이상·타워 수와 누적 피해를 표시한다.
기본 속도는 실제 게임의 4배인 초당 120틱이며, 실제 30Hz 속도로 보려면
다음처럼 실행한다.

```bash
./Tools/Ruleforge.BalanceCli/scripts/run-live.sh \
  --ticks-per-second 30
```

특정 시드와 정책도 일반 CLI 옵션으로 바꿀 수 있다.

```bash
./Tools/Ruleforge.BalanceCli/scripts/run-live.sh \
  --difficulty current \
  --policy novice-random-spender \
  --game-seed 1001 \
  --policy-seed 2001
```

기존 `simulate` 명령에서는 `--live`를 추가하면 같은 상태판이 켜진다.
`--refresh-ms`로 화면 갱신 간격을 50~5000ms 범위에서 조절할 수 있다.
출력을 파일이나 파이프로 보내면 ANSI 화면 제어와 실시간 지연은 자동으로
꺼지고 최종 요약과 JSON 산출물만 남는다.

CLI와 전용 테스트 실행 파일은 `net8.0`을 대상으로 유지하되 더 최신 .NET
런타임에서도 major roll-forward로 실행되도록 설정되어 있다.

## Frozen release 절차

다음은 최종 판정용 절차다. 정책, 목표, 프로필, 프롬프트, seed set을 고정하고 `policy-lock.json`을 갱신한 뒤 실행한다. 아래 명령은 `--limit`를 사용하지 않으므로 Train 64, Validation 64, Holdout 128 전체를 선택한다.

### 1. Train에서 난이도별 인덱스 생성

```bash
dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  discover-cards \
  --difficulty easy \
  --seed-set train \
  --all-contexts \
  --coverage \
  --output-dir Artifacts/Balance/final/indices/easy

dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  discover-cards \
  --difficulty medium \
  --seed-set train \
  --output-dir Artifacts/Balance/final/indices/medium

dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  discover-cards \
  --difficulty hard \
  --seed-set train \
  --output-dir Artifacts/Balance/final/indices/hard

dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  discover-synergies \
  --difficulty hard \
  --seed-set train \
  --card-strength Artifacts/Balance/final/indices/hard/card-strength-index.json \
  --triples \
  --output-dir Artifacts/Balance/final/indices/hard/triple-beam
```

`--triples`는 ordered pair 결과에 triple 측정을 추가한다. 현재 strict evaluator는 compiled-content hash, 난이도, evaluable entry 존재 여부와 entry별 최소 matched sample 수를 검사하지만, triple 존재 여부나 모든 entry의 runtime-clean 여부를 강제하지 않는다. 따라서 `--triples`를 쓴 자료와 runtime 진단을 별도로 확인해야 한다.

현재 discovery 인덱스의 `seedSetHash`는 선택된 prefix의 fingerprint가 아니라 `seed-sets.json` 전체 파일 해시다. Strict evaluator도 이 값을 현재 seed 파일과 대조하지 않는다. 선택 seed 이름·개수·순서를 산출물 자체에서 독립 증명하지 못하므로, 최종 증거에서는 실행 명령과 실제 `n`을 함께 보존해야 한다.

### 2. Freeze 검증과 Validation 64

```bash
dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  verify \
  --output Artifacts/Balance/final/verify/verification.json

dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  evaluate \
  --all-difficulties \
  --seed-set validation \
  --strict-indices \
  --card-synergy-hard Artifacts/Balance/final/indices/hard/triple-beam/card-synergy-index.json \
  --output-dir Artifacts/Balance/final/validation/all-difficulties
```

Validation에서는 `--allow-bootstrap-indices`를 사용하지 않는다. Strict 평가는 난이도별 compiled-content hash가 다르므로 Easy, Medium, Hard의 strength index와 Easy coverage, Hard synergy index를 각각 확인한다. 고정 경로는 `Artifacts/Balance/final/indices/<difficulty>/`이며, 다른 경로를 쓰면 `--card-strength-easy`, `--card-strength-medium`, `--card-strength-hard`, `--card-coverage-easy`, `--card-synergy-hard`로 명시한다.

`verify`는 콘텐츠/프로필 로드, seed partition, 정책 registry, frozen file/policy version hash, 하나의 same-seed 결정성 런과 replay를 검사한다. 전체 난이도 gate, card coverage, 단위·통합 test suite를 대체하지 않으므로 `evaluate`와 `Tests/Ruleforge.BalanceCli.Tests`를 별도로 실행해야 한다.

### 3. 미사용 Holdout 128 최종 1회

```bash
dotnet run --configuration Release \
  --project Tools/Ruleforge.BalanceCli -- \
  evaluate \
  --all-difficulties \
  --seed-set holdout \
  --strict-indices \
  --card-synergy-hard Artifacts/Balance/final/indices/hard/triple-beam/card-synergy-index.json \
  --output-dir Artifacts/Balance/final/holdout/all-difficulties
```

Holdout은 모든 패치와 Validation이 끝난 뒤 미사용 seed로 최종 1회만 실행한다. 현재 parser는 Holdout에서도 `--allow-bootstrap-indices`를 기술적으로 차단하지 않으므로 최종 명령에서는 절대 사용하지 않고 실행 기록을 검토해야 한다. Holdout을 본 뒤 정책·목표·프로필·인덱스를 변경했다면 기존 Holdout은 폐기하고, `seed-sets.json`의 `holdout`을 정말 사용하지 않은 128개 pair로 교체한 후 모든 freeze hash를 갱신해야 한다. 이미 노출된 seed를 같은 이름으로 다시 실행해 최종 증거로 사용하지 않는다.

## 구조

- `Tools/Ruleforge.GameLogic.Net`: Unity 엔진 참조 없이 실제 GameLogic 소스를 공유하는 SDK 프로젝트
- `Content/HeadlessContentLoader`: 기본 콘텐츠와 카드 모듈을 합성하고 난이도 오버레이를 적용한 뒤 실제 효과 컴파일러를 호출
- `Simulation/HeadlessRunDriver`: Snapshot → legal action → CommandResult → Step 루프
- `Simulation/ReplayRecorder`, `ReplayRunner`: 정책을 다시 호출하지 않는 명령·Step 재생과 상태 비교
- `Policies`: 정책 seed로 결정되는 초보·중급·숙련·대조군 정책
- `Evaluation`: batch 통계, 카드 단독 lift, 순서·SubjectType 시너지
- `Balance`: 허용 필드 검증, matched-seed 비교, 제한 최적화
- `Llm`: command-free legal-action 선택과 읽기 전용 패치 제안 경계

## 데이터와 산출물

난이도 프로필, 목표, seed 분할은 `Assets/Game/Data/Balance`에 있다. 프로필은 기본 콘텐츠의 복제본이 아니라 작은 정수/permille 오버레이다.

- JSON은 해시, 전체 run, command/result, 상세 telemetry와 gate 증거를 가진 **권위 원장**이다.
- CSV는 일부 필드를 평탄화한 분석용 파일이며 JSON의 모든 필드를 대체하지 않는다.
- Markdown는 사람이 읽기 쉬운 요약이며 수치 판정은 링크된 JSON으로 다시 확인한다.
- Replay는 `simulate`의 기본 경로인 `Artifacts/Balance/runs/replays/`, batch의 `--replays` 경로, 또는 명시한 `--replay`에 저장한다.

같은 콘텐츠 해시, 프로필 해시, 시나리오, game seed, policy seed, 정책 버전이면 명령 기록과 최종 상태가 같아야 한다.

## Replay 계약

현재 recorder는 schema v2를 쓴다. v2는 `TotalDecisions`를 저장하고 replay 작업 스트림에서 결정 횟수를 다시 계산해 tick/decision timeout을 독립적으로 검증한다. Reader는 기존 schema v1도 읽지만, 새 증거는 v2로 남긴다.

Replay는 정책을 재실행하지 않고 기록된 `GameCommand`와 `Step()`만 재생하여 승패, phase, 체력, 골드, tick, 최종 타워·카드 상태, 권위 해시를 비교한다. 거절된 권위 command는 Error의 재현 가능한 증거다. 반면 정책 선택 예외, host 예외, 취소는 command 스트림에 표현되지 않으므로 기록된 `Result=Error`를 복사해 MATCH로 인증하지 않는다.

## 운영 원칙

- 정상 밸런스 런에서 debug gold, 강제 체력, 강제 적 생성 등 sandbox 조작을 사용하지 않는다.
- Timeout, 예외, safety 오류와 정책의 거절 명령이 있는 런은 원본 진단을 보존하면서 승률 집계에서는 패배로 처리한다.
- LLM 결과는 출시 gate가 아니다. 결정적 C# 정책 결과만 gate 판정에 사용한다.
- 이 작업의 명시적 범위에서 Unity Editor, Scene, prefab, 렌더 프레임, WebGL 빌드를 실행하지 않는다. CLI 증거는 브라우저 성능이나 presentation 연결 검증이 아니다.
