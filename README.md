# 몰입형 XR을 위한 상황 인식 기반 Adaptive Passthrough Framework (Context-aware Adaptive Passthrough Framework for Immersive XR)

> 정적·동적 위험 분석과 ML 개인화를 결합한 Meta Quest 3 기반 XR 안전 시스템

---

# 1. 프로젝트 배경

## 1.1. 국내외 시장 현황 및 문제점

XR(Extended Reality) 기술은 가상현실(VR), 증강현실(AR), 혼합현실(MR)을 포함하는 기술로, 다양한 산업과 일상 환경으로 활용 범위가 확대되고 있다. XR 시장은 2021년 약 190억 달러에서 2026년 약 1,008억 달러 규모로 성장할 것으로 전망되며, 연평균 39.7%의 높은 성장률이 예상된다.

특히 HMD(Head-Mounted Display)를 사용하는 몰입형 VR 환경에서는 사용자가 가상 콘텐츠에 집중하는 동안 실제 주변 환경에 대한 시각적 인지가 제한되어 벽이나 가구와 같은 주변 환경을 인지하지 못하고 충돌하거나, 주변 사람에게 피해를 주는 안전사고가 발생할 수 있다. National Electronic Injury Surveillance System(NEISS) 데이터를 기반으로 VR 관련 부상 사례를 분석한 연구에 따르면, VR 관련 응급실 방문 추정 건수는 2017년 125건에서 2021년 1,336건으로 증가했으며, 해당 기간 동안 352% 증가한 것으로 나타났다.또한 해당 연구에서는 VR 사용 중 발생하는 주요 부상 원인으로 주변 사물과의 충돌 및 VR 사용자의 움직임으로 인해 VR 컨트롤러 등에 주변 사람이 맞는 Bystander Injury, 즉 주변인에 대한 2차 피해를 제시했다.

기존 VR 기기들은 Passthrough라는 카메라 영상으로 주변 환경을 실시간으로 보여주는 기능을 활용해 물리적 충돌을 방지한다. 대표적으로 Meta Quest의 Guardian은 사용자가 설정한 플레이 영역의 경계에 접근하면 Passthrough 화면을 띄워 위험을 알리는 방식이다. 본 프로젝트는 이 Passthrough 기능을 확장하여, 화면 전체가 아닌 위험이 감지된 영역에만 선택적으로 현실 화면을 노출한다.

그러나 기존 경계 기반 시스템은 다음과 같은 한계를 가진다.

- 사전에 설정된 경계까지의 거리를 중심으로 위험을 판단한다.
- 사용자의 현재 이동 속도나 움직임 상태를 충분히 반영하지 않는다.
- 사람과 같이 위치가 변화하는 동적 객체를 직접적인 위험 요소로 판단하기 어렵다.
- 협소한 공간에서는 불필요한 경고가 반복적으로 발생할 수 있다.
- 사용자별 행동 특성이나 위험 인지 차이를 반영하기 어렵다.
- 위험이 발생했을 때 전체 화면을 현실 환경으로 전환하는 방식은 몰입감을 저하시킬 수 있다.

따라서 XR 환경에서는 단순히 고정된 경계를 표시하는 것을 넘어, 사용자의 움직임과 주변 환경을 실시간으로 분석하고 현재 발생하는 위험의 위치와 정도에 따라 적응적으로 대응하는 안전 시스템이 필요하다.

---

## 1.2. 필요성과 기대효과

VR 환경에서 안전을 확보하기 위해 현실 환경을 지속적으로 노출하면 주변 환경에 대한 인지는 향상되는 반면에 가상 환경에 대한 몰입감이 저하될 수 있다. 반대로 현실 환경의 노출을 최소화하면 몰입감은 유지할 수 있지만 실제 장애물이나 주변 사람에 대한 인지가 늦어질 수 있다. 본 프로젝트는 이러한 안전성과 몰입감 사이의 균형 문제를 해결하기 위해 상황 인식 기반 Adaptive Passthrough Framework를 개발한다.

본 시스템은 각 HMD 및 컨트롤러 정보를 실시간으로 분석하고 분석된 위험 정보를 기반으로 위험이 발생한 영역에만 선택적으로 Passthrough를 노출한다. 전체 화면을 현실 환경으로 전환하는 것이 아니라 위험한 방향과 객체 주변만 국소적으로 노출하여 나머지 시야의 몰입을 유지한다.

이를 통해 다음과 같은 효과를 기대한다.

1. VR 사용 중 물리적 충돌 위험에 대한 인지를 향상한다.
2. 불필요한 Passthrough 노출을 줄여 몰입감 저하를 최소화한다.
3. 정적 장애물뿐만 아니라 움직이는 사람에 대한 위험에도 대응한다.
4. 사용자 로그를 기반으로 위험 판단 기준을 점진적으로 개인화한다.
5. 향후 시각·청각·촉각을 결합한 멀티모달 안전 시스템으로 확장할 수 있는 기반을 마련한다.

---

# 2. 개발 목표

## 2.1. 목표 및 세부 내용

본 프로젝트의 목표는 Meta Quest 3 기반 몰입형 XR 환경에서 사용자의 안전과 몰입감을 동시에 확보하는 상황 인식 기반 Adaptive Passthrough Framework를 개발하는 것이다. 이를 위해 다음과 같은 기능을 개발한다.

### 2.1.1. 정적 경계 위험 분석

벽, 가구, 낮은 장애물과 같은 정적 환경을 인식하고 사용자와 장애물 사이의 위험도를 계산한다.

주요 위험 요소는 다음과 같다.

- 사용자와 장애물 사이의 거리
- 장애물 방향으로의 접근 속도
- TTC(Time to Collision)
- HMD 위치 및 움직임
- 양손 위치 및 움직임
- 사용자 이동 상태

정적 위험은 머리, 양손, 낮은 장애물 등의 채널로 구분하여 판단한다. 또한 히스테리시스와 긴급 상황 오버라이드를 적용하여 위험이 임계값 주변에서 반복적으로 발생하거나 해제되는 현상을 줄인다.

### 2.1.2. 동적 객체 위험 분석

Quest 3의 Passthrough 카메라를 이용하여 주변 사람을 검출하고 추적한다.

```text
Passthrough Camera
        ↓
      YOLOv9
        ↓
  Person Detection
        ↓
 Object Tracking
        ↓
Distance / Approach / TTC / Path
        ↓
 Dynamic Risk
```

동적 위험은 사람과의 거리, 접근 속도, TTC, 이동 경로 등을 기반으로 계산한다.

### 2.1.3. 정적·동적 위험의 독립적인 판단

정적 환경과 동적 객체는 입력 데이터의 특성과 반응해야 하는 시간 스케일이 서로 다르기 때문에 각각 독립적으로 위험을 판단한다.

```text
Static Risk
     ↓
Static Passthrough Policy Controller
     │
     ├─────────────────────┐
                           ↓
              Selective Passthrough Controller
                           ↑
     ┌─────────────────────┘
     │
Dynamic Passthrough Policy Controller
     ↑
Dynamic Risk
```

통합된 Risk Snapshot은 로그 기록과 개인화 feature 계산에 활용하며, 정적 위험과 동적 위험은 각각의 정책 컨트롤러를 통해 Passthrough 출력에 반영한다.

### 2.1.4. 선택적 Passthrough

위험이 발생한 전체 시야를 현실 환경으로 전환하지 않고 위험 영역에만 Passthrough 창을 생성한다.

- 정적 위험 → 벽·가구 방향의 사각형 Surface Window
- 동적 위험 → 사람 주변의 상체 Capsule Window
- 낮은 장애물 → 바닥 안내선
- 후방 위험 → 방향 진동
- 보조 경고 → 붉은 테두리 및 컨트롤러 햅틱

이를 통해 안전을 확보하면서도 불필요한 현실 환경 노출을 줄인다.

### 2.1.5. ML 기반 개인화

사용자의 Passthrough 활성화 로그를 이용하여 개인별 위험 판단 민감도를 조정한다.

```text
Quest Logs
    ↓
Session / Event Extraction
    ↓
7-Dimensional Features
    ↓
Random Forest
    ↓
ONNX
    ↓
Unity Sentis
    ↓
Threshold Adjustment
```

개인화 모델은 PC에서 학습하고 ONNX 형식으로 변환한 후 Unity Sentis를 이용하여 Quest에서 추론한 후 위험도 계산에 직접 반영된다.

---

## 2.2. 기존 서비스 대비 차별성

본 프로젝트는 기존 기기들이 사용하는 거리뿐만 아니라 사용자의 움직임과 동적 객체까지 고려하여 상황에 따라 위험도를 계산하고 위험 영역에만 선택적으로 Passthrough를 제공한다.

| 구분             | 기존 시스템              | 본 프로젝트                     |
| -------------- | ------------------- | -------------------------- |
| 위험 판단 기준       | 사전 설정 경계까지의 거리      | 거리 + 접근 속도 + TTC 기반 연속 위험도 |
| 대응 대상          | 주로 정적 경계            | 정적 경계 + 동적 객체              |
| 사용자 상태 반영      | 제한적                 | HMD 및 컨트롤러 움직임 반영          |
| 동적 위험          | 직접적인 동적 객체 판단 없음    | 사람 검출·추적 및 동적 위험도 계산       |
| 제어 방식          | 경계 접근에 따른 경고        | 규칙 기반 위험 판단 + ML 기반 개인화    |
| Passthrough 범위 | 경계 또는 전체적인 현실 환경 노출 | 위험 영역만 국소적으로 노출            |
| ML 기반 개인화            | 동일한 기준 적용           | 사용자 로그 기반 점진적 개인화          |
| 사용자 개입         | 경계 설정 필요            | 시스템이 상황을 자동 판단             |

특히 본 프로젝트는 ML이 안전 판단을 직접 대체하는 방식이 아니라 규칙 기반 안전 로직을 기본 안전망으로 유지하면서 ML을 통해 개인별 판단 임계값을 조정하는 구조를 사용한다. 이를 통해 ML 모델의 예측 오류가 발생하더라도 기본적인 안전 규칙이 유지되도록 설계한다.

---

## 2.3. 사회적 가치 도입 계획

본 프로젝트는 VR 사용 중 발생할 수 있는 물리적 충돌과 주변인에 대한 2차 피해 가능성을 고려하여 XR 환경의 물리적 안전성을 향상하는 것을 주요 사회적 가치로 둔다. 특히 VR 사용자의 안전뿐만 아니라 주변 사람과 실제 공간까지 고려하여 위험을 판단한다는 점에서 기존 사용자 중심 안전 시스템의 범위를 확장한다. 또한 불필요한 현실 환경 노출을 줄이는 선택적 Passthrough 방식을 통해 안전성과 몰입감을 동시에 확보하는 것을 목표로 한다.

향후에는 현재의 시각적,진동 기반 피드백을 3D Spatial Audio 및 추가적인 Haptic Feedback으로 확장하여 다양한 감각을 활용한 멀티모달 안전 경고 시스템으로 발전시킬 수 있다.

---

# 3. 시스템 설계

## 3.1. 시스템 구성도

```text
┌────────────────────────────────────────────┐
│                  Meta Quest 3              │
│                                            │
│   HMD / Controller / Room Scene / Camera  │
│                  / Depth                   │
└──────────────────────┬─────────────────────┘
                       │
                       ▼
┌────────────────────────────────────────────┐
│            Hardware & Input Layer          │
│                                            │
│  Spatial Information / User Movement Data  │
│       Passthrough Camera / Depth Data      │
└───────────────┬────────────────┬───────────┘
                │                │
                ▼                ▼
┌────────────────────────┐ ┌────────────────────────┐
│ Static Risk Pipeline   │ │ Dynamic Risk Pipeline  │
│                        │ │                        │
│ Wall / Furniture       │ │ YOLOv9 Person         │
│ Low Obstacle           │ │ Detection              │
│ Distance               │ │ Object Tracking        │
│ Approach Velocity      │ │ Distance / TTC         │
│ TTC                    │ │ Approach / Path        │
└────────────┬───────────┘ └───────────┬────────────┘
             │                         │
             ▼                         ▼
┌──────────────────────┐   ┌──────────────────────┐
│ Static Policy        │   │ Dynamic Policy       │
│ Controller           │   │ Controller            │
└────────────┬─────────┘   └────────────┬─────────┘
             │                          │
             └────────────┬─────────────┘
                          ▼
              ┌────────────────────────┐
              │ Selective Passthrough  │
              │ Controller              │
              │                        │
              │ Surface Window         │
              │ Capsule Window         │
              │ Visual / Haptic Alert  │
              └────────────┬───────────┘
                           │
                           ▼
                    Meta Quest 3
```

시스템은 크게 하드웨어 입력 계층, 정적 위험 분석 엔진, 동적 위험 분석 엔진, 정책 제어 계층, 선택적 Passthrough 출력 계층으로 구성된다.

정적 위험과 동적 위험은 각각 독립적으로 분석한 후 정책 컨트롤러를 통해 최종 Passthrough 표현으로 연결된다.

---

## 3.2. 사용 기술

| 구분                    | 기술                                               |
| --------------------- | ------------------------------------------------ |
| HMD                   | Meta Quest 3                                     |
| Engine                | Unity 6                                          |
| Unity Version         | 6000.4.2f1                                       |
| Language              | C# / Python                                      |
| XR                    | Meta XR SDK                                      |
| Spatial Understanding | Meta XR / Room Scene / EnvironmentRaycastManager |
| Computer Vision       | YOLOv9                                           |
| Object Tracking       | SimpleObjectTracker                              |
| ML Inference          | Unity Sentis                                     |
| Machine Learning      | Random Forest                                    |
| ML Framework          | scikit-learn                                     |
| Model Format          | ONNX                                             |
| Testing               | Unity EditMode / pytest                          |
| Camera                | Quest 3 Passthrough Camera API                   |

---

# 4. 개발 결과

## 4.1. 전체 시스템 흐름도

전체 시스템은 다음 순서로 동작한다.

```text
[Quest Sensor / Camera]
          ↓
[환경 및 사용자 데이터 수집]
          ↓
 ┌────────┴─────────┐
 ↓                  ↓
[정적 위험 분석]   [동적 위험 분석]
 ↓                  ↓
거리/속도/TTC      사람 검출/추적
 ↓                  ↓
Static Risk        Dynamic Risk
 ↓                  ↓
Static Policy      Dynamic Policy
 └────────┬─────────┘
          ↓
[Selective Passthrough]
          ↓
[위험 영역만 현실 환경 노출]
          ↓
[시각 / 진동 피드백]
```

각 위험 분석 경로는 측정 → 위험 추정 → 위험 판단 → 경고 유지 및 해제 → Passthrough 표현의 순서로 처리된다.

---

## 4.2. 기능 설명 및 주요 기능 명세서

### 4.2.1. 정적 경계 위험 분석

| 항목    | 내용                               |
| ----- | -------------------------------- |
| 입력    | Room Scene, 공간 표면, HMD 위치, 양손 위치 |
| 주요 요소 | 거리, 접근 속도, TTC, 사용자 상태           |
| 출력    | 정적 위험도 및 위험 영역                   |
| 대응    | Surface Window, 바닥 안내선, 진동       |
| 안전장치  | 히스테리시스, 긴급 Passthrough           |

정적 위험도는 사용자와 주변 정적 환경 사이의 거리와 움직임을 기반으로 계산한다.

---

### 4.2.2. 동적 객체 위험 분석

| 항목    | 내용                        |
| ----- | ------------------------- |
| 입력    | Passthrough 카메라 영상, 깊이 정보 |
| 객체    | 사람                        |
| 검출    | YOLOv9                    |
| 추적    | SimpleObjectTracker       |
| 위험 요소 | 근접, 접근, TTC, 경로           |
| 출력    | 동적 위험도                    |
| 대응    | 사람 주변 Capsule Window      |
| 안전장치  | Force Passthrough         |

동적 객체가 사용자에게 접근하는 경우 거리와 접근 속도를 기반으로 위험도를 계산하며, 매우 가까운 상황에서는 안전을 위해 Passthrough를 강제로 노출할 수 있도록 설계한다.

---

### 4.2.3. 선택적 Passthrough

위험 유형에 따라 서로 다른 형태의 Passthrough를 제공한다.

```text
정적 위험
    ↓
벽 / 가구 방향
    ↓
Rectangular Surface Window


동적 위험
    ↓
사람 위치
    ↓
Capsule Window
```

정적 위험 영역에는 사각형 Surface Window를, 동적 사람 영역에는 상체 중심의 Capsule Window를 오버레이한다.

추가적으로 낮은 장애물에 대한 바닥 안내선과 후방 위험에 대한 진동 피드백을 제공한다.

---

### 4.2.4. ML 기반 개인화

사용자의 Passthrough 활성화 기록을 기반으로 개인별 위험 판단 임계값을 조정한다. Feature를 기반으로 Random Forest 모델을 학습하고 ONNX로 변환한 뒤 Unity Sentis에서 추론한다.

개인화 모델은 기본적으로 Shadow Mode를 사용하며, 신규 사용자에게 충분한 로그가 확보되지 않은 경우 기본 임계값을 유지하는 Cold-start 방식을 선택했다. 더불어 사용자의 안전을 위해서 조정 가능한 범위를 ± 10%로 지정한다.

---

### 4.2.5. 사용자 실험

본 프로젝트에서는 총 12명(N=12)을 대상으로 세 가지 조건을 비교하는 사용자 실험을 수행했다. 개인화 모듈은 사용자 로그가 축적되어야 하는 적응형 요소이므로 짧은 세션과 작은 표본으로 구성된 본 비교실험에서는 통제 변인으로 포함하지 않았다.

| 조건      | 설명            |
| ------- | ------------- |
| Round 1 | Guardian      |
| Round 2 | 정적 위험 인식      |
| Round 3 | 정적 + 동적 위험 인식 |

안전감과 몰입감을 7점 Likert 척도로 측정하고 Friedman 검정 및 사후 Wilcoxon 검정을 수행했다.

### 실험 결과

| 지표  | Round 1 | Round 2 | Round 3 |
| --- | ------: | ------: | ------: |
| 안전감 |    4.50 |    5.45 |    5.73 |
| 몰입감 |    4.51 |    4.32 |    4.03 |

#### 안전감

- Friedman: χ² = 10.59, p = .005
- Kendall's W = .44
- Round 1 vs Round 2: p = .005
- Round 1 vs Round 3: p = .004
- Round 2 vs Round 3: p = .141

안전감은 라운드 간 유의한 차이를 보였다. 특히 Guardian 조건인 Round 1보다 정적 위험 인식 조건인 Round 2와 정적, 동적 위험 인식 조건인 Round 3에서 안전감이 유의하게 높았다.

#### 몰입감

- Friedman: χ² = 2.13, p = .345
- Kendall's W = .089

몰입감은 라운드 간 유의한 차이를 보이지 않았다.

따라서 본 실험에서는 Guardian 대비 정적 및 정적+동적 위험 인식 조건에서 안전감이 유의하게 높아졌으며 해당 안전감 향상이 몰입감의 유의한 감소와 함께 나타났다는 근거는 확인되지 않았다.

---

## 4.3. 디렉토리 구조

```text
.
├─ unity-client/
│  └─ Quest 3에서 실제로 빌드·실행되는 Unity 6 / C# 앱
│
├─ ml-dynamic-object/
│  └─ 동적 객체(사람) 위험도 Python 참조 구현 및 테스트
│
├─ ml-personalization/
│  └─ 세션 로그 → Feature → Random Forest → ONNX 개인화 파이프라인
│
└─ docs/
   └─ 설계 및 구현 스펙 및 보고서
```
---

## 4.4. 산업체 멘토링 의견 및 반영 사항

본 프로젝트에서는 KT 책임연구원 안종길 전문가의 자문을 반영하여 위험도 통합 구조를 변경했다.

기존 설계에서는 다음과 같은 가중합을 통해 전체 위험도를 계산했다.

```text
Rtotal =
wstatic · Rstatic
+ wstate · Rstate
+ wdynamic · Rdynamic
+ wintent · Rintent
```

그러나 전문가 자문 과정에서 단순 가중평균 방식은 특정 위험 요소가 매우 높더라도 다른 위험 요소의 낮은 값에 의해 최종 위험도가 희석될 수 있다는 문제가 제기되었다. 또한 정적 환경과 동적 객체는 입력 데이터의 특성과 반응해야 하는 시간 스케일이 서로 다르기 때문에 하나의 위험도로 통합하는 것보다 독립적으로 판단하는 것이 적절하다고 판단했다.

이에 따라 최종 시스템에서는 다음과 같이 변경했다.

```text
Before

Static Risk ─┐
             ├─▶ Rtotal ─▶ Passthrough
Dynamic Risk ┘


After

Static Risk  ─▶ Static Policy  ─┐
                                ├─▶ Selective Passthrough
Dynamic Risk ─▶ Dynamic Policy ─┘
```

이를 통해 정적 위험과 동적 위험이 서로의 위험도를 희석하지 않고 독립적으로 판단하도록 변경했다.

---

# 5. 설치 및 실행 방법

## 5.1. 설치절차 및 실행 방법

---

## 5.2. 오류 발생 시 해결 방법

---

# 6. 소개 자료 및 시연 영상

## 6.1. 프로젝트 소개 자료

```markdown
![포스터](docs/02.포스터/TeamVR_포스터.pdf)
``
```markdown
![발표 자료](docs/03.발표자료/TeamVR_발표자료.pdf)
``

---

## 6.2. 시연 영상
[![2026 전기 졸업과제 08 TeamVR](https://img.youtube.com/vi/Xw21HWSOwO8/0.jpg)](https://youtu.be/Xw21HWSOwO8)   

---

# 7. 팀 구성

## 7.1. 팀원별 소개 및 역할 분담

### Team VR

| 팀원      | 역할                | 주요 담당                                                                                |
| ------- | ----------------- | ------------------------------------------------------------------------------------ |
| **따다소** | 개인화 모델 및 사용자 실험   | Random Forest 기반 On-device 추론, PC 기반 개인화 모델 재학습, 사용자 실험 설계/진행, 비모수 통계 분석, 보고서 및 발표자료 제작 |
| **최아영** | 정적 위험도 및 실험 게임    | 정적 경계 기반 위험도 알고리즘, 정적 Passthrough 제어, 실험용 VR 게임 개발                                   |
| **이승주** | 동적 위험 인식 및 시스템 통합 | 사람 검출·추적, 동적 위험도 계산, 선택적 Passthrough 시각화, 시각·진동 피드백, 전체 시스템 통합                       |

---

## 7.2. 팀원 별 참여 후기

### 따다소

아이디어를 실제로 동작하는 시스템으로 만드는 과정이 생각보다 훨씬 복잡하다는 것을 느꼈다. 시행착오는 많았지만 그만큼 배운 것도 많았던, 뜻깊은 프로젝트였다.

### 최아영

TBA

### 이승주

TBA

---

# 8. 참고 문헌 및 출처

## 연구 및 산업 자료

1. 김성진. *확장현실(XR) 산업의 현황과 과제*. KIET 산업경제, 산업연구원, 2023.

2. *Development and Validation of the Collision Anxiety Questionnaire for VR Applications*. Proceedings of the 2024 CHI Conference on Human Factors in Computing Systems.

3. Cucher, D. J., Kovacs, M. S., Clark, C. E., & Hu, C. K. P. (2023). *Virtual reality consumer product injuries: An analysis of national emergency department data*. Injury, 54(5), 1396–1399.

   - Data Source: National Electronic Injury Surveillance System (NEISS)
   - 2017년 125건 → 2021년 1,336건
   - 해당 기간 352% 증가

## 기술 문서

4. Meta. *Meta Quest에서 패스스루를 사용하는 방법*.

5. Meta for Developers. *Passthrough Camera API Overview*. Meta Horizon Documentation.

6. Meta Platforms, Inc. *Create engaging experiences with anchoring improvements and multi-room support*. Meta Horizon Developer Blog, 2024.

7. Apple Inc. *Important Safety Information for Apple Vision Pro*. 2024.

## 관련 연구

8. Breiman, L. (2001). *Random Forests*. Machine Learning, 45(1), 5–32.

9. Probst, P., Wright, M. N., & Boulesteix, A.-L. (2019). *Hyperparameters and tuning strategies for random forest*. WIREs Data Mining and Knowledge Discovery, 9(3), e1301.

10. Tseng, W.-J., Kontrazis, P. D., Lecolinet, E., Huron, S., & Gugenheimer, J. (2024). *Understanding Interaction and Breakouts of Safety Boundaries in Virtual Reality through Mixed-Method Studies*. IEEE VR, 2024.

11. Schmelter, T., Küchenmeister, P., Geuter, J., & Hildebrand, K. (2025). *Depth-aware immersive visualization of boundaries using particles in VR*. ACM SUI, 2025.
