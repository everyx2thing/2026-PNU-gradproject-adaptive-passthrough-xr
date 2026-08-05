# 온디바이스(Sentis) ML 개인화 통합 스펙

작성: 따다소 (ML 개인화)
대상: 아영 (unity-client)
목적: Backend/서버 없이, Quest 안에서 개인화된 Passthrough ON 임계값을 실시간으로
      계산해서 판단에 반영하기.

> **2026-08-06 갱신**: `feature/static-boundary-passthrough` 브랜치(아직 main 미머지)에서
> `QuestRiskExperimentLogger.cs`가 예전 `Rtotal = w_c*R_c + w_s*R_s + w_d*R_d + w_i*R_i` 가중합 +
> 단일 `tau_viz` 임계값 구조를 버리고, `R_static_head`/`R_static_hand` 두 경로 + `UserState`로
> on/off 임계값을 Lerp 조절하는 구조로 바꿨습니다. 이 문서와 `PersonalizationSentisRunner.cs`는
> 그 새 구조(`stableOnThreshold`/`rapidOnThreshold`/`handFullThreshold`)에 맞춰 다시 썼습니다.
> **아직 그 브랜치가 실제로 main에 머지된다는 보장은 없으니, 머지 여부가 확정되면 이 문서가
> 맞는 구조를 가리키고 있는지 다시 확인해주세요.**

## 왜 이 방향인가
- Backend/PostgreSQL 없이도 로그 수집 → 위험도 판단 개인화까지 전부 끝낼 수 있음
- 지금 이미 Quest 로컬에 쌓이고 있는 `RiskSnapshot` 로그만으로 충분함
- `ml-personalization` 파이프라인은 mock/real(합성 로그 dry-run) 양쪽 다 end-to-end 동작 확인 완료
  (실제 Quest 로그로는 아직 재검증 전)

## 전체 흐름
```
Unity가 이미 갖고 있는 값들
  (활성화 지속시간, 세션 진행 시간, 공간 크기, 머리 이동속도)
        ↓
7차원 feature 벡터로 조립 (아래 표 순서 그대로, 순서 바뀌면 안 됨)
        ↓
rf_personalization_real.onnx 에 Sentis로 추론
        ↓
Negative 확률(p_negative) 추출
        ↓
personalize.py 로직(아래 3번) 그대로 이식한 C#으로
stableOnThreshold / rapidOnThreshold / handFullThreshold 계산
        ↓
QuestRiskExperimentLogger.cs의 같은 이름 필드에 주입
(→ 이 필드들이 지금 private [SerializeField]라서, 외부에서 값을 넣을 수 있는
   public setter/프로퍼티를 추가하는 작업이 필요함 — 아래 6번 체크리스트 참고)
```

## 1. Feature 벡터 (순서 고정 — 모델 학습 시 이 순서로 맞춰짐)

| 순서 | 이름 | 뜻 | Unity에서 어떻게 구하는지 |
|---|---|---|---|
| 0 | f_pt | 최근 윈도우 내 활성화 빈도 | 최근 N개 활성화 이벤트 수 / 윈도우 크기 |
| 1 | r_cancel | 수동 해제 비율 | **지금은 항상 0** (수동 제어 미구현) |
| 2 | t_pt_bar | 평균 지속시간(초) | 최근 활성화 이벤트들의 duration 평균 |
| 3 | v_h_bar | 평균 머리 이동속도 | `UserStateRiskSnapshot.FilteredSpeed` 평균 (이미 로그에 추가한 필드) |
| 4 | v_h_max | 최대 머리 이동속도 | 위 값들 중 최댓값 |
| 5 | A_space_norm | 공간 크기 정규화 (0~1) | Scene API로 얻은 방 면적을 정규화 (범위는 별도 논의 필요, 임시로 4~8㎡ 기준) |
| 6 | T_session_norm | 세션 진행률 (0~1) | 현재 세션 경과시간 / 예상 최대 세션 시간(임시 30분 기준) |

**주의**: `r_cancel`은 지금 항상 0으로 넣어야 학습 때와 일관성이 유지됩니다 (모델이 그렇게 학습됨).

## 2. Sentis 추론 (C# 스켈레톤)
`unity-client-snippet/PersonalizationSentisRunner.cs` 참고. 핵심 포인트:
- 모델 파일(`rf_personalization_real.onnx`)을 `Assets/Models/`에 넣고 Sentis `ModelAsset`으로 임포트
- 입력 텐서 shape: `(1, 7)` — 위 표 순서 그대로
- 출력: `output_label`(문자열 클래스), `output_probability`(shape `(1, n_classes)`의 순수 float 텐서)
  - **2026-08 수정**: 원래 skl2onnx 기본값(ZipMap)은 확률을 `seq(map(string,float))` 형태로
    내보내는데, **Unity Sentis는 시퀀스/맵 타입을 지원하지 않고 텐서만 읽을 수 있음**.
    그래서 `convert_to_onnx.py`에서 `zipmap=False` 옵션으로 순수 텐서 출력으로 바꿔둠.
  - 컬럼 순서는 dict 키가 아니라 **그 모델을 학습할 때의 `model.classes_` 순서**를 그대로 따름
    (`convert_to_onnx.py` 실행 시 `학습된 클래스 순서 (model.classes_): [...]` 로그로 출력됨 —
    onnx 파일 넘길 때 이 로그도 같이 캡처해서 전달할 것).
  - **주의**: 실제 로그 표본이 적으면 학습 데이터에 "Negative" 클래스 자체가 없을 수 있음
    (실제로 합성 dry-run 테스트에서 이 경우가 발생함 — 표본 51개 중 Negative 1개뿐이라 train/test
    split 과정에서 Negative가 학습셋에서 빠짐). 이 경우 `classes_`에 "Negative"가 없으므로
    `negativeClassIndex`를 하드코딩하면 안 되고, 매번 클래스 목록에서 "Negative"를 찾고
    없으면 p_negative=0(기본값 유지 방향)으로 처리해야 함 — 파이썬 쪽 `personalize.py`의
    `_get_negative_probability()`가 이미 이렇게 방어적으로 되어 있음, C# 쪽도 동일하게 처리 필요.
  - onnxruntime 기준 검증은 `convert_to_onnx.py`에서 자동으로 함 (sklearn vs onnx 확률값 비교).
    Sentis 실제 로드/출력 파싱은 이 문서 작성 시점까지 확인 안 됨 (아래 6번 항목).

## 3. 임계값 계산 로직 (personalize.py → C# 그대로 이식)

핵심 아이디어: 최근 활성화들 중 "괜히 켜진(Negative)" 비율이 높을수록, `QuestRiskExperimentLogger.cs`가
실제로 쓰는 세 ON 임계값을 전부 올려서 더 확실한 위험 상황에서만 Passthrough가 켜지게(=덜 민감하게) 만듦.

```csharp
// 기본값 (cold-start 구간에서 사용). QuestRiskExperimentLogger.cs의 [SerializeField]
// 기본값(stableOnThreshold=0.65, rapidOnThreshold=0.45, handFullThreshold=0.85)과 동일하게 유지.
const float STABLE_ON_DEFAULT = 0.65f;
const float RAPID_ON_DEFAULT = 0.45f;
const float HAND_FULL_DEFAULT = 0.85f;

const float ADJUSTMENT_SCALE = 0.2f;  // TODO: 임의값, 사용자 테스트로 튜닝 필요
const float MAX_THRESHOLD = 0.95f;    // 안전장치: 완전히 둔감해지지 않도록 상한
const int N_MIN_SESSIONS = 5;         // 이 세션 수 미만이면 기본값 사용 (cold-start)

// pNegative: Sentis 추론 결과의 "Negative" 클래스 확률
float delta = Mathf.Max((pNegative - 0.5f) * ADJUSTMENT_SCALE, 0f);

float stableOnThreshold = Mathf.Min(STABLE_ON_DEFAULT + delta, MAX_THRESHOLD);
float rapidOnThreshold = Mathf.Min(RAPID_ON_DEFAULT + delta, MAX_THRESHOLD);
float handFullThreshold = Mathf.Min(HAND_FULL_DEFAULT + delta, MAX_THRESHOLD);
```

`hysteresisWidth`, `awareThreshold`, `weightDistance`/`weightTTC`/`weightApproachAcceleration`/
`weightBlind` 등 나머지 파라미터는 개인화 대상이 아니라 그대로 둠 (지금 범위는 "얼마나 쉽게
켜지는가"만 사용자 이력 기반으로 조정 — `effectiveOffThreshold`는 Unity 쪽에서
`effectiveOnThreshold - hysteresisWidth`로 자동 계산되므로 따로 안 건드려도 됨).

## 4. Cold-start 게이트
```csharp
if (accumulatedSessionCount < N_MIN_SESSIONS) {
    // 기본값(STABLE_ON_DEFAULT 등) 그대로 사용, Sentis 추론 스킵해도 됨
} else {
    // 위 로직으로 개인화된 값 사용
}
```
"세션 수"는 지금 `PlayerPrefs`나 로컬 파일에 카운터 하나 저장해서 앱 실행할 때마다 +1 하면 될 것 같습니다.

## 4-1. QuestRiskExperimentLogger.cs 쪽에 필요한 것 (제안 — 아영님 파일이라 여기 코드는 안 넣었음)

`stableOnThreshold`/`rapidOnThreshold`/`handFullThreshold`가 지금 private `[SerializeField]`라서,
`PersonalizationSentisRunner`가 계산한 값을 넣어줄 방법이 없습니다. 예를 들면 이런 public 메서드
하나만 추가되면 연결할 수 있습니다 (정확한 이름/방식은 아영님이 편한 대로 바꿔도 됩니다):

```csharp
// QuestRiskExperimentLogger.cs에 추가 제안
public void ApplyPersonalizedThresholds(float stableOn, float rapidOn, float handFull)
{
    stableOnThreshold = stableOn;
    rapidOnThreshold = rapidOn;
    handFullThreshold = handFull;
}
```

그러면 `PersonalizationSentisRunner` 쪽에서:
```csharp
var p = personalizationRunner.GetPersonalizedParams(sessionCount, featureVector);
riskExperimentLogger.ApplyPersonalizedThresholds(p.stableOnThreshold, p.rapidOnThreshold, p.handFullThreshold);
```
처럼 매 세션(또는 매 N분)마다 한 번씩 갱신해주면 됩니다 — 매 프레임 호출할 필요는 없음
(개인화 값은 세션 단위로만 바뀌는 값이라서).

## 5. 지금 모델의 한계 (같이 알고 있어야 할 것)
- 지금 `rf_personalization_real.onnx`는 표본이 25개(대부분 한 세션)뿐이라 **실제 배포용이 아니라 파이프라인 검증용**입니다
- `v_h_bar/v_h_max`는 아직 로그에 `headSpeedMps`가 충분히 안 쌓여서 사실상 신호가 없는 상태
- 통합 코드 자체는 지금 모델로 테스트해보고, 나중에 로그 더 쌓이면 모델 파일만 교체하면 됩니다 (구조는 안 바뀜)
- 표본이 적으면 학습 데이터에 "Negative"(또는 다른 클래스)가 통째로 빠질 수 있음 -> `model.classes_`가
  세션마다 달라질 수 있다는 뜻이라, negativeClassIndex를 코드에 하드코딩하지 말고 매번 확인할 것
- ONNX 변환 시 `target_opset=17`로 고정해뒀음 (설치된 onnx 패키지가 onnxruntime/Sentis가 아직
  지원 안 하는 최신 opset을 자동으로 골라버려서 로드 자체가 실패하는 문제 방지). Sentis가 opset 17도
  못 읽으면 `convert_to_onnx.py`의 `TARGET_ONNX_OPSET`을 더 낮춰야 할 수 있음

## 6. 확인이 필요한 것 (아영한테 물어볼 것)
- [ ] `feature/static-boundary-passthrough`가 실제로 main에 머지되는지, 머지된다면 그 구조가
      최종인지 (머지 안 되고 예전 `OverallRiskFusion` 가중합 구조로 돌아간다면 이 문서와
      `PersonalizationSentisRunner.cs`를 다시 예전 방식으로 되돌려야 함)
- [ ] `QuestRiskExperimentLogger.cs`의 `stableOnThreshold`/`rapidOnThreshold`/`handFullThreshold`가
      지금 private `[SerializeField]`인데, 개인화 값을 실제로 주입하려면 public setter(또는
      런타임에 값을 갱신할 수 있는 프로퍼티/메서드)가 필요함 — 이 작업은 아영님 쪽에서 진행 필요
- [ ] Unity Sentis 패키지 아직 설치 안 되어있으면 Package Manager에서 추가 필요
- [ ] `A_space_norm` 정규화 기준(4~8㎡)이 실제 Scene API 값 범위랑 맞는지
- [ ] `T_session_norm` 기준(30분)이 적당한지, 아니면 다른 기준으로 할지
- [ ] onnx 파일을 실제로 Sentis `ModelAsset`으로 임포트했을 때 opset 17이 문제없이 로드되는지
- [ ] `output_probability` 텐서를 Sentis에서 실제로 인덱싱해봤을 때, `convert_to_onnx.py`가 출력한
      `model.classes_` 순서와 일치하는지 (모델 파일 교체할 때마다 이 순서를 다시 확인해야 함)
