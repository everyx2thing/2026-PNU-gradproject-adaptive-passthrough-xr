# 공간 측정·사람 추적 품질 개선 구현 및 검증

## 구현 구조

정적 위험 입력은 `ISpatialObstacleProvider`로 분리했다. Quest에서는
`QuestSpatialObstacleProvider`가 Environment Depth와 Room Scene을 같은
주기에 측정하고, 거리 일치·신뢰도·self-body 검증을 거쳐 융합한다. 한
공급자만 유효하면 그 결과를 사용하므로 Room Setup과 룸스케일 경계는
기능의 선행 조건이 아니다.

측정 루프는 머리에는 ray와 근접 overlap 검사를, 양손에는 전방 ray를
적용한다. 손 overlap을 0m로 합성하는 경로는 사용하지 않는다.
ray hit는 근거리 클러스터, 분산, 표본 수로 신뢰도를 계산하고 median과 시간
기반 smoothing을 거쳐 기존 `StaticBoundaryRiskFrame` 어댑터로 전달된다.
0.25m 이하 또는 확인된 머리 overlap은 정적 가중 위험도를 우회하며, 기존 전체
Boundaryless 및 passthrough 최소 1.5초 유지 정책은 그대로 보존한다.

사람 추적은 카메라 capture timestamp와 pose를 관측값에 보존한다. 기본
13개 중심·몸통 가중 depth sample을 사용하고 불일치 다음 프레임만 25개로
확장한다. 동적 jump gate는 `0.35m + 2.5m/s * dt`이고, 박스가 커지는데
깊이가 멀어지는 경우 `Receding`을 확정하지 않는다. 추적 연결은 예측 IoU,
중심, 크기, 신뢰도와 선택적 world point를 사용하는 Hungarian 전역
최소비용 매칭이다. 1.2초 누락 동안 ID를 보존하고 신뢰 가능한 world point를
현재 HMD 시점으로 매 프레임 재투영한다.

초근접 사람은 다음 조건 중 하나로 강제 위험 상태에 진입한다.

- depth confidence 0.45 이상이며 거리 0.60m 이하
- detection confidence 0.55 이상이며 박스 높이 0.75 이상 또는 면적 0.35 이상

해제는 거리 0.80m 초과와 박스 높이 0.65 미만이 3회 연속일 때만 이루어진다.

## 품질 프리셋

| 프리셋 | 공간 측정 | 최대 ray | 사람 추론 목표 |
|---|---:|---:|---:|
| Balanced | 20Hz | 12 | 3Hz |
| Accuracy | 30Hz | 24 | 3Hz |
| Performance | 10Hz | 6 | 3Hz |

선택은 `PlayerPrefs`에 저장되며 기존 설정 초기화 버튼으로 함께 삭제된다.
Balanced에서 프레임 p95가 13.9ms를 2초간 넘으면 10Hz/6 ray/3Hz까지
단계적으로 감속하고, 5초 안정되면 단계적으로 복구한다. Quest 첫 실행에서는
CPU와 GPUCompute를 같은 표본 수로 측정한다. p95 13.9ms 이하 후보 중 추론
median이 빠른 backend를 고르며 0.1ms 이내 동률은 CPU를 선택한다.

## 패널과 로그

헤드셋 패널의 `Tracking Quality` 카드에서 세 프리셋을 즉시 바꾸고 공간
source/distance/confidence, 사람 ID와 raw/filtered distance, motion과 missing
시간, 추론·공간 Hz/ms, FPS/p95를 확인한다. `OBJECT APPROACH`,
`PERSON APPROACH`, `PERSON RECEDE` 버튼은 로그에 시나리오 marker를 남긴다.

JSONL 로그는 기존 필드를 유지하고 `schemaVersion=2`와 다음 additive 필드를
추가한다.

- 실제 프리셋과 유효 측정·추론 빈도/시간
- 공간 source, sample count, dispersion, age
- 사람 raw/filtered distance, bbox-depth conflict, missing seconds
- ID handoff와 테스트 marker
- FPS와 전체 프레임 p95

## Unity 양안 Presentation Lab

`TeamVR/Adaptive Passthrough/Create or Replace Mock Test Scene` 메뉴로
`Assets/Scenes/DynamicRiskMock.unity`를 재생성한다. Play Mode에서는 IPD
0.064m의 512×512 좌·우 카메라 결과가 나란히 표시된다. 벽 정면/사선,
사람 접근·횡단·0.75초 가림, 낮은 상자·의자·계단, floor stale과 Depth
0.5초/장기 누락을 버튼으로 재생할 수 있다. 적색은 production과 동일한
world-corner/SDF 결과이고 청록 외곽은 기대 투영이다. IPD, HMD 이동·회전,
벽 거리·yaw, policy ON/OFF, 재생·일시정지·frame step도 즉시 조절한다.

자동 PlayMode 검증은 좌·우 eye의 외곽 projection 오차 2px 이내, HMD 이동
중 world geometry 고정, 낮은 장애물 patch와 0.20m→0.45m floor corridor를
검사한다. EditMode는 12.5% padding, 상단 82% 사람 capsule, 6.5% feather
공유 셰이더 경로, 125/100/300/1500/300ms 상태 전이를 검증한다. 실제 OVR
compositor와 렌즈를 통한 초점 편안함만 Quest 최종 확인 항목으로 남는다.

## Quest A/B 절차

1. `TeamVR/Adaptive Passthrough/Build Quest 3 APK`로 development APK를 만든다.
2. Room Scene 설정이 있는 상태와 없는 상태에서 각각 실행한다.
3. 책상·의자·상자에 느리게/빠르게 접근한다.
4. 사람 전후 이동, 횡단, 0.75초 가림, 0.60m 이내 접근을 수행하고 각 marker를 누른다.
5. 5분 연속 실행한 뒤 JSONL과 Android logcat을 회수한다.
6. 패널에서 72FPS, p95 13.9ms 이하, 추론 2.8Hz 이상, 공간 측정 20Hz 이상을 확인한다.

물리 거리 정확도와 초근접 표시 지연은 marker 구간을 실제 측정 거리·영상과
대조해 판정한다. Editor 테스트만으로 Quest 센서 오차나 72FPS 합격을
확정해서는 안 된다.
