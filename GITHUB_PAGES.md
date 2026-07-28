# GitHub Pages 자동 배포

`main` 브랜치에 푸시하면
`.github/workflows/unity-webgl-pages.yml`이 Stage 01을 Unity WebGL로
빌드하고 GitHub Pages에 배포한다.

공개 주소:

```text
https://wl39.github.io/NAN-Ruleforge-TD/
```

## 빌드 기준

- Unity 버전: `ProjectSettings/ProjectVersion.txt`
- 현재 버전: `2022.3.62f2`
- 빌드 타깃: `WebGL`
- 빌드 메서드:
  `RuleforgeTD.Editor.AssetImport.CraftPixFieldTilemapAssetBuilder.BuildWebGLFromCommandLine`
- 배포 디렉터리: `Builds/WebGL/Stage01`
- Pages 배포 방식: GitHub Actions artifact

빌드 메서드는 Stage 01 생성 에셋을 검증하고, 압축을 비활성화한 WebGL
산출물을 원자적으로 교체한다. 워크플로는 `index.html`, loader,
framework, data, wasm 파일이 모두 생성됐는지 확인한 뒤에만 배포한다.

## 필요한 Actions 시크릿

GitHub 저장소의 `Settings → Secrets and variables → Actions`에 다음
Repository secret을 등록한다.

- `UNITY_LICENSE`: Unity Personal 수동 활성화로 받은 라이선스 파일 전체
- `UNITY_EMAIL`: Unity 계정 이메일
- `UNITY_PASSWORD`: Unity 계정 비밀번호

시크릿 값은 저장소 파일이나 로그에 기록하지 않는다.

세 시크릿 중 하나라도 없으면 워크플로는 경고를 남기고 빌드와 배포를
건너뛴다. 이 경우 현재 공개 중인 Pages 버전은 그대로 유지된다.

시크릿을 처음 등록한 뒤에는 Actions의
`Build and deploy Unity WebGL` 워크플로를 수동 실행하거나 `main`에
새 커밋을 푸시한다.

## 동시 실행 정책

Pages 배포는 저장소별 단일 큐로 직렬화한다. 여러 푸시가 연달아 발생하면
진행 중인 배포를 강제로 취소하지 않고, 마지막 커밋까지 순서대로 처리해
최종 공개 버전이 최신 `main`과 일치하게 한다.
