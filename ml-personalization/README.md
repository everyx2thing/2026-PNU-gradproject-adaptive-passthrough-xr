# ML 개인화 (3.4절) — 파이프라인 README

담당: 로그 수집 및 피드백 전략(3.4.1, 3.4.3) / ML 개인화 모델 구현·경량화(3.4.2, 3.4.4)

## 실행 순서

```bash
pip install -r requirements.txt

cd src

python generate_mock_logs.py      # 1. mock 세션/이벤트 로그 생성
python build_features.py          # 2. 라벨링 + 7차원 feature 벡터 추출
python train_model.py             # 3. RF 모델 학습
python convert_to_onnx.py         # 4. ONNX 변환 + 검증 (클래스 예측 + 확률값 둘 다)
python cold_start.py              # 5. cold-start 게이트 동작 확인
python personalize.py             # 6. 가중치/임계값 매핑 수식 검증
python test_personalization.py    # 7. 전체 파이프라인 통합 테스트
python hyperparam_tuning.py       # 8. 윈도우 크기/stride 비교 실험 (선택)
```

## 파일 설명

| 파일 | 역할 |
|---|---|
| `config.py` | 여러 파일에서 공유하는 상수 (FEATURE_COLUMNS, N_MIN_SESSIONS 등) |
| `generate_mock_logs.py` | 원룸 환경(2~8㎡) 기준 mock 세션/이벤트 로그 생성 |
| `build_features.py` | 라벨링 규칙 적용 + 슬라이딩 윈도우 기반 7차원 feature 추출 |
| `train_model.py` | Random Forest 개인화 모델 학습 및 평가 |
| `convert_to_onnx.py` | 학습된 모델을 ONNX로 변환, 클래스/확률 예측 둘 다 sklearn과 일치하는지 검증 |
| `cold_start.py` | 세션 수 기준 cold-start 판단, 기본 가중치(w_Default)/임계값 반환 |
| `personalize.py` | 모델 확률(Negative 확률) 기반으로 실제 w_c,w_s,w_d,w_i,tau_viz 계산 |
| `test_personalization.py` | cold_start + personalize 통합 동작 확인 |
| `hyperparam_tuning.py` | 윈도우 크기(W)/stride(S) 여러 조합 비교 실험 |

## 지금까지 확인된 것

- 전체 파이프라인(로그 생성 → feature 추출 → 학습 → ONNX 변환 → 개인화 매핑)이 end-to-end로 정상 동작
- ONNX 변환 후 클래스 예측뿐 아니라 **확률값도 sklearn과 일치** → 온디바이스(Sentis)에서 `personalize.py`의 로직을 그대로 쓸 수 있음
- 모델 크기 0.09MB (목표 50MB 대비 여유 충분)

## 아직 안 된 것 (TODO)

- [ ] Unity Sentis 실제 로드 테스트 (Unity 프로젝트 준비되면)
- [ ] 실제 HMD 로그 연동 (백엔드 스키마 확정 후)
- [ ] 라벨링 경계 케이스(2~3초 구간) 처리 방식 재검토
- [ ] 윈도우 크기/stride 최종 확정 (지금은 mock 데이터 기준 참고치만 있음, `hyperparam_tuning.py` 결과 참고)
- [ ] `personalize.py`의 ADJUSTMENT_SCALE, MIN_WEIGHT 값 사용자 테스트로 튜닝
- [ ] p_negative < 0.5일 때(활성화가 잘 맞았을 때) 가중치를 더 민감하게 만들지 여부 결정 (지금은 보수적으로 미조정)
- [ ] 사용자 테스트 설계 및 실행 (Baseline Guardian / Full Passthrough / Proposed 비교)

## 주의할 점

- 지금 모든 결과는 **mock 데이터 기반**이라, RF 모델 accuracy(97%)는 라벨이 feature로부터 파생된 구조라서 나온 순환적 결과입니다. 실 데이터로 재검증 전까지는 "파이프라인이 정상 동작한다"는 것만 의미합니다.
- `mock_*.csv`, `*.joblib`, `*.onnx` 파일은 `.gitignore` 처리되어 있어서 저장소에는 안 올라갑니다. 필요하면 스크립트 재실행으로 언제든 다시 생성됩니다.
