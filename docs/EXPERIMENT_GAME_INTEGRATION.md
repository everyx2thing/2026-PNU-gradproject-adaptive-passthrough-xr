# Adaptive 보존형 실험 게임 통합

## 구성

- 실제 실행 Scene은 `Assets/Scenes/SampleScene.unity` 하나다.
- 게임은 `Assets/Prefabs/Experiment/ExperimentGameRoot.prefab` 인스턴스 하나로 들어간다.
- `ExperimentGameTest.unity`는 원본 배치 참고용이며 빌드에는 포함하지 않는다.
- 카메라 리그, EventSystem, OVRManager, passthrough layer, 정적·동적 정책,
  사람 추론, ML 개인화와 logger는 기존 `SampleScene`의 인스턴스를 그대로 쓴다.

게임 Prefab을 다시 생성해야 할 때 Unity 메뉴의
`Tools > Experiment > Rebuild Integrated Game Prefab`을 실행한다. 빌더는 원본
Scene에서 게임 오브젝트만 추출하고 현재 `SampleScene`을 교체하지 않는다.

## 조건 동작

| 조건 | 커스텀 안전 출력 | Guardian 요청 | 측정·추론·ML·로그 |
|---|---|---|---|
| A `NoPassthrough` | 억제 | 숨김 | 계속 실행 |
| B `GuardianDefault` | 억제 | 표시 | 계속 실행 |
| C `Adaptive` | 사용자 설정 유지 | 숨김 | 계속 실행 |

조건 전환은 컴포넌트의 `enabled`나 PlayerPrefs를 바꾸지 않는다. 라운드 종료,
메뉴 복귀 또는 컨트롤러 비활성화 시 presentation과 boundary override만 기본
정책으로 복구한다. B 조건은 실제 Guardian suppression이 1초 안에 해제되지
않으면 라운드를 시작하지 않고 운영자 상태 문구를 표시한다.

## 라운드 좌표와 입력

- 9개 Spawn Point는 라운드 시작 시점 HMD 위치와 수평 yaw에 다시 정렬된다.
- 원본 마커의 수평 오프셋만 사용하고 높이는 HMD 높이와 schedule offset을
  사용하므로 참가자 키와 시작 위치가 달라도 궤적 기준이 유지된다.
- 총은 `RoundActive`일 때만 reticle, raycast와 trigger 발사를 허용한다.
- 라운드 중 게임 메뉴는 `CanvasGroup`으로 숨기며 진단 패널은 계속 동작한다.
- 라운드 종료와 중단 시 발사체, coroutine, 효과와 비영속 override를 정리한다.

## 데이터와 검증

- 기존 JSONL `schemaVersion=2`를 유지한다.
- 각 레코드에 condition, round ID/sequence, presentation/boundary override,
  Guardian 요청·실제 suppression 상태가 추가된다.
- EditMode는 조건 매핑, 점수 규칙, 72개 schedule, 필수 Prefab 참조, Missing
  Script, `SampleScene` singleton 구성을 검사한다.
- PlayMode는 A/B/C를 순서대로 전환하면서 presentation/boundary controller가
  비활성화되지 않고 종료 후 사용자 설정으로 복원되는지 검사한다.

Quest 최종 시험에서는 B 조건의 실제 Guardian 표시 가능 여부와 A/B/C 임의
순서, C 조건의 기존 정적·사람·ML·피드백 기능, 게임 최대 부하의 72 FPS를
별도로 확인해야 한다.
