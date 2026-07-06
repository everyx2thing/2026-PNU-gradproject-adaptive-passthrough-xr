# Meta Quest 기반 센서 Feature 및 위험도 계산 프로토타입

본 프로젝트는 졸업과제 **Context-aware Adaptive Passthrough Framework** 중, Meta Quest 환경에서 센서 Feature와 정적 경계 기반 위험도를 계산하는 Unity 프로토타입입니다.

현재 구현은 Scene API로 수집한 Room Scene의 벽 정보와 HMD/컨트롤러 움직임 데이터를 이용하여 머리 속도, 가속도, 각속도, 손 움직임, 벽 접근 속도, TTC, 시선-경계 각도 등을 계산합니다. 이를 바탕으로 `Rcollision`과 `Rstate`를 산출하고, 최종 위험도 `Rtotal`을 통해 Passthrough 활성화 여부를 판단합니다.

현재는 실제 Passthrough 제어 전 단계로, 계산 결과와 `Passthrough Decision: ON/OFF`를 UI에 표시하여 위험도 계산식과 실험용 계수 조정 구조를 검증하는 것을 목표로 합니다.

## 현재 구현된 기능

- Scene API 기반 Room Scene 벽 정보 수집
- HMD와 컨트롤러 움직임 Feature 계산
- 벽까지의 거리, 접근 속도, 접근 가속도 계산
- 시선-경계 각도 기반 `Rblind` 계산
- `Rcollision`, `Rstate`, `Rtotal` 계산
- `Rtotal` 기반 Passthrough ON/OFF 판단 결과 UI 표시

현재는 실제 Passthrough를 직접 제어하지 않고, 판단 결과만 UI에 표시합니다.

## 실행

1. Unity Hub에서 프로젝트 열기
2. `Assets/Scenes/SampleScene.unity` 실행
3. Android 플랫폼으로 전환 후 Quest 기기에 Build & Run
4. (앱 실행 전) 헤드셋에서 Space Setup(Room Setup) 완료
5. 실행 후 UI에서 거리, 움직임 Feature, 위험도, Passthrough 판단 결과 확인

## Status

- 구현 완료: `Rcollision`, `Rstate`, `Rtotal` 계산 및 UI 표시
- 진행 중: 실제 Quest 헤드셋 실험을 통해 `safeDistance`, `safeTime`, `maxApproachAccel`, 위험도 가중치, `passthroughOnThreshold` 등 실험용 계수 조정
- 미구현: 실제 Passthrough 제어, `Rdynamic`, `Rintent`, ML 개인화, 다양한 시각화 방식

## Details

자세한 구현 내용은 [PROTOTYPE_DETAILS.md](unity-client/PROTOTYPE_DETAILS.md)를 참고하세요.
