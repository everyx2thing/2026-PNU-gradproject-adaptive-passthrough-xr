# 온디바이스(Sentis) ML 개인화 통합 스펙

작성: 따다소 (ML 개인화)
대상: 아영 (unity-client)
목적: Backend/서버 없이, Quest 안에서 개인화 가중치를 실시간으로 계산해서
      Passthrough 판단에 반영하기.

## 왜 이 방향인가
- Backend/PostgreSQL 없이도 로그 수집 → 위험도 판단 개인화까지 전부 끝낼 수 있음
- 지금 이미 Quest 로컬에 쌓이고 있는 `RiskSnapshot` 로그만으로 충분함
- `ml-personalization` 파이프라인은 이미 real 데이터로 끝까지 도는 것 확인 완료

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
personalize.cs (아래 로직) 로 w_c/w_s/w_d/w_i, tau_viz 계산
        ↓
다음 프레임 Rtotal 계산에 이 가중치 사용
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
- 출력: 클래스 확률 (ONNX ZipMap 출력, "Negative"/"Positive"/"Neutral" 키의 dict) — `convert_to_onnx.py`에서 이미 이 출력 형태 검증 완료

## 3. 가중치 계산 로직 (personalize.py → C# 그대로 이식)

```csharp
// 기본값 (cold-start 구간에서 사용)
const float W_C_DEFAULT = 0.30f;
const float W_S_DEFAULT = 0.20f;
const float W_D_DEFAULT = 0.30f;
const float W_I_DEFAULT = 0.20f;
const float TAU_VIZ_DEFAULT = 0.5f;

const float ADJUSTMENT_SCALE = 0.2f;  // TODO: 임의값, 사용자 테스트로 튜닝 필요
const float MIN_WEIGHT = 0.05f;
const int N_MIN_SESSIONS = 5;  // 이 세션 수 미만이면 기본값 사용 (cold-start)

// pNegative: Sentis 추론 결과의 "Negative" 클래스 확률
float delta = Mathf.Max((pNegative - 0.5f) * ADJUSTMENT_SCALE, 0f);

float wC = Mathf.Max(W_C_DEFAULT - delta, MIN_WEIGHT);
float actualReduction = W_C_DEFAULT - wC;

float otherTotal = W_S_DEFAULT + W_D_DEFAULT + W_I_DEFAULT;
float wS = W_S_DEFAULT + actualReduction * (W_S_DEFAULT / otherTotal);
float wD = W_D_DEFAULT + actualReduction * (W_D_DEFAULT / otherTotal);
float wI = W_I_DEFAULT + actualReduction * (W_I_DEFAULT / otherTotal);

float tauViz = Mathf.Min(TAU_VIZ_DEFAULT + delta, 1.0f);
```

## 4. Cold-start 게이트
```csharp
if (accumulatedSessionCount < N_MIN_SESSIONS) {
    // 기본값(W_C_DEFAULT 등) 그대로 사용, Sentis 추론 스킵해도 됨
} else {
    // 위 로직으로 개인화된 값 사용
}
```
"세션 수"는 지금 `PlayerPrefs`나 로컬 파일에 카운터 하나 저장해서 앱 실행할 때마다 +1 하면 될 것 같습니다.

## 5. 지금 모델의 한계 (같이 알고 있어야 할 것)
- 지금 `rf_personalization_real.onnx`는 표본이 25개(대부분 한 세션)뿐이라 **실제 배포용이 아니라 파이프라인 검증용**입니다
- `v_h_bar/v_h_max`는 아직 로그에 `headSpeedMps`가 충분히 안 쌓여서 사실상 신호가 없는 상태
- 통합 코드 자체는 지금 모델로 테스트해보고, 나중에 로그 더 쌓이면 모델 파일만 교체하면 됩니다 (구조는 안 바뀜)

## 6. 확인이 필요한 것 (아영한테 물어볼 것)
- [ ] Unity Sentis 패키지 아직 설치 안 되어있으면 Package Manager에서 추가 필요
- [ ] `A_space_norm` 정규화 기준(4~8㎡)이 실제 Scene API 값 범위랑 맞는지
- [ ] `T_session_norm` 기준(30분)이 적당한지, 아니면 다른 기준으로 할지
