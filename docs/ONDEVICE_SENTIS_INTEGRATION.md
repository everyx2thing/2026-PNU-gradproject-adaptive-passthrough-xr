# 온디바이스 ML 개인화 통합 및 실시간 테스트

## 현재 상태

- ML 데이터 처리·학습·변환 코드는 `ml-personalization/`에 통합되어 있다.
- 전달받은 Random Forest 모델은 원본 보존 목적으로
  `Assets/Models/rf_personalization_real.onnx.source`에 둔다.
- 이 모델은 `ai.onnx.ml/TreeEnsembleClassifier`를 사용하고 출력 클래스가
  `Neutral`, `Positive`뿐이다. `Negative` 클래스가 없다.
- Unity InferenceEngine 2.6.1은 이 파일을 `.onnx`로 임포트할 때 예외를 발생시킨다.
  `convert_tree_onnx_for_unity.py`가 트리를 표준 ONNX 연산으로 변환한
  `Assets/Models/personalization_runtime.onnx`를 런타임에 사용한다.
- 사용자 요청에 따라 `Neutral`을 `Negative` 위험으로 해석한다. 호환 모델은
  `risk_probability = P(Neutral) = 1 - P(Positive)`를 직접 출력한다.
- 앱은 ML 비사용 상태로 시작한다. 패널에서 ML을 켜거나 `APPLY ML NOW`를 누르면
  ONNX 결과를 정적 정책 임계값에 적용한다.

## 런타임 구조

```text
StaticPassthroughPolicyController 이벤트
    + QuestRiskExperimentLogger 머리 속도
    + 수동 입력한 방 면적/세션 정보
        ↓
PersonalizationRuntimeController
        ↓ 7차원 feature
Unity 호환 ONNX (`risk_probability = P(Neutral)`)
        ↓
stableOnThreshold / rapidOnThreshold / handFullThreshold
        ↓
ML OFF: 안전 기본 임계값
ML ON / APPLY NOW: StaticPassthroughPolicyController에 적용
```

정적 정책에 적용하는 공개 진입점은 다음과 같다.

```csharp
staticPolicy.ApplyPersonalizedThresholds(
    stableOnThreshold,
    rapidOnThreshold,
    handFullThreshold);
```

## 7차원 feature 계약

순서는 학습과 Unity에서 반드시 동일해야 한다.

| 순서 | 이름 | 런타임 값 |
|---:|---|---|
| 0 | `f_pt` | 최근 활성화 수 / 이벤트 윈도우 크기 |
| 1 | `r_cancel` | 사용자가 불필요하다고 표시한 활성화 비율 |
| 2 | `t_pt_bar` | 최근 활성화 평균 지속시간(초) |
| 3 | `v_h_bar` | 활성화 중 평균 머리 속도(m/s) |
| 4 | `v_h_max` | 활성화 중 최대 머리 속도(m/s) |
| 5 | `A_space_norm` | 방 면적을 임시 4~8㎡ 범위로 정규화 |
| 6 | `T_session_norm` | 세션 경과시간을 임시 30분으로 정규화 |

실제 모델이 학습될 당시 `r_cancel`은 항상 0이었다. UI에서 값을 바꾸는 기능은
파이프라인 시험용이며, 재학습 전에는 실제 개인화 품질을 의미하지 않는다.

## 개인화 수식과 안전 게이트

기본값은 다음과 같다.

```text
stableOnThreshold = 0.65
rapidOnThreshold  = 0.45
handFullThreshold = 0.85
minimum sessions  = 5
adjustment scale  = 0.20
maximum threshold = 0.95
```

세션 수가 5 미만이면 자동 추론은 기본값을 유지한다. `APPLY ML NOW`는 명시적인
사용자 동작이므로 cold-start를 한 번만 우회하여 즉시 추론·적용한다.

```text
delta = max((pNegative - 0.5) * adjustmentScale, 0)
threshold = min(default + delta, maximumThreshold)
```

ML은 아래 안전 파라미터를 변경하지 않는다.

- 초근접/비상 거리와 emergency hold
- 최소 패스스루 유지시간
- 해제 지연과 히스테리시스 폭
- 동적 사람 위험 및 critical override
- Boundaryless 설정

## Quest 실시간 테스트 패널

오른손 컨트롤러 레이저로 패널을 가리키고 인덱스 트리거로 조작한다.

### LIVE

- 정적 head/hand/combined risk, 거리, TTC, activation cause, emergency 상태
- 동적 사람 수, 주 대상 거리·접근 속도·TTC
- 현재 정책 임계값과 패스스루 window 상태
- 7개 feature, 현재 머리 속도, 최근 이벤트 수
- 모델 상태, 추론 source, `pRISK`, 로그 파일명
- `STATIC`, `DYNAMIC`, `ML` 사용/미사용 토글
- `APPLY ML NOW`: 현재 7차원 입력으로 ONNX를 즉시 실행하고 결과를 한 번 적용
- `SAVE SETTINGS`, `RESET SETTINGS`, 패널 투명도

## 로그

개인화 스냅샷은 다음 경로에 JSONL로 기록된다.

```text
Application.persistentDataPath/RiskLogs/personalization-*.jsonl
```

각 레코드에는 7개 feature, 세션 수, cold-start 여부, 모델 상태, pRISK,
계산 임계값, 실제 적용 여부와 당시 정적 risk가 포함된다. UI의 LIVE 탭에도 현재
파일명이 표시된다.

## 호환 모델 교체 조건

새 Random Forest 모델은 원본을 `.onnx.source`로 보존하고 다음 명령으로 변환한다.

```bash
python ml-personalization/src/convert_tree_onnx_for_unity.py
```

생성된 `Assets/Models/personalization_runtime.onnx`에서 다음을 확인해야 한다.

1. InferenceEngine 2.6.1에서 오류 없이 임포트된다.
2. 입력 shape가 `(1, 7)`이고 feature 순서가 위 표와 같다.
3. 확률 출력이 float tensor다.
4. 클래스 순서가 `Neutral`, `Positive`이고 `Neutral`이 위험 확률로 변환된다.
5. 원본 ONNX와 호환 ONNX의 `P(Neutral)` 오차가 `1e-5` 이하다.
6. Unity Editor와 Quest에서 `risk_probability` 출력이 동일하게 읽힌다.

현재 source 모델은 약 25개 표본 기반 파이프라인 검증용이므로 배포 품질 모델로
간주하지 않는다.
