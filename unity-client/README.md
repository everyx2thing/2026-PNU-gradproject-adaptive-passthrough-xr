# Meta Quest 기반 센서 Feature 및 위험도 계산 프로토타입

본 프로젝트는 졸업과제 **Context-aware Adaptive Passthrough Framework** 중, Meta Quest 환경에서 센서 Feature와 환경 기반 충돌 위험도를 계산하는 Unity 프로토타입입니다.

이 프로젝트는 Meta Quest 헤드셋을 쓰고 있을 때, 사용자가 실제 공간의 벽이나 물건에 부딪힐 것 같으면
자동으로 카메라 화면(Passthrough)을 켜서 주변을 보여주는 안전 기능을 실험하는 Unity 프로토타입입니다.

## 동작 방식

1. 헤드셋과 양손 컨트롤러의 움직임을 매 프레임 측정합니다 — 벽까지 거리, 다가가는 속도/가속도,
   시선이 벽 쪽을 보고 있는지 등.
2. 이 값들로 "지금 벽에 부딪힐 위험이 얼마나 되는지"를 0~1 사이 점수로 계산합니다.
   - 머리/몸 쪽 위험도: `R_static_head`
   - 손 쪽 위험도: `R_static_hand` (팔이 실제로 닿을 수 있는 거리에 있을 때만 반영됨)
3. 머리가 얼마나 빠르게 움직이는지를 `UserState`(0=정적~1=동적) 값으로 계산해서, 위 위험도
   점수가 "얼마나 되어야 Passthrough를 켤지"의 기준(민감도)을 조절합니다.
4. 위험도가 기준을 넘으면 실제로 Passthrough를 켜고, 그 판단 과정 전체가 화면 UI에도 표시됩니다.



## 현재 구현된 기능

- Scene API로 방(Room Scene)의 벽 위치/방향 정보 수집
- 헤드셋·컨트롤러 움직임 계산 (이동 속도, 접근 가속도, 손 속도 등)
- 벽까지의 거리, 접근 속도, **TTC**(Time-To-Collision, 지금 속도로 계속 다가가면 몇 초 후 부딪히는지)
- 시선이 벽을 등지고 있는지(사각지대) 반영한 위험도 `Rblind`, 이를 합산한 `R_static_head` 산출
- 팔 길이를 기준으로 "그 손이 지금 그 벽에 실제로 닿을 수 있는가"를 판별(reach gate)하는
  손 위험도 `R_static_hand` 산출
- 머리 이동 속도 기반 `UserState`(0~1 연속값, 정적↔동적) 계산
- `R_static_head` / `R_static_hand` / 벽에 너무 가깝고 빠르게 다가가는 긴급 상황, 3가지 조건 중 하나라도
  만족하면 Passthrough를 켜는 판단 로직 (한 번 켜졌다가 바로 꺼지는 걸 막는 완충 구간 포함)
- 위험 수준을 3단계로 나타내는 `WarningLevel`(`None`/`Aware`/`Full`) 계산
- 판단 결과에 따라 실제 Passthrough 화면 전환 및 UI 실시간 표시

> **`WarningLevel`과 Passthrough ON/OFF의 관계**: `WarningLevel`이 `Full`인 경우에만 Passthrough가
> 켜지고, `None`/`Aware`인 경우에는 Passthrough가 켜지지 않습니다. `Aware`는 아직 화면을 바꾸지는
> 않지만 잠재 위험도가 쌓이고 있다는 신호이며, 향후 화살표나 가장자리 빛 표시 같은 경량 UI로
> 구현할 예정입니다 (현재는 `CurrentWarningLevel` 등 값만 계산·노출되고, 실제로 그려주는 UI는
> 아직 없습니다).

## Requirements

- Unity **6000.4.2f1** (Unity 6)
- Meta Quest 계열 헤드셋 (Quest 2 / 3 / 3S / Pro), 개발자 모드 활성화
- 헤드셋에서 **Space Setup(Room Setup)을 먼저 완료**해야 함 (Scene API로 벽 정보를 가져오기 때문)
- Android Build Support 모듈

## 실행

1. 프로젝트 폴더를 Unity Hub에서 열기
2. `Assets/Scenes/SampleScene.unity` 실행
3. Android 플랫폼으로 전환 후 Quest 기기에 Build & Run
4. (앱 실행 전) 헤드셋에서 Space Setup(Room Setup) 완료 (첫 실행 시 SCENE 권한 승인 필요)
5. 실행 후 화면에 벽 거리 및 모션 피처(왼쪽)와 사용자 상태·위험도·손 위험도 및 Passthrough 판단 결과(오른쪽)가
   실시간 출력됨

## Status

- 구현 완료: `R_static_head`(환경 기반 충돌 위험도), `UserState`(머리 이동 속도 기반 0~1 연속값), 팔 길이 기반 손 위험도(`R_static_hand`), 3가지 경로 히스테리시스 기반 Passthrough ON/OFF 판단, `WarningLevel`(`None`/`Aware`/`Full`) 산출, 실제 Passthrough 레이어 제어 및 UI 표시
- 수정 가능: 실제 Quest 헤드셋 실험을 통해 `safeDistanceMeters`, `safeTimeSeconds`, `maxApproachAccel`, 각 위험도 가중치, `stableOnThreshold`/`rapidOnThreshold`/`hysteresisWidth`, `handFullThreshold` 등 실험용 계수 조정
- 미구현: `WarningLevel.Aware`를 실제로 그려주는 화살표/가장자리 UI 레이어, 로그 수집 기반 ML 개인화 연동, 다양한 시각화 방식(Directional Passthrough, Augmented Virtuality 등) 비교 실험

## Details

자세한 구현 내용은 [PROTOTYPE_DETAILS.md](PROTOTYPE_DETAILS.md)를 참고하세요.
