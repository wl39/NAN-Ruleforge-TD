# Ruleforge TD 게임 로직 아키텍처

## 1. 문서 목적과 범위

이 문서는 Ruleforge TD의 전투 및 런 진행 로직이 따라야 하는 구조적 계약을 정의한다. `AGENTS.md`가 제품 설계의 최상위 원본이며, 이 문서는 그중 Phase 0~1 게임 로직을 구현 가능한 형태로 구체화한다.

현재 실행 가능한 `phase1-content.json`은 카드 32장(Common 14장, Uncommon 18장), 타워 3종, 적 7종, 9웨이브를 컴파일한다. `CARD_RULES.md`의 58장과 `TOWER_RULES.md`의 18종은 전체 게임을 위한 설계 계약이며, Phase 1 런타임에 모두 등록되어 있다는 뜻이 아니다.

현재 범위에 포함되는 항목은 다음과 같다.

- 카드, 타워, 적, 탄환, 상태이상
- 고정 틱 전투 시뮬레이션
- 이벤트 큐와 연쇄 예산
- 피해, 보상, 웨이브, 드래프트
- 명령 입력, 상태 스냅샷, 표현 이벤트
- 설정과 완료된 런을 위한 저장 포트
- Stage01 고정 건설 지점 입력, 런타임 HUD와 스냅샷 기반 전투 표현
- 적·탄환·상태 파티클 오브젝트 풀과 WebGL Stage01 빌드

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

`StageOneBattleController`는 건설 지점과 uGUI 입력을 `GameCommand`로 바꾸고, 30Hz 누적 시간에 맞춰 `GameSimulation.Step()`을 호출한다. 일시정지는 Step 호출을 멈추고, 2배속은 같은 실제 시간에 두 배의 고정 틱을 처리한다. Unity `Time.timeScale`은 애니메이션과 파티클 표현 속도만 동기화하며 판정 수치의 원본으로 사용하지 않는다.

`StageOneEnemyView`, `StageOneProjectileView`, 타워 프리팹은 `SimulationSnapshot`을 읽는 대리자다. 적과 탄환은 풀에서 재사용하며, 화상·중독 연출도 `EnemySnapshot.StatusDetails`를 읽을 뿐 지속시간이나 피해를 직접 갱신하지 않는다. `StageOneGameplaySceneInstaller`가 Stage01 씬, 프레젠테이션 카탈로그, 한국어 임시 UI 데이터를 멱등적으로 연결한다.

Stage01에서는 타워를 클릭하면 별도의 소형 장착 패널이 열린다. 이 패널은 선택 타워의 레벨, 해금 슬롯, 보유 카드, 탄환/적 해석을 스냅샷에서 읽고 모든 변경을 `GameCommand`로 보낸다. 화면에 보이는 슬롯 잠금만으로 규칙을 대신하지 않으며, 시뮬레이션도 레벨별 슬롯 수를 다시 검증한다.

`StageOneCameraController`는 Terrain Tilemap의 실제 렌더 경계를 기준으로 최대 줌아웃을 계산한다. 화면비가 달라져도 카메라 사각형이 맵 밖으로 나가지 않으며, 휠은 포인터 중심 줌, 가운데 버튼은 경계 안 패닝을 담당한다. Stage01에서는 Pixel Perfect Camera의 크기 덮어쓰기를 비활성화한다. WebGL은 `RuleforgeFullscreen` 템플릿을 사용해 캔버스를 브라우저 뷰포트 전체로 맞추고 기본 푸터와 페이지 여백을 두지 않는다.

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
| `UpgradeTower` | Planning, CardPackLoadout | 레벨 1~7 범위 |
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
- 시작 선택지는 `ballista`, `mutation_obelisk` 두 종이다. 하나를 선택하면 `death_engine`도 `initiallyUnlockedTowers`에 의해 소유 목록에 들어가 계획 단계에서 배치할 수 있다.
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

타워는 배치 지점, 콘텐츠 ID, 1~7 레벨, 선택한 `SubjectType`, 쿨다운, 트리거별 런타임 메모리, 장착 카드, 웨이브용 `ProgramSnapshot`을 가진다. 타워 로직은 카드의 효과를 직접 구현하지 않고 Trigger, SubjectType, SubjectSelector만 결정한다. 공격 타워가 적 해석을 선택하면 탄환은 적 해석 플래그를 들고 비행하고, 실제 충돌이 확정된 적에게만 프로그램을 실행한다.

## 8. 콘텐츠 파이프라인

JSON 논리 데이터와 밸런스 데이터가 런타임 수치의 단일 원본이다. ScriptableObject는 향후 에디터 편집 또는 JSON 생성 어댑터로만 사용한다.

```text
원본 JSON
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

전체 58장과 18개 타워의 설계 계약은 각각 `CARD_RULES.md`, `TOWER_RULES.md`를 따른다. Phase 1 `CompiledContent`에는 활성 카드 32장과 타워 3종만 들어간다. 콘텐츠 컴파일은 양쪽 해석을 검증하고, `GameSimulation.Initialize`는 그 활성 집합의 executor 등록을 다시 검증한다. 후속 카드는 실제 JSON과 executor가 추가된 Phase부터 런타임 검증 대상이 된다.

## 9. 시스템 처리 순서

`Submit`은 `Step` 밖에서 호출 즉시 처리된다. 전투 중 한 틱의 시스템 순서는 다음과 같이 고정한다.

1. 웨이브 예약 스폰 처리
2. 상태이상 갱신과 Status 단계 이벤트 처리
3. 적 이동, 경로 도착, Movement 단계 이벤트 처리
4. 공간 인덱스 재구축
5. 활성 영역과 타워 트리거 처리, Tower 단계 이벤트 처리
6. 탄환 이동/충돌과 Projectile 단계 이벤트 처리
7. Damage → Death → Reward 단계 이벤트 처리
8. 제거 대상 정리, 웨이브 종료 판정, Wave 단계 이벤트 처리
9. 틱 증가

세부 이벤트 순서와 연쇄 규칙은 `EFFECT_PIPELINE.md`를 따른다.

복합 효과는 큐와 RootChain 예산을 먼저 함께 예약한다. Phase 1에서 분열의 원본/자식 continuation, 폭발의 전체 대상 피해 묶음, 밀치기 충돌의 양쪽 피해 쌍은 일부만 적용되지 않도록 원자적으로 사전 예약한다.

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
