# Difficulty Definitions

난이도는 적 수치만 높이는 이름표가 아니라 서로 다른 숙련도 정책과 대조군으로 검증하는 데이터 오버레이다. 정확한 gate 수치는 `Assets/Game/Data/Balance/balance-targets.json`, 실제 조정값은 각 `*.profile.json`이 권위다.

## Easy — Reasonable Novice

대상은 규칙을 자세히 모르지만 고의로 자해하지 않는 초보다. 세 novice 정책은 카드 pair/triple 시너지와 순서 최적화를 사용하지 않고, 안전한 카드 후보 중 단순 선택을 하며 계획 단계 골드의 최소 70% 사용을 지향한다. 전투 중 새 타워는 건설하지 않는다.

필수 gate는 세 novice 정책 앙상블과 각 정책의 승률, 승리 체력 분포, starting-card fixture 및 모든 활성 카드의 비자해 viable path다. `NoSpendPolicy`는 투자가 없어도 자동 승리하는지 확인하는 음성 대조군이고, `AdversarialRandomPolicy`는 진단용일 뿐 Easy 통과 기준이 아니다.

시작 타워 분기는 별도 보고한다. 직접 피해가 없는 분기가 실패한다면 시작 규칙을 조용히 변경하지 않고 잠금, 보조 기본 타워, 온보딩 보정, 첫 보상 보정 등의 후보를 수치와 함께 제안한다.

## Medium — Good Standalone

대상은 기본 규칙과 문맥별 단독 성능이 좋은 카드를 아는 중급자다. `GoodStandalonePolicy`는 동일 seed matched 비교로 만든 card-strength index를 사용할 수 있지만 card synergy index, pair/triple 상호작용, 고급 순서 탐색은 사용할 수 없다. 계획 단계에서 합리적으로 건설·업그레이드하며 전투 중 건설은 하지 않는다.

Medium은 GoodStandalone이 안정적으로 승리하는 동시에 novice가 Easy보다 뚜렷하게 실패하고, SynergyTactical은 더 높은 성공률을 보이는지 검사한다.

## Hard — Synergy Tactical

대상은 카드 순서, 슬롯별 SubjectType, 타워 Trigger, 적 대응, 경제 breakpoint를 이해하는 숙련자다. `SynergyTacticalPolicy`는 card-strength와 ordered pair/triple synergy index를 사용하고, 현재 공개 위협이 실제로 높으며 골드가 비용 breakpoint를 넘을 때 Combat 중 새 타워를 지을 수 있다.

Hard는 다음 대조군으로 설계 의도를 분리한다.

- `SynergyNoCombatBuildPolicy`: 동일 전략에서 Combat 건설만 금지
- `SynergyDisabledPolicy`: 동일 경제·위협 판단에서 pair/triple 및 순서 최적화만 제거
- `GoodStandalonePolicy`, novice ensemble: 숙련도 분리 확인
- `OracleSearchPolicy`: frozen Hard 목표의 **필수 feasibility acceptance gate**. 공개 Snapshot의 합법 행동만 점수화하는 공격적 휴리스틱이며, 인간 숙련도 지표나 해당 seed의 논리적 가능성에 대한 증명은 아님

성공 런의 mid-wave build 사용률과 matched-seed 승률 하락을 함께 보며, 지표를 맞추기 위한 의미 없는 건설은 성공으로 해석하지 않는다.

초기 설계 문서의 “Oracle는 통과 기준에 사용하지 않음” 문구와 고정 `balance-targets.json`의 `oracleSearchPolicy.winRateMin` 간에 충돌이 있다. 현재 자동 평가는 고정 목표 파일을 따라 Oracle를 Hard 필수 gate로 처리하며, 이 gate의 해석 범위만 feasibility 진단으로 제한한다.

## 공통 불변 규칙

난이도 프로필은 카드 의미와 기본 카드 성능, 카드 실행 순서, SubjectType, Trigger, RootChain 및 safety 규칙, 본진 최대 체력, 누수·승패 조건을 바꾸지 않는다. 공통 카드 수치 변경이 필요하면 별도의 global patch로 분리하고 세 난이도를 모두 재검증한다.
