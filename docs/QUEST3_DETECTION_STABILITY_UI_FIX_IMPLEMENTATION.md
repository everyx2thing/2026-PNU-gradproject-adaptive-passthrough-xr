# Quest 3 사람 검출·사용자 상태·UI 안정화 구현 결과

작성일: 2026-07-28  
대상: `unity-client/`  
기준 문서: `QUEST3_DETECTION_STABILITY_UI_FIX_SPEC.md`

## 1. 구현 상태

제작문서에서 정의한 다음 항목을 Unity 프로젝트와 `SampleScene`에 반영했다.

- YOLO 출력 계약을 `CenterXYWH + ModelPixels`로 명시
- bbox 유효성 검사, confidence 필터, NMS 후처리 분리
- 단일 프레임 검출과 확정된 사람 트랙을 분리
- 확정된 트랙만 동적 위험도와 사람 수에 사용
- HMD 속도·가속도·각속도에 시간 기반 EMA 적용
- 사용자 상태 전환에 진입 지연과 해제 히스테리시스 적용
- 겹치던 기존 상태 Text를 비활성화하고 단일 TMP HUD로 통합
- 개발용 bbox 오버레이의 상단 패널과 헤더 제거
- 사람 검출 및 사용자 상태의 진단 로그 추가
- Quest 3 APK 메뉴 빌드와 설치 경로 추가

## 2. 사람 bbox 후처리

구현 파일:

- `Assets/Scripts/AdaptivePassthrough/Core/PersonDetectionPostProcessorSettings.cs`
- `Assets/Scripts/AdaptivePassthrough/Core/PersonDetectionPostProcessor.cs`
- `Assets/Scripts/AdaptivePassthrough/Quest/QuestPersonDetectionRunner.cs`

적용값:

| 항목 | 값 |
|---|---:|
| 좌표 형식 | `CenterXYWH` |
| 좌표 단위 | 모델 입력 pixel |
| confidence | 0.55 |
| NMS IoU | 0.45 |
| NMS 최대 후보 | 50 |
| 최종 최대 검출 | 10 |
| 최소 화면 노출 비율 | 0.15 |
| 최대 정규화 폭·높이 | 2.0 |
| 추론 주기 | 10 Hz |

처리 순서는 다음과 같다.

1. 원본 좌표의 `NaN`과 무한대 검사
2. 모델 계약에 맞는 corner 좌표 변환
3. 역전되거나 면적이 없는 원본 bbox 제거
4. pixel 좌표를 정규화
5. 화면과 실제로 교차하는지 검사
6. 화면에 보이는 비율 검사
7. 마지막 단계에서만 화면 영역으로 clip
8. person class와 confidence 필터
9. confidence 내림차순 NMS
10. 최종 검출 수 제한

잘못된 좌표를 먼저 `Clamp01`하여 정상 bbox처럼 만드는 동작은 제거했다.

## 3. 다중 프레임 사람 확정

구현 파일:

- `Assets/Scripts/AdaptivePassthrough/Core/SimpleObjectTracker.cs`
- `Assets/Scripts/AdaptivePassthrough/Core/DynamicRiskModels.cs`
- `Assets/Scripts/AdaptivePassthrough/Core/DynamicRiskPipeline.cs`

트랙 상태는 `Tentative → Confirmed → Lost → Removed`로 관리한다.

- 일반 확정: 최근 5프레임 중 3프레임 이상 매칭
- 빠른 확정: confidence 0.85 이상으로 2프레임 연속 매칭
- 임시 미검출 유지: 0.5초
- 사람 수와 `Rdynamic`: `Confirmed` 트랙만 사용
- 한 프레임 오검출: 사람 수와 위험도에 반영하지 않음

## 4. 사용자 움직임 상태 안정화

구현 파일:

- `Assets/Scripts/AdaptivePassthrough/Core/UserMotionStateFilter.cs`
- `Assets/Scripts/QuestRiskExperimentLogger.cs`

필터값:

| 항목 | 값 |
|---|---:|
| 속도 EMA 시간 상수 | 0.35초 |
| 가속도 EMA 시간 상수 | 0.45초 |
| 각속도 EMA 시간 상수 | 0.35초 |
| Static 진입 유지 | 0.75초 |
| Static 해제 유지 | 0.25초 |
| Agitated 진입 유지 | 0.25초 |
| Agitated 해제 유지 | 0.75초 |
| HUD 갱신 | 5 Hz |

EMA 계수는 프레임 수가 아닌 실제 `deltaTime`으로 계산한다. 따라서 72 Hz와
90 Hz에서도 유사한 전환 시간을 유지한다. 한 프레임의 위치 spike만으로
`Agitated`가 되지 않으며 상태가 실제로 변경될 때만 `[UserMotion]` 로그를
남긴다.

## 5. HUD 정리

구현 파일:

- `Assets/Scripts/QuestRiskHud.cs`
- `Assets/Scripts/AdaptivePassthrough/Unity/DynamicRiskDebugOverlay.cs`
- `Assets/Editor/AdaptivePassthroughSceneBuilder.cs`
- `Assets/Scenes/SampleScene.unity`

`SampleScene`의 기존 `DistanceText`, `RiskText`는 삭제하지 않고 비활성화했다.
대신 `QuestRiskHudPanel/SummaryText` 한 곳에서 다음 네 줄을 5 Hz로 갱신한다.

```text
Camera: READY | Room: READY
Candidates: raw 0 | NMS 0 | People 0
Rdynamic: 0.00 | User: Static | Rstate: 0.00
Rtotal: 0.00 | Safe | Passthrough: ON
```

개발용 bbox 라벨은 유지하지만 전체 화면 상단 패널과 중복 헤더를 제거했다.
사람 수는 raw bbox 개수가 아니라 확정된 트랙 수를 표시한다.

## 6. 자동 검증 결과

Unity Test Runner EditMode 전체 결과:

```text
Total: 25
Passed: 25
Failed: 0
Skipped: 0
```

검증 범위:

- center/corner 및 pixel/normalized bbox 변환
- 잘못된 좌표를 clip 전에 제거
- 동일 bbox 5개의 NMS 단일화
- 1프레임 오검출 미확정
- 3/5 프레임 및 고신뢰 2프레임 확정
- Lost 트랙의 0.5초 유지와 만료
- tentative 트랙의 동적 위험도 제외
- 정지 노이즈, 단일 spike, 지속 이동, 빠른 회전 상태 전환
- 72 Hz와 90 Hz의 상태 전환 일관성
- `SampleScene`의 단일 HUD와 중복 Text 비활성화

## 7. Quest 3 실기기 확인 결과

기기:

```text
Serial: 2G97C5ZH9800D4
ADB state: device
Model: Quest 3
Package: com.pnu.teamvr.adaptivepassthrough
```

2026-07-28 빌드를 설치하고 자동 실행했다. 시작 로그에서 다음을 확인했다.

- OpenXR 세션이 `FOCUSED` 상태까지 진입
- 모델 입력: `[1,3,640,640]`
- 모델 출력: boxes `(8400,4)`, classes `(8400)`, scores `(8400)`
- bbox 계약: `CenterXYWH`, `ModelPixels`
- confidence 0.55, NMS IoU 0.45
- 사람 없는 현재 화면의 진단 10회에서 최종 `output=0`
- 크래시, `NullReferenceException`, `FATAL EXCEPTION` 없음
- 사용자 상태가 지속 조건을 거쳐 `Agitated → Dynamic → Static`으로 전환

이번 실행은 설치와 초기 동작을 확인한 smoke test다. 아래 항목은 착용자가
Quest 화면을 보면서 추가로 완료해야 한다.

- 손 흔들기 60초에서 confirmed people 0 유지
- 실제 사람 1명과 2명 검출률 및 중복 트랙 확인
- 접근·정지·후퇴에 따른 `Rdynamic` 변화 확인
- HUD 및 bbox 라벨의 실제 시야 내 겹침 확인
- 30분 연속 실행 안정성 확인

## 8. 산출물

APK:

```text
unity-client/Builds/Android/AdaptivePassthrough.apk
Size: 168,453,977 bytes
SHA-256: E7D3509B281BCD2244ED4CC59CE4AC62C01968BE9296386F313E35A1A74C0E8F
```

Unity 메뉴:

```text
TeamVR > Adaptive Passthrough > Build Quest 3 APK
```

재설치 및 실행:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Tools\Install-Quest3.ps1 `
  -Serial 2G97C5ZH9800D4 `
  -Launch
```

