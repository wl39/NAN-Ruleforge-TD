# Ruleforge TD 게임 로직 아키텍처

## 1. 문서 목적과 범위

이 문서는 Ruleforge TD의 전투 및 런 진행 로직이 따라야 하는 구조적 계약을 정의한다. `AGENTS.md`가 제품 설계의 최상위 원본이며, 이 문서는 그중 Phase 0~1 게임 로직을 구현 가능한 형태로 구체화한다.

현재 실행 가능한 `phase1-content.json`은 카드 58장(Common 14장, Uncommon 18장, Rare 12장, Legendary 9장, Mythic 5장), 타워 3종, 적 7종, 9웨이브를 컴파일한다. 카드 카탈로그는 전부 런타임에 등록됐고, `TOWER_RULES.md`의 18종 중 아직 활성 데이터에 없는 타워는 별도 확장 범위다.

현재 범위에 포함되는 항목은 다음과 같다.

- 카드, 타워, 적, 탄환, 상태이상
- 고정 틱 전투 시뮬레이션
- 이벤트 큐와 연쇄 예산
- 피해, 보상, 웨이브, 드래프트
- 명령 입력, 상태 스냅샷, 표현 이벤트
- 설정과 완료된 런을 위한 저장 포트
- Stage01 고정 건설 지점 입력, 런타임 HUD와 스냅샷 기반 전투 표현
- 적·탄환·상태 파티클 오브젝트 풀과 WebGL Stage01 빌드
- 고밀도 웨이브 예고, 전투 연쇄·카드별 통계와 골드 합산 HUD
- Stage01을 원본으로 재사용하는 Battle Test Lab과 테스트 전용 샌드박스 제어 포트

현재 범위에서 제외되는 항목은 다음과 같다.

- 완성 카드 일러스트와 카드 드래그 앤 드롭 편집
- SFX와 완성형 전투 로그
- Stage01 외 맵의 실제 씬 구성
- 전투 중 저장 및 이어하기

## 2. 계층과 의존성

핵심 규칙은 `RuleforgeTD.GameLogic` 어셈블리에 둔다. 이 어셈블리는 `UnityEngine`, `MonoBehaviour`, `Transform`, `Time`, 물리 엔진을 참조하지 않는다. Unity를 사용하지 않는 것이 아니라, 전투 결과를 결정하는 계층만 Unity의 프레임과 오브젝트 수명에서 분리하는 것이다.

```text
Unity Runtime / 향후 UI
  - 프레임 시간 누적
  - 입력을 GameCommand로 변환
  - JSON 또는 ScriptableObject 원본 로딩
  - Snapshot 보간 및 PresentationEvent 표현
               │
               ▼
RuleforgeTD.GameLogic
  - 명령 검증과 런 상태 머신
  - 30Hz 결정적 시뮬레이션
  - 카드/타워/적/탄환/상태/보상/웨이브
  - Snapshot, PresentationEvent, 상태 해시 생성
               │
               ▼
저장/플랫폼 어댑터
  - ISaveRepository 구현
  - WebGL 저장소 또는 로컬 테스트 저장소
```

기존 `RuleforgeTD.Runtime`의 `EnemyHealth`와 애니메이션 코드는 표현 데모로 보존한다. 해당 컴포넌트의 체력, 사망 여부, Transform 위치는 새 시뮬레이션의 원본 상태가 아니다.

### Stage01 Unity 브리지

`StageOneBattleController`는 건설 지점과 uGUI 입력을 `GameCommand`로 바꾸고, 30Hz 누적 시간에 맞춰 `GameSimulation.Step()`을 호출하는 조정자다. 타워 가격, 최대 레벨, 슬롯 해금, 구매 가능 여부를 다시 계산하지 않고 GameLogic의 건설·업그레이드 견적을 뷰 모델에 전달한다. 타워 프리팹 선택과 프로토타입 외형은 프레젠테이션 카탈로그/팩토리가 담당하며 안정 타워 ID별 분기를 컨트롤러에 두지 않는다. 생성된 루트, 선택 뷰, 선택적 전용 애니메이터, 정의/레벨은 하나의 presentation handle로 캐시하므로 특정 타워 컴포넌트가 없는 전용 프리팹도 불필요하게 재생성하지 않는다. authored 외형보다 높은 데이터 레벨은 같은 타워의 가장 가까운 이전 외형을 사용한다. 일시정지는 Step 호출을 멈추고, 2배속은 같은 실제 시간에 두 배의 고정 틱을 처리한다. Unity `Time.timeScale`은 애니메이션과 파티클 표현 속도만 동기화하며 판정 수치의 원본으로 사용하지 않는다.

`StageOneEnemyView`, `StageOneProjectileView`, 타워 프리팹은 `SimulationSnapshot`을 읽는 대리자다. 적과 탄환은 풀에서 재사용하며, 화상·중독 연출도 `EnemySnapshot.StatusDetails`를 읽을 뿐 지속시간이나 피해를 직접 갱신하지 않는다. 머리 위에는 우선순위가 높은 상태 아이콘을 최대 3개만 표시하고 전체 상태는 적 상세 정보에서 확인한다. `StageOneGameplaySceneInstaller`가 Stage01 씬, 프레젠테이션 카탈로그, 한국어 임시 UI 데이터를 멱등적으로 연결한다.

고밀도 HUD는 `GetWaveForecast()`와 `GetCombatTelemetrySnapshot()`만 읽는다. 계획 단계의 예고 수량은 실제 `CompiledWaveDefinition`과 같은 스폰 배열에서 계산한다. 전투 중에는 연속 처치, 한 RootChain 최대 처치, 폭발·감전·도탄·전염, 12틱 합산 골드, 골드 카드 생성량/남은 한도를 한 줄로 표시한다. 웨이브 후에는 카드별 피해와 발동 횟수를 피해 순으로 표시한다. 이 집계 상태도 결정성 해시에 포함한다.

월드 렌더 순서는 `WorldSortingLayers`가 소유한다. 뒤에서 앞으로 `Route < Tower < Enemy < Object < Effects < Default(UI)` 순서를 사용하며, Terrain, Ground Decals와 건설 지점은 모두 `Route`에 속한다. 따라서 건설 지점 프리팹의 `sortingOrder`가 적보다 높더라도 역할 레이어 경계를 넘어 적을 가릴 수 없다. 타워, 풀링된 적, 장식, 탄환과 선택/VFX는 생성 또는 활성화 시 각 역할 레이어를 다시 적용해 스테이지나 프리팹의 과거 직렬화 값이 런타임 순서를 바꾸지 못하게 한다. 장식의 기존 발밑 Y축 `sortingOrder`는 `Object` 레이어 내부 세부 순서로 계속 사용한다.

적 상세 보기는 풀링된 Transform이 아니라 `EnemySnapshot.Id`를 선택 원본으로 사용한다. `EnemySelectionView`는 클릭 의도와 빨간 코너 표시만, `StageOneEnemyInspectionModelFactory`는 적 정의와 현재 스냅샷의 결합만, `StageOneEnemyInspectionView`는 uGUI 렌더링만 담당한다. `StageOneEnemyInspectionController`는 살아 있는 동안 불변 상세 모델과 논리 위치를 갱신하고, 최종 `EnemyDied` 표현 사건에서 이를 사망 모델과 컨트롤러 소유 카메라 앵커로 동결한다. 따라서 짧은 사망 연출 뒤 원본 뷰가 풀로 반환되거나 같은 프리팹이 새 개체 ID로 재사용되어도 마지막 정보와 사망 위치 포커스는 사용자가 닫거나 다른 대상을 선택할 때까지 유지된다. 디버그 전체 제거, 유출처럼 최종 사망 사건이 없는 제거는 기존처럼 선택을 해제한다. 레벨, 유형, 설명은 `EnemyDefinitionDto`의 `level`, `typeKey`, `descriptionKey`에서 읽으므로 신규 적을 위한 UI 분기를 추가하지 않는다.

Stage01에서는 타워를 클릭하면 별도의 소형 장착 패널이 열린다. 이 패널은 선택 타워의 레벨, 해금 슬롯, 보유 카드, 탄환/적 해석을 스냅샷에서 읽고 모든 변경을 `GameCommand`로 보낸다. 장착 슬롯은 카드 안정 ID를 `StageOneCardArtworkCatalog`로 해석해 카드 원화를 표시하고, 슬롯별 탄환/적 대상 아이콘과 설명 툴팁은 현재 해석의 현지화 문장을 함께 보여준다. 보유 카드의 사용 중 배지와 호버 미니맵은 `CardInstanceSnapshot.TowerId`, 경로 웨이포인트, 건설 지점의 표현 좌표만 읽는 비권위 UI다. 미니맵은 도로·빈 지점·설치 타워·해당 카드 적용 타워를 단순 색 사각형으로 구분할 뿐 배치 규칙을 재계산하지 않는다. 화면에 보이는 슬롯 잠금만으로 규칙을 대신하지 않으며, 시뮬레이션도 레벨별 슬롯 수를 다시 검증한다.

`StageOneCameraController`는 Terrain Tilemap의 실제 렌더 경계를 기준으로 최대 줌아웃을 계산한다. 화면비가 달라져도 일반 탐색 카메라 사각형이 맵 밖으로 나가지 않으며, 휠은 포인터 중심 줌, 가운데 버튼은 경계 안 패닝을 담당한다. 적 상세 보기가 열리면 `IStageOneCameraFocusTarget`의 논리 위치를 패널이 가리지 않는 가용 화면 앵커에 유지하고 수동 패닝을 잠근다. 데스크톱 상세 패널은 `StageOneHudLayoutMetrics`가 정의한 상단 HUD 점유 높이 아래의 우측 영역을 사용한다. 모바일·세로 화면에서는 Safe Area의 하단 3/5를 전체 폭 스크롤 시트로 사용하고, 카메라 앵커는 시트와 상단 HUD 사이의 빈 영역으로 이동한다. 맵 가장자리 적도 패널 아래로 밀리지 않도록 포커스 중에만 필요한 만큼 오버스캔을 허용하고, 포커스를 해제하면 즉시 정상 맵 경계로 복귀한다. Stage01에서는 Pixel Perfect Camera의 크기 덮어쓰기를 비활성화한다. WebGL은 `RuleforgeFullscreen` 템플릿을 사용해 캔버스를 브라우저 뷰포트 전체로 맞추고 기본 푸터와 페이지 여백을 두지 않는다.

### Battle Test Lab

`BattleTestLab.unity`는 전투 맵을 복제하지 않는 작은 bootstrap 씬이다. 실행 시 Build Settings의 최신 `Stage01.unity`를 additive로 로드하고, 그 씬의 `StageOneBattleController`에 `TestLabRuntimeInstaller`를 연결한다. 따라서 맵, 프리팹, 카탈로그와 전투 표현은 Stage01이 계속 단일 원본이며 테스트 씬을 별도로 동기화할 필요가 없다.

테스트 패널은 `ITestLabControlTarget`만 의존한다. 구체 `GameSimulation`과 Stage01 표현 호스트를 함께 아는 코드는 `TestLabBattleControlTarget` 어댑터 한 곳으로 제한한다. GameLogic에서는 `SandboxSimulationControl.Attach(GameSimulation)`가 반환하는 `ISandboxSimulationControl`이 일반 `GameCommand`와 분리된 명시적 우회 경계다. attach 자체는 상태를 바꾸지 않고 Test Lab 설치 시 `EnterSandboxMode`를 호출해야만 수동 스폰, 자원 설정, 전체 콘텐츠 지급·배치와 전투 중 장착이 허용된다. 카드 선택지와 `GrantEveryCard`는 모두 같은 `CompiledContent` 카드 카탈로그를 사용하고, 공용 `StageOneTowerLoadoutView`는 보유 카드 수에 맞춰 재사용 뷰 풀을 동적으로 확장한다. 따라서 콘텐츠 순서 뒤쪽에 추가된 카드가 고정 UI 용량 때문에 누락되지 않는다.

샌드박스 적은 정의 ID, 체력 배율 또는 절대 HP, 이동 속도 배율을 입력받지만 보상·웨이브·카드팩 진행 예산은 0으로 생성된다. 열린 전투에서는 정규 웨이브 예약 스폰과 종료 판정을 실행하지 않고, 적이 유출되어도 기지 체력을 최소 1로 유지해 무한 소환 세션이 패배 화면으로 닫히지 않는다. UI의 자동 소환 시간표와 일시정지 상태는 Runtime이 소유하고, GameLogic 포트는 요청당 배치 수를 검증한다. 활성 적 상한은 GameLogic의 단일 생성 게이트가 소유해 수동 생성뿐 아니라 분열·복제·잔상·보스 소환에도 동일하게 적용한다.

씬 생성, 검증, 전용 WebGL 빌드는 `TestLabSceneBuilder`가 담당한다. 표준 `CraftPixFieldTilemapAssetBuilder.BuildWebGLFromCommandLine`은 Stage01 게시 직후 이 전용 빌더도 실행해 `Builds/WebGL/Stage01`과 `Builds/WebGL/TestLab`의 소스 개정이 갈라지지 않게 한다. 사용법과 조작 항목은 `TEST_LAB.md`를 따른다.

## 3. 공개 진입점

게임 로직의 외부 진입점은 아래 책임으로 제한한다. 실제 C# 선언은 이 의미를 보존해야 한다.

```csharp
GameSimulation.Initialize(
    CompiledContent content,
    RunConfig config,
    ulong seed);

CommandResult GameSimulation.Submit(in GameCommand command);
void GameSimulation.Step();
SimulationSnapshot GameSimulation.GetSnapshot();
SimulationEventBuffer GameSimulation.ReadPresentationEvents();
ulong GameSimulation.ComputeStateHash();
```

- `Initialize`: 검증이 완료된 불변 콘텐츠와 런 설정, 시드를 받아 초기 상태를 만든다. 같은 인스턴스를 새 런으로 초기화할 때 모든 런 상태와 난수 스트림을 함께 초기화한다.
- `Submit`: 현재 런 상태를 기준으로 명령을 즉시 검증하고, 승인된 계획/진행 상태 변경을 호출 순서대로 적용한다. 거절된 명령은 상태를 바꾸지 않는다.
- `Step`: 정확히 한 시뮬레이션 틱을 처리한다. 일시정지 상태에서는 외부가 `Step`을 호출하지 않는다.
- `GetSnapshot`: 표현 계층이 읽는 불변 복사본을 반환한다. 반환 객체를 바꿔도 시뮬레이션은 변하지 않는다.
- `ReadPresentationEvents`: 직전 읽기 이후의 표현용 사건을 순서대로 반환한다. 게임 규칙은 이 버퍼 소비 여부에 의존하지 않는다.
- `ComputeStateHash`: 재현성 검증용으로 규칙 상태만 정규 순서로 해시한다. 로그 문자열과 표현 이벤트 소비 위치는 제외한다.

## 4. 입력 명령

`GameCommand`는 다음 종류를 제공한다.

| 명령 | 허용 상태 | 핵심 검증 |
| --- | --- | --- |
| `ChooseStartingTower` | AwaitingStartingTower | 시작 선택지 포함 여부 |
| `PlaceTower` | Planning, Combat | 빈 고정 건설 지점, 소유 타워 여부, 건설 비용 |
| `UpgradeTower` | Planning, CardPackLoadout | 정의의 다음 레벨 존재 여부, 업그레이드 견적과 잔액 |
| `SetTowerSubjectType` | Planning, CardPackLoadout | 탄환/적 해석, 타워 트리거 호환성 |
| `EquipCard` | Planning, CardPackLoadout | 소유 카드, 레벨 해금 슬롯, 연산력, 중복 정책 |
| `MoveCard` | Planning, CardPackLoadout | 원본/대상 슬롯과 타워가 유효한지 |
| `UnequipCard` | Planning, CardPackLoadout | 장착 중인 카드를 인벤토리로 되돌리는지 |
| `ReorderCard` | Planning, CardPackLoadout | 같은 타워의 해금된 두 슬롯인지 |
| `SelectDraft` | Draft | 현재 제안의 0~2 인덱스인지 |
| `OpenCardPack` | Combat | 월드에 존재하는 특수 몬스터팩인지 |
| `SelectCardPack` | CardPackChoice | 현재 제안의 0~2 인덱스인지 |
| `ResumeCardPackCombat` | CardPackLoadout | 전투 중 획득 카드가 합법적으로 장착됐는지 |
| `StartWave` | Planning | 웨이브 준비 완료 및 최소 타워 배치 |

카드 장착, 이동, 순서 변경은 일반 `Combat`과 `CardPackChoice`에서 잠기고 `Planning`과 `CardPackLoadout`에서만 허용한다. `Submit` 호출 순서가 명령 순서이며, 리플레이는 같은 순서의 명령과 `Step` 호출을 기록한다.

## 5. 런 상태 머신

```text
Uninitialized
  → AwaitingStartingTower
  → Planning
  → Combat
  → CardPackChoice → CardPackLoadout ─┐
  → Draft ────────────────────────────┤
      └──────────────────────── Planning
  → Victory 또는 Defeat
```

- Phase 1은 9웨이브다.
- 정상 런의 시작 선택지와 건설 가능 정의는 궁수 타워(`ballista`) 하나뿐이다. `initiallyUnlockedTowers`는 비어 있으며, 적 문맥·사망 문맥 타워 정의는 회귀 테스트에서만 명시적으로 사용한다.
- 전투 시작 시 모든 배치 타워의 장착 카드 ID와 카드 인스턴스 ID를 타워별 불변 프로그램 배열로 복사한다. 문서에서 이 웨이브 고정 복사본을 `ProgramSnapshot`이라 부른다.
- 전투 중 인벤토리가 바뀌지 않으므로 실행 중인 카드 배열은 절대 재조회하지 않는다.
- 웨이브의 모든 스폰 가계가 제거되거나 본진에 도달하고 예약 이벤트가 정리되면 웨이브가 종료된다.
- 웨이브 2·5·8은 일반 드래프트, 웨이브 3·6 보스는 보스 카드팩을 예약한다. 특수 몬스터팩, 보스팩, 일반 드래프트 순으로 모두 처리한 뒤 계획 단계로 간다.
- 마지막 웨이브의 모든 적과 예약 이벤트가 정리되면 추가 카드 보상 없이 `Victory`로 간다.
- 본진 체력이 0이 되면 아직 남은 이벤트와 관계없이 `Defeat`를 확정하고 전투 규칙 처리를 종료한다.
- `CardPackChoice`와 `CardPackLoadout`에서는 `Step`이 틱을 증가시키지 않는다.

## 6. 결정적 데이터 표현

- 시뮬레이션 주기는 초당 30틱이다.
- 시간과 재사용 대기시간은 정수 틱으로 저장한다.
- 체력, 피해, 거리, 속도는 정수 고정밀 단위로 저장한다.
- 배율과 확률은 10,000분율(basis point)로 저장한다.
- Phase 1 원본 JSON도 `durationTicks`, `intervalTicks`, milli, basis point 같은 정수 단위를 직접 저장한다. 초 단위 입력을 틱으로 바꾸는 별도 스키마는 아직 제공하지 않는다.
- 모든 엔티티는 런 안에서 재사용하지 않는 단조 증가 `EntityId`를 가진다.
- 컬렉션 순회가 결과에 영향을 주면 항상 `EntityId` 또는 명시된 정렬 키로 정렬한다.

동률 타깃 선택 기본 순서는 다음과 같다.

1. 표식 우선 여부
2. 선택 기준 거리가 가까운 적
3. 경로 진행도가 큰 적
4. `EntityId`가 작은 적

난수는 PCG32 계열의 독립 스트림을 사용한다.

- `CombatRandom`: 치명타와 전투 확률
- `WaveRandom`: 웨이브 변형과 스폰 변형
- `DraftRandom`: 카드 드래프트

한 스트림의 호출 수가 다른 스트림의 결과를 바꾸지 않는다. 확률 판정은 대상의 정규 처리 순서 안에서만 수행한다.

## 7. 핵심 상태 모델

### 적

적은 최소한 다음 규칙 상태를 가진다.

- 엔티티/콘텐츠/가계 ID
- 최대 및 현재 체력
- 방어력과 속성 저항
- 경로 진행 거리와 이동 방향
- 크기, 피격 반경, 이동 속도
- 제어 저항 게이지
- 상태이상 목록
- 보상 및 웨이브 기여도 할당량
- 사망 확정, 부활, 복제 여부

이동은 월드 Transform이 아니라 1차원 `PathProgress`로 계산한다. 밀치기, 공포, 역행, 환원, 순간이동은 이 값을 결정적으로 바꾼다. 화면 좌표는 향후 경로 어댑터가 스냅샷을 변환한다.

### 탄환

탄환은 최소한 다음 상태를 가진다.

- 엔티티, 원본 타워, 원본 발동 ID
- 위치/방향의 고정밀 좌표 또는 경로 샘플
- 피해, 속도, 크기, 충돌 반경, 남은 수명
- 남은 관통/도탄 수
- 이미 적중한 적 집합
- 프로그램 커서와 발동 바인딩
- RootChain, Generation, 복제/부활 토큰

### 타워

타워는 배치 지점, 콘텐츠 ID, 정의의 `levels` 범위 안에 있는 레벨, 선택한 `SubjectType`, 쿨다운, 트리거별 런타임 메모리, 장착 카드, 웨이브용 `ProgramSnapshot`을 가진다. `MaxLevel`, 슬롯 수, 연산력, 공격 수치는 레벨 테이블에서 파생하며 UI나 명령 처리에 별도 레벨 상수를 두지 않는다. 타워 로직은 카드의 효과를 직접 구현하지 않고 Trigger, SubjectType, SubjectSelector만 결정한다. 공격 타워가 적 해석을 선택하면 탄환은 적 해석 플래그를 들고 비행하고, 실제 충돌이 확정된 적에게만 프로그램을 실행한다.

타워 활성화는 `TowerTrigger`를 키로 하는 handler registry를 통과한다. 각 handler가 DispatchKind, SubjectTypeMode, SubjectSelector와 좁은 런타임 포트를 함께 소유하며, 콘텐츠의 실행 계약과 등록 시 교차 검증된다. 새 타워가 이미 지원되는 Trigger·Selector 조합을 사용하면 기존 타워나 카드 코드를 수정하지 않는다. 아직 구현되지 않은 `Alternating`, `Inherited` 또는 지원되지 않는 Trigger·Selector 조합은 콘텐츠 컴파일 시 즉시 거절해 런타임의 조용한 폴백을 금지한다.

## 8. 콘텐츠 파이프라인

JSON 논리 데이터와 밸런스 데이터가 런타임 수치의 단일 원본이다. ScriptableObject는 향후 에디터 편집 또는 JSON 생성 어댑터로만 사용한다.

```text
기본 phase 콘텐츠 JSON + Assets/Game/Data/Cards/**/*.json 모듈
  → 모듈 order/moduleId 기준 결정적 합성
  → 스키마 역직렬화
  → ID/참조/범위/양쪽 해석 검증
  → 문자열 ID를 정수 ID로 정규화
  → 정수 틱, milli, basis point, 방어적 복사 배열로 컴파일
  → CompiledContent
```

컴파일은 다음 오류를 시작 전에 차단한다.

- 중복 또는 존재하지 않는 ID
- 빈 탄환/적 해석
- 등록되지 않은 `EffectOperation`
- 티어/연산 비용/슬롯 비용 범위 오류
- 타워 슬롯 또는 연산력 위반
- 음수 시간, 체력, 보상
- 존재하지 않는 적, 웨이브, 상태 참조
- 비결정적 컬렉션을 요구하는 설정

컴파일 결과는 모든 활성 정의와 안전 한도, 런 설정을 포함하는 `ContentHash`를 가진다. 별도로 `RunConfig.DefinitionHash`가 시작 선택지, 초기 해금, 경로, 건설 지점, 드래프트 및 전투 공통값을 지문으로 만든다. 두 지문은 `ComputeStateHash()`에 포함되므로 같은 시드라도 콘텐츠나 런 설정이 다르면 같은 리플레이로 간주하지 않는다.

전체 58장과 18개 타워의 설계 계약은 각각 `CARD_RULES.md`, `TOWER_RULES.md`를 따른다. 현재 `CompiledContent`에는 활성 카드 58장과 타워 3종이 들어간다. 콘텐츠 컴파일은 양쪽 해석과 각 `EffectOperation` descriptor의 등록·매개변수 검증을 수행하고, `GameSimulation.Initialize`는 그 활성 집합의 executor 등록을 다시 검증한다.

기존 58장은 저장/리플레이 호환성을 위한 기본 카탈로그 순서를 유지한다. 이후 카드는 `CardContentModuleDto` 단위로 추가한다. `CardContentCatalogComposer`는 기본 카드를 먼저 보존한 뒤 모듈의 `order`, `moduleId` Ordinal 순서로 병합하며 입력 DTO를 바꾸지 않는다. 모듈 ID나 카드 ID가 비었거나 전체 집합에서 중복되면 일부만 로드하지 않고 합성 자체가 실패한다. Unity Editor는 카드 폴더의 JSON을 자동 발견해 `StageOnePresentationCatalog`에 직렬화하므로 Stage01과 TestLab이 별도 카드 목록을 소유하지 않는다.

Editor의 파일 발견 자체는 `RuleforgeTD.Editor.BuildTools` 어셈블리의 `CardContentModuleAssetDiscovery`가 소유한다. catalog import/prebuild 동기화와 headless scene builder가 이 공용 경계를 함께 사용하므로 named editor assembly가 predefined `Assembly-CSharp-Editor` 구현을 역참조하지 않는다.

모듈 카드는 기존 64-bit authored VFX 비트 ABI를 확장하지 않고 `visualStyleIndex: -1`을 사용한다. 합성 콘텐츠 등록 시 stable ID 기반 공용 스타일을 만들고, `CardExecuted`·`StatusApplied`·`EffectTriggered` 및 도탄 의미 사건만 `StageOneCardEffectVfxView`로 전달한다. 카드 ID는 로직과 표현 모두 Ordinal 비교하며, 카드 stable ID와 상태/파생 이벤트 alias는 타입별 조회 경계를 사용해 같은 문자열도 서로의 스타일을 가로채지 않는다. 의미 VFX는 `maxEffectsPerFrame`(현재 32)으로 제한하고 초과 사건은 시뮬레이션 결과를 바꾸지 않은 채 표현만 생략한다. 위치 스냅샷 캐시는 사건이 있을 때만 프레임당 한 번 만들고 같은 프레임에 새로 생긴 개체는 최신 스냅샷에서 보완 조회한다.

각 효과 연산의 등록 단위는 operation ID, 소유 module ID, 허용 SubjectType, 매개변수 validator, executor를 함께 소유한다. 기본 레지스트리는 Core → Common → Uncommon → Rare → Legendary → Mythic 모듈 순서로 한 번 조립하고 enum 전체가 정확히 한 번 등록됐는지 확인한 뒤 동결한다. 순수 `ContentCompiler`는 `IEffectOperationValidator`만 의존하고, `EffectContentCompiler` 조립 경계가 같은 기본 descriptor registry를 주입하므로 Content 계층이 Effects나 Simulation을 역참조하지 않는다. executor는 거대한 `GameSimulation` 공개 표면 대신 효과 실행에 필요한 포트를 사용한다. 이로써 새 고유 효과를 추가할 때 실행기·검증·문맥 지원을 한 모듈에서 함께 등록하고, 잘못된 탄환/적 문맥은 콘텐츠 로딩 중 차단한다.

고티어 카드는 다음 응집 경계를 지킨다.

- `GameSimulation.ProgramGrammar.cs`: 방향, 위력, 반복 번호, 실행 플래그와 패스 완료 조정만 소유한다.
- `GameSimulation.LegendaryCards.cs`: 전설 9종의 문법·지연 상태·최종 소멸 훅과 결정적 확률 정책을 소유한다.
- `GameSimulation.MythicCards.cs`: 신화 5종의 파생 개체, 공유 링크, 부활, 시간 이력과 반복 방문 집합을 소유한다.
- `HighTierCardEffectExecutors.cs`: registry에서 위 두 시뮬레이션 모듈로 전달하는 얇은 adapter만 둔다.

공용 전투 파일은 초기화, 고정 틱, 이동, 적중, 피해, 최종 생명주기와 정리 경계에서만 위 모듈의 좁은 훅을 호출한다. 카드별 Dictionary나 토큰을 공용 파일에서 직접 읽지 않는다. 새 분열·복제 생성 경로는 전설 상속 훅을, 새 부활·대체 탄환 경로는 전설·신화 생명주기 이전 훅을 반드시 호출한다.

카드의 심볼, 시각 스타일과 VFX 선택 키는 카드 콘텐츠/프레젠테이션 메타데이터에서 읽는다. GameLogic이나 UI에 안정 카드 ID별 switch를 두지 않으며, 순수 표현 키는 피해·보상·이벤트 순서를 결정하지 않는다.

### 다음 웨이브 예고 계약

`GameSimulation.GetUpcomingWaveForecast()`는 현재 웨이브 인덱스 다음의 컴파일된 스폰 목록과 최종 능력치를 `WaveForecastSnapshot`으로 반환한다. `WavePreviewModelFactory`는 이 스냅샷을 적 정의와 엘리트 특성 조합별로 묶는다. 고정 웨이브인 현재 콘텐츠에서는 이 목록이 계획 단계의 확정본이며 `StartWave()`도 같은 `CompiledWaveDefinition.SpawnsInternal`을 그대로 예약한다. 향후 무작위 웨이브가 추가되면 계획 단계 진입 시 결과를 한 번 확정해 이 동일한 목록에 저장해야 하며, UI용 재추첨 경로를 만들지 않는다.

`WaveEnemyStatResolver`는 기본 적 수치에 엘리트 체력·방어력·속도·보상·방어막·표현 크기 배율을 적용하는 유일한 계산 경계다. 웨이브 예고와 `SpawnEnemy()`가 이 결과를 함께 사용하므로 예고 체력, 방어력, 이동 속도, 방어막과 실제 생성 값이 계산식 수준에서 일치한다. 보스의 주기 소환은 예약 스폰 총합에 더하지 않고, 조건·소환 수·체력 배율·동시 상한을 보스 특수 능력 상세에 표시한다.

적별 `speedRatingKey`, 특징·능력·약점 키, 추천 카드 ID와 추천 태그 키, 엘리트 특성의 이름·접두사·설명·추천 카드·추천 태그는 콘텐츠 데이터가 소유한다. 엘리트 그룹은 기본 적 추천과 변형 추천을 중복 없이 합친다. 콘텐츠 컴파일은 추천 카드 참조와 중복을 검증하고, Runtime 초기화는 모든 표시 키의 현지화 완전성을 검증한다. 추천은 현재 보유/장착 상태를 강조하는 표현 정보일 뿐 카드 장착 명령의 허용 여부나 전투 판정을 바꾸지 않는다.

`WavePreview` 런타임 모듈은 `IWavePreviewLocalization`과 `IEnemyPreviewSpriteProvider`만 의존한다. `WavePreviewPresenter`가 모든 전투 스테이지의 표시 가능 단계, 입력 상태 기반 모델 캐시, 뷰 갱신을 소유하고 전투 컨트롤러는 현재 시뮬레이션 스냅샷만 전달한다. `WavePreviewView`는 계획 중 다음 웨이브 요약을 항상 표시하고 전투 중에는 그 다음 웨이브 요약을 유지한다. 요약 또는 그룹 버튼을 클릭·터치해야 상세가 열리며 hover에 의존하지 않는다. 일반/정예/보스 텍스트와 엘리트 아이콘을 함께 표시해 색상만으로 등급을 구분하지 않는다. 전투 중 상세는 읽기만 가능하고 기존 GameSimulation의 카드 편집 잠금 규칙을 우회하지 않는다. 현재 Stage 1·2·3은 같은 presenter와 view를 사용하며 스테이지별 차이는 콘텐츠·로컬라이제이션·프레젠테이션 공급자 데이터로만 주입한다.

### 엘리트 변형 표현 계약

엘리트는 별도 적 정의나 프리팹이 아니라 `CompiledEnemyDefinition + EliteTraitId[]` 조합이다. 현재 콘텐츠 컴파일은 특성 하나만 허용하지만 상태·스냅샷·예고 모델은 배열을 유지해 향후 복합 엘리트를 같은 경계에서 확장한다. 체력·방어력·속도·보상·방어막·렌더 크기는 `WaveEnemyStatResolver`에서 결정하며, 특성별 강화점과 약점이 동시에 없으면 콘텐츠 로딩을 거절한다.

`StageOneEnemyView`는 기본 몬스터 프리팹과 Animator를 그대로 풀링한다. `EliteEnemyVisualView`가 현재 `SpriteRenderer.sprite`를 매 프레임 외곽선 렌더러와 동기화하고 본체 tint를 적용하므로 Down/Up/Side 방향 및 Walk/Attack/Death/Special 애니메이션 모두 같은 팔레트 변형을 자동으로 공유한다. 방향별 이미지, AnimationClip, AnimatorController, 적 프리팹을 엘리트용으로 복제하지 않는다.

색상은 보조 정보다. 전장에서는 외곽선과 머리 위 문자 아이콘을 함께 표시하고, 체력바는 특성별 색과 결계 전용 방어막 막대를 사용한다. 웨이브 예고와 적 상세에는 아이콘, 이름 접두사, 특성 설명, 대응 힌트를 텍스트로 함께 제공한다. 사망 애니메이션이 끝날 때까지 같은 tint와 외곽선을 유지한다.

## 9. 시스템 처리 순서

`Submit`은 `Step` 밖에서 호출 즉시 처리된다. 전투 중 한 틱의 시스템 순서는 다음과 같이 고정한다.

1. 웨이브 예약 스폰 처리
2. 희귀·전설·신화의 시간 기반 런타임 갱신
3. 상태이상 갱신과 Status 단계 이벤트 처리
4. 적 이동, 경로 도착, Movement 단계 이벤트 처리
5. 공간 인덱스 재구축
6. 활성 영역과 타워 트리거 처리, Tower 단계 이벤트 처리
7. 탄환 이동/충돌과 Projectile 단계 이벤트 처리
8. Damage → Death → Reward 단계 이벤트 처리
9. 제거 대상 정리, 웨이브 종료 판정, Wave 단계 이벤트 처리
10. 틱 증가

세부 이벤트 순서와 연쇄 규칙은 `EFFECT_PIPELINE.md`를 따른다.

적 이동 직전 `MovementRestrictionEscapeSystem`이 각 개체의 경로 진행도와 이동 제한 범주를 갱신한다. 10초 장기 고정 뒤 1초 탈출 상태는 `EnemyState`에 정수 틱으로 저장하고 결정성 해시에 포함한다. 이 모듈은 상태 제거를 소유하지 않고 해당 이동 틱에서 감속·강제 정지·시간 정지 판정만 우회하므로 중앙 상태 처리 순서와 지속 피해 일정은 바뀌지 않는다.

주변 대상 검색은 틱 5에서 만든 결정적 공간 인덱스만 사용하고 카드별로 전체 적 배열을 매 프레임 훑지 않는다. `MaxEventsPerTick`에 도달하면 큐의 나머지 사건은 정렬 상태 그대로 다음 틱에 처리한다. 동시 적 상한에 도달한 예약 스폰도 `Spawned`와 `NextTick`을 진행시키지 않아 다음 틱으로 미룬다. 어느 경로도 초과 작업을 무작위 삭제하지 않는다.

복합 효과는 큐와 RootChain 예산을 먼저 함께 예약한다. 분열의 원본/자식 continuation, 폭발의 전체 대상 피해 묶음, 밀치기 충돌의 양쪽 피해 쌍뿐 아니라 재귀·과부하·우로보로스가 만든 전체 프로그램 패스도 일부만 적용되지 않도록 원자적으로 사전 예약한다. 방향·위력·반복 번호·재진입 억제 플래그는 불변 `ProgramExecutionSpec`으로 모든 continuation에 전달한다.

## 10. 출력 계약

`SimulationSnapshot`은 다음 읽기 모델을 제공한다.

- 틱, 런 상태, 현재 웨이브
- 본진 체력, 골드
- 타워의 정의 ID, 건설 지점, 논리 위치, 레벨, SubjectType, 슬롯별 카드 인스턴스
- 적의 가계, 경로 진행도, 위치, 체력, 방어력, 둔화/속도/크기 배율, 제어 게이지, 가지별 보상/진행도
- 탄환의 위치, 피해, 수명, 반경, 관통 사용량, 방향, 추적 여부와 바인딩 수
- 상태 인스턴스의 출처, 중첩, 강도, 남은 틱, 틱 간격, 방어 무시
- 소유 카드와 장착 상태, 현재 드래프트 제안, 해금된 타워 ID
- 카드팩 제안, 월드 카드팩 ID/위치, 보상 큐, 카드팩 진행률/다음 임계값, 즉시 장착 대기 카드 ID
- 가계별 최고 세대/전체 분열/생성/생존 수, 기본·최대·지급·몰수 보상, 진행도 정산량과 증액 키 수

별도 읽기 모델인 `WaveForecastSnapshot`은 웨이브 유형, 총수, 일반/정예/보스 수, 종류별 예약 수와 보스 보조 소환 동시 상한을 제공한다. `CombatTelemetrySnapshot`은 연쇄 통계, 합산 골드, 골드 카드 한도, 카드별 피해/발동을 제공한다. 두 모델은 권위 상태를 변경하지 않으며 예고와 결과 화면이 자체 수량을 다시 계산하지 않게 한다.

각 호출은 새 배열과 값 복사본을 반환하며, 이를 바꿔도 내부 시뮬레이션은 변하지 않는다. 진단 원형 버퍼는 `GameSimulation.Diagnostics`로 별도 공개한다.

`PresentationEvent`는 `EnemySpawned`, `EnemyDied`, `ProjectileSpawned`, `EnemyDamaged`, `StatusApplied`, `CardExecuted`, `RewardGranted`, `WaveStarted`, `SafetyLimitReached`와 특수 몬스터 생성, 카드팩 드롭·소멸·개봉, 보스 능력 예고·발동·단계 전환 같은 의미 사건만 전달한다. 보스 이벤트는 안정 콘텐츠 ID만 제공하고 VFX 이름, 스프라이트 파일명, 애니메이션 프레임은 포함하지 않는다.

## 11. 저장 계약

```csharp
public interface ISaveRepository
{
    void Save<T>(string key, T value);
    bool TryLoad<T>(string key, out T value);
    void Delete(string key);
}
```

Phase 1은 설정, 메타 해금, 최근 완료 런 기록을 저장할 수 있는 포트와 `DataVersion`만 정의한다. 저장 구현은 게임 로직 바깥에 둔다. 전투 중간 상태 저장 및 이어하기는 지원하지 않는다.

## 12. 단계별 완료 기준

Phase 1 로직은 다음을 모두 만족해야 한다.

- 에셋과 씬 없이 9웨이브를 끝까지 실행할 수 있다.
- 12장 카드가 두 해석을 모두 가지며 3개 타워에서 실행된다.
- 같은 시드와 명령 로그의 매 틱 상태 해시가 일치한다.
- 카드 순서 차이가 상태 해시와 전투 결과를 바꾼다.
- 분열, 사망 폭발, 상태이상 연쇄가 예산 안에서 끝난다.
- 분열/복제/부활로 골드와 웨이브 기여도가 증가하지 않는다.
- 새 카드 정의는 기존 타워 코드를, 새 타워 정의는 기존 카드 코드를 수정하지 않고 추가할 수 있다.
