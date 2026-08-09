# AI Balance Pipeline

## 권위 경계

CLI 정책이 보는 것은 공개 `SimulationSnapshot`, 공개 콘텐츠 지식, 현재 합법 행동 목록뿐이다. 행동 목록의 각 항목에는 안정적인 `actionId`가 있고, 실제 `GameCommand`는 CLI가 소유한다. 정책이나 LLM이 비용·피해·슬롯 규칙을 재계산하거나 임의 command payload를 만들 수 없다.

```text
content JSON + difficulty overlay
  → actual content compiler
  → GameSimulation
  → Snapshot + authoritative legal actions
  → deterministic policy (release gate) or LLM selector (exploration)
  → GameCommand / CommandResult / Step
  → replay + telemetry + aggregate report
```

## 폐쇄 루프

1. `current` 프로필을 Train seed에서 모든 기준 정책으로 측정한다.
2. Easy, Medium, Hard 순서로 목표와 실제 지표의 차이를 계산한다.
3. 가장 큰 원인을 최대 3개 선택한다.
4. optimizer 또는 Balance Director가 최대 5개 데이터 변경을 제안한다.
5. `BalanceProposalValidator`가 source hash, old value, JSON pointer, 변경 폭, 구조 변경 여부를 검사한다.
6. 승인 가능한 candidate만 메모리에서 생성한다.
7. 동일 seed·동일 정책으로 before/after를 비교한다.
8. 현재 CLI optimizer는 선택한 한 난이도의 penalty가 줄어든 후보만 Validation seed로 승격한다. 세 난이도 회귀는 별도의 release evaluation에서 확인하며, 이를 통과하지 않은 후보는 최종 승인으로 간주하지 않는다.
9. 정책·목표·프로필·프롬프트·seed set·난이도별 인덱스를 고정하고 `policy-lock.json`과 증거 해시를 확정한다.
10. 아직 노출되지 않은 Holdout 128을 한 번 실행하고 이후 수치를 변경하지 않는다.

거절 후보와 이유도 산출물에 남긴다. 실패 seed, Timeout, 예외, safety 오류를 제거하거나 목표 파일과 정책을 optimizer가 수정해서는 안 된다. 현재 optimizer 목적함수의 범위와 자동 교차 난이도 승인 부재는 `BALANCE_LIMITATIONS.md`에 명시한다.

## 증거 승격 계층

- Train 64는 프로필 탐색, 카드 strength/coverage, ordered pair/triple discovery에만 쓴다.
- Validation 64는 후보 승인과 세 난이도 strict 회귀에 쓴다. Bootstrap index로 통과한 결과는 release 증거로 승격하지 않는다.
- Holdout 128은 모든 입력을 freeze한 뒤 미사용 seed로 최종 1회만 실행한다. Holdout을 본 뒤 수정했다면 그 증거는 무효며, 최종 평가에는 새로운 미사용 seed 128개가 필요하다.
- `optimize` 산출물의 `ValidationApproved` 단독은 선택한 난이도에 대한 국소 증거다. 세 난이도 strict Validation 회귀를 통과하기 전에는 최종 승인으로 표현하지 않는다.

현재 discovery 산출물은 `seed-sets.json` 전체 파일 해시와 matched sample 수를 저장하지만, 선택 seed-set 이름·정확한 prefix·순서 fingerprint는 저장하지 않는다. Strict evaluator도 인덱스의 seed hash를 현재 파일과 대조하지 않는다. 따라서 seed 선별·재배열 방지는 아직 구조적으로 보장되지 않으며 실행 명령, 실제 `n`, 동결 입력 스냅샷을 함께 보존해야 한다.

## LLM 플레이어

LLM 플레이어는 새 전략을 찾는 정성 탐색용이다. 입력에는 command가 제거된 합법 행동 요약만 들어가며 출력은 다음처럼 제한된다.

```json
{
  "selectedActionId": "a-101",
  "reasonCode": "MIDWAVE_DAMAGE_BREAKPOINT"
}
```

어댑터는 단일 JSON 객체, 알려진 필드, 짧은 reason code, 유한 evidence metric을 검사하고 `selectedActionId`가 현재 legalActions에 정확히 존재할 때만 결정을 반환한다. Easy에는 시너지 지수를 숨기고, Medium에는 단독 성능만, Hard에는 단독 성능과 시너지 지수를 제공한다. 숨겨진 RNG와 미래 spawn은 전달하지 않는다.

## Balance Director

Director는 aggregate report, optional before/after report, 고정 목표, 허용 JSON pointer만 입력받는다. 응답의 `proposalId`와 `sourceProfileHash`는 모델이 정하지 않고 호출자가 신뢰된 source에서 붙인다. 최종 제안은 반드시 `BalanceProposalValidator`를 통과해야 하며, 어댑터 자체는 프로필을 쓰거나 적용하지 않는다.

자동 적용 기본 제한은 한 반복 최대 5개 필드, 일반 수치 ±10%, spawn 간격 ±15%, 적 수 ±2다. 카드 정체성, 승패 조건, 본진 체력, 정책, seed, safety limit, GameLogic 변경은 자동 범위 밖이다. `needsStructuralReview` 제안은 기록할 수 있지만 자동 적용하지 않는다.

## 재현성과 증거

각 런은 콘텐츠/프로필/시나리오/정책 버전과 두 seed, 모든 command/result, phase 전환, Step 순서, 최종 해시를 저장한다. Batch는 Wilson 95% 구간, 체력 분위수, 실패 웨이브, 경제, 누출, 타워·카드 선택, mid-wave build, 명령 거절과 safety 지표를 계산한다. 수치 결론은 생성된 JSON 권위 원장을 근거로 작성하며, CSV는 평탄화된 분석용 파일, Markdown은 요약으로 취급한다.
