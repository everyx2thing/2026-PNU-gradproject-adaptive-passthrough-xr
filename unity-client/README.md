# Context-aware Adaptive Passthrough Framework  
Meta Quest 기반 XR 안전 실험 프로토타입

## Overview

본 프로젝트는 Meta Quest와 Unity를 기반으로, 사용자의 움직임과 실제 공간의 벽 정보를 분석하여 Passthrough 활성화 여부를 판단하는 실험용 프로토타입입니다.

현재는 졸업과제 보고서에서 제안한 전체 구조 중 정적 경계 충돌 위험도 `Rcollision`과 사용자 상태 위험도 `Rstate`를 중심으로 구현되어 있습니다.

## Current Implementation

현재 구현된 기능은 다음과 같습니다.

- Scene API 기반 Room Scene 벽 정보 수집
- HMD와 컨트롤러 움직임 Feature 계산
- 벽까지의 거리, 접근 속도, 접근 가속도 계산
- 시선-경계 각도 기반 `Rblind` 계산
- `Rcollision`, `Rstate`, `Rtotal` 계산
- `Rtotal` 기반 Passthrough ON/OFF 판단 결과 UI 표시

현재는 실제 Passthrough를 직접 제어하지 않고, 판단 결과만 UI에 표시합니다.

## Quick Start

1. Unity Hub에서 프로젝트 열기
2. `Assets/Scenes/SampleScene.unity` 실행
3. Android 플랫폼으로 전환 후 Quest 기기에 Build & Run
4. 헤드셋에서 Space Setup(Room Setup) 완료
5. 실행 후 UI에서 거리, 움직임 Feature, 위험도, Passthrough 판단 결과 확인

## Status

- 구현 완료: `Rcollision`, `Rstate`, `Rtotal` 계산 및 UI 표시
- 진행 중: 실제 Quest 헤드셋 실험을 통해 `safeDistance`, `safeTime`, `maxApproachAccel`, 위험도 가중치, `passthroughOnThreshold` 등 실험용 계수 조정
- 미구현: 실제 Passthrough 제어, `Rdynamic`, `Rintent`, ML 개인화, 다양한 시각화 방식

## Details

자세한 구현 내용은 [PROTOTYPE_DETAILS.md](docs/PROTOTYPE_DETAILS.md)를 참고하세요.
