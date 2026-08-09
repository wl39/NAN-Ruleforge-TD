# Card content modules

이 폴더 아래의 `*.json` 카드 모듈은 Unity가 자동으로 발견해 Stage01과
카드 VFX 갤러리에 함께 등록한다. 기존 효과 연산으로 카드를 조합하는 경우
전투 코드, VFX 팔레트 배열, 갤러리 목록을 따로 수정하지 않는다.

필수 규칙:

- `schemaVersion`은 현재 `1`이다.
- `moduleId`와 카드 `id`는 전체 카탈로그에서 고유해야 한다.
- `order`, 그 다음 `moduleId` 순서로 기본 카드 뒤에 병합된다.
- 모든 카드는 projectile/enemy 해석과 현지화 문자열을 모두 제공한다.
- 모듈 카드는 `visualStyleIndex: -1`을 사용한다.
- 전용 VFX가 없는 카드는 stable ID와 티어로 결정적인 대체 VFX가 생성된다.
- 전용 VFX를 추가하면 같은 stable ID의 authored palette가 자동 우선한다.

카드 JSON을 추가·수정·삭제하면 `StageOnePresentationCatalog`가 다음 Editor
tick에 동기화된다. VFX 갤러리 WebGL 빌드는 이 병합 카탈로그를 다시 읽어
카드 수, 패널, 티어 라벨, 재생 목록과 화면 높이를 자동 생성한다.
