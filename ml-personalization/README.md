# ML 개인화 (3.4절) — 파이프라인 README

담당: 로그 수집 및 피드백 전략(3.4.1, 3.4.3) / ML 개인화 모델 구현·경량화(3.4.2, 3.4.4)

> **2026-08-06 구조 변경**: unity-client(아영님) 쪽 Passthrough 판단 구조가 예전
> `Rtotal = w_c*R_c + w_s*R_s + w_d*R_d + w_i*R_i` 가중합 방식에서 `QuestRiskExperimentLogger.cs`
> (`feature/static-boundary-passthrough` 브랜치, 아직 main 미머지) 기준 임계값(threshold) 조절
> 방식으로 바뀌어서, 이 파이프라인의 출력도 `w_c/w_s/w_d/w_i,tau_viz`에서
> `stable_on_threshold/rapid_on_threshold/hand_full_threshold`로 바꿨습니다. 그 브랜치가
> 실제로 머지 안 되거나 구조가 또 바뀌면 이 파이프라인도 다시 맞춰야 합니다 —
> `docs/ONDEVICE_SENTIS_INTEGRATION.md` 상단 안내 참고.

## 실행 순서 (mock 데이터로 동작 확인용)

```bash
pip install -r requirements.txt

cd src

python generate_mock_logs.py      # 1. mock 세션/이벤트 로그 생성
python build_features.py          # 2. 라벨링 + 7차원 feature 벡터 추출
python train_model.py             # 3. RF 모델 학습
python convert_to_onnx.py         # 4. ONNX 변환 + 검증 (클래스 예측 + 확률값 둘 다)
python cold_start.py              # 5. cold-start 게이트 동작 확인
python personalize.py             # 6. 가중치/임계값 매핑 수식 검증
python test_personalization.py    # 7. 전체 파이프라인 통합 테스트 (cold_start + personalize)
python hyperparam_tuning.py       # 8. 윈도우 크기/stride 비교 실험 (선택)
```

## 실제 Quest 로그로 개인화 모델 만들기 (팀원용 — 아래 "실제 로그 연동 절차" 참고)

퀘스트를 가진 팀원이 아래 절차를 따라하면 `rf_personalization_real.onnx`까지
혼자 만들 수 있습니다. ML 쪽 코드/수식을 몰라도 됩니다.

## 파일 설명

| 파일 | 역할 |
|---|---|
| `config.py` | 여러 파일에서 공유하는 상수 (FEATURE_COLUMNS, N_MIN_SESSIONS, THRESHOLD_DEFAULT 등) |
| `generate_mock_logs.py` | 원룸 환경(2~8㎡) 기준 mock 세션/이벤트 로그 생성 |
| `parse_risk_snapshot_log.py` | **실제** Quest `risk-snapshot-*.jsonl` 로그를 읽어서 `real_sessions.csv`/`real_events.csv`로 변환 |
| `build_features.py` | 라벨링 규칙 적용 + 슬라이딩 윈도우 기반 7차원 feature 추출 (`--source mock`/`real`) |
| `train_model.py` | Random Forest 개인화 모델 학습 및 평가 (`--source mock`/`real`) |
| `convert_to_onnx.py` | 학습된 모델을 ONNX로 변환, 클래스/확률 예측 둘 다 sklearn과 일치하는지 검증 (`--source mock`/`real`) |
| `convert_tree_onnx_for_unity.py` | `TreeEnsembleClassifier`를 Unity 호환 표준 ONNX 연산으로 변환하고 `Neutral`을 위험 확률로 검증 |
| `cold_start.py` | 세션 수 기준 cold-start 판단 + `personalize.py` 매핑까지 합친 최종 진입점(`get_personalized_params`) |
| `personalize.py` | 모델 확률(Negative 확률) 기반으로 실제 stable_on_threshold/rapid_on_threshold/hand_full_threshold 계산 |
| `test_personalization.py` | cold_start + personalize 통합 동작 확인 (`--source mock`/`real`) |
| `hyperparam_tuning.py` | 윈도우 크기(W)/stride(S) 여러 조합 비교 실험 |

## 실제 로그 연동 절차 (Quest 담당 팀원용)

1. **Quest에서 로그 파일 꺼내기**
   앱 실행 중 `RiskSnapshotSessionLogger`가 기기 안 `Application.persistentDataPath/RiskLogs/`에
   `risk-snapshot-YYYYMMDD-HHmmss.jsonl` 파일을 씀. `adb pull`(또는 Device File Explorer)로 PC에 복사.
   세션(=한 번 앱 켜서 쓴 구간)마다 파일이 따로 생김 — 여러 개 모아도 됨.

2. **각 로그 파일을 real_sessions.csv / real_events.csv로 변환**
   ```bash
   cd ml-personalization/src
   python parse_risk_snapshot_log.py --input risk-snapshot-20260728-141623.jsonl --session-id s1 --space-m2 4.0
   python parse_risk_snapshot_log.py --input risk-snapshot-20260729-091000.jsonl --session-id s2 --space-m2 4.0 --append
   ```
   - `--session-id`: 임의로 붙이는 이름 (s1, s2, ... 겹치지만 않으면 됨)
   - `--space-m2`: 그 로그를 찍을 때 있던 방의 크기(㎡). 로그에 안 남는 값이라 직접 입력해야 함
   - `--append`: 두 번째 파일부터는 꼭 붙여야 기존 csv에 이어서 쌓임 (안 붙이면 덮어씀)
   - 마지막에 "활성화 이벤트 0개"라고 나오면 그 로그 구간엔 Passthrough가 한 번도 안 켜진 것 — 정상일 수 있음

3. **feature 추출 -> 학습 -> ONNX 변환**
   ```bash
   python build_features.py --source real
   python train_model.py --source real       # 윈도우 5개 미만이면 여기서 에러 남 -> 로그 더 모아야 함
   python convert_to_onnx.py --source real
   python test_personalization.py --source real   # 세션별로 값이 잘 나오는지 눈으로 확인
   ```
   `convert_to_onnx.py`가 마지막에 `models/rf_personalization_real.onnx`를 만듦. Unity 프로젝트에는 이 파일을
   `Assets/Models/rf_personalization_real.onnx.source`로 보존한 뒤 다음 변환을 실행할 것.

   ```bash
   python ml-personalization/src/convert_tree_onnx_for_unity.py
   ```

   결과물 `Assets/Models/personalization_runtime.onnx`는
   `risk_probability = P(Neutral) = 1 - P(Positive)`를 출력함.

4. **Unity 쪽 통합**: `docs/ONDEVICE_SENTIS_INTEGRATION.md` + `PersonalizationSentisRunner.cs` 참고.
   onnx 파일을 `unity-client/Assets/Models/`에 넣고 Sentis `ModelAsset`으로 임포트하면 됨.

## 지금까지 확인된 것

- 전체 파이프라인(로그 생성 → feature 추출 → 학습 → ONNX 변환 → 개인화 매핑)이 mock/real(합성 로그로
  dry-run) 양쪽 다 end-to-end로 정상 동작 확인 (2026-08-06)
- `cold_start.py`와 `personalize.py`가 하나의 진입점(`get_personalized_params`)으로 합쳐짐 — 이전엔
  `cold_start.py`의 공개 함수가 스텁이라 실제 매핑 로직은 테스트 스크립트 안에만 있었음
- ONNX 변환 후 클래스 예측뿐 아니라 **확률값도 sklearn과 일치** → 온디바이스(Sentis)에서 `personalize.py`의 로직을 그대로 쓸 수 있음
- `convert_to_onnx.py`를 `zipmap=False`로 바꿔서 확률 출력이 **순수 float 텐서**로 나오도록 수정함
  (원래 ZipMap 기본값은 `seq(map(string,float))` 타입인데 Sentis가 시퀀스/맵 타입을 못 읽음 — 그대로 넘겼으면
  Unity 쪽에서 모델 출력을 못 읽었을 것)
- ONNX 변환 opset을 17로 고정함 (설치된 onnx 패키지가 onnxruntime이 아직 공식 지원 안 하는 opset을
  자동으로 골라서 로드 자체가 실패하는 문제가 실제로 발생했음 — 팀원 PC 환경에 따라 재현 여부가 다를 수 있어서 고정)
- 모델 크기 0.04~0.09MB (목표 50MB 대비 여유 충분)

## 아직 안 된 것 (TODO)

- [x] Unity InferenceEngine 실제 로드 테스트 및 `risk_probability` 출력 텐서 파싱
- [ ] 실제 Quest 로그로 `parse_risk_snapshot_log.py` ~ `test_personalization.py --source real` 전체를
      진짜 데이터로 한 번 실행 (지금까지는 합성 로그로 코드 동작만 검증함, 라벨 분포/정확도는 무의미)
- [ ] 라벨링 경계 케이스(두 임계값 사이 구간) 처리 방식 재검토
- [ ] real용 라벨링 임계값(0.15초/0.3초), 윈도우 크기/stride(3/1) 최종 확정 — 지금은 로그 몇 개 보고 정한 임시값
- [ ] `personalize.py`의 ADJUSTMENT_SCALE, MAX_THRESHOLD 값 사용자 테스트로 튜닝
- [ ] p_negative < 0.5일 때(활성화가 잘 맞았을 때) 임계값을 더 민감하게(낮게) 만들지 여부 결정 (지금은 보수적으로 미조정)
- [ ] 사용자 테스트 설계 및 실행 (Baseline Guardian / Full Passthrough / Proposed 비교)
- [ ] `A_space_norm`/`T_session_norm` 정규화 기준(4~8㎡, 30분)이 실제 Scene API 값 범위와 맞는지 확인
- [ ] `feature/static-boundary-passthrough` 머지 여부 확정 후, `QuestRiskExperimentLogger.cs`에
      `stableOnThreshold`/`rapidOnThreshold`/`handFullThreshold`를 외부에서 갱신할 수 있는
      public setter 추가 (아영님 쪽 작업, 지금은 전부 private `[SerializeField]`)

## 주의할 점

- mock 결과는 **mock 데이터 기반**이라, RF 모델 accuracy(97%)는 라벨이 feature로부터 파생된 구조라서 나온 결과, 실제데이터로 재검증 전까지는 무의미 (정상 동작 확인용)
- real 파이프라인은 합성(가짜) 로그로 **코드가 안 깨지는지만** 검증한 상태 — 실제 Quest 로그로 다시 돌려서
  라벨 분포/모델 품질을 재확인해야 함
- 현재 실모델의 클래스는 `Neutral`, `Positive`이며 사용자 결정에 따라 `Neutral`을 `Negative` 위험으로 사용함.
  재학습 후 클래스 순서가 바뀌면 Unity 호환 변환 스크립트의 검증이 실패하도록 되어 있음
- `mock_*.csv`, `real_*.csv`, `*.joblib`, `*.onnx` 파일은 `.gitignore` 처리되어 있어서 저장소에는 안 올라감
  → onnx 모델 파일은 Unity 담당자에게 **직접 전달**해야 함 (git으로 안 감)
