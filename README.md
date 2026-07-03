# Adaptive Passthrough Framework for Immersive XR

몰입형 XR을 위한 상황 인식 기반 Adaptive Passthrough Framework — 정적/동적 위험 분석과 ML 개인화를 결합한 Quest 3 안전 시스템

Context-aware Adaptive Passthrough Framework for Immersive XR combining static/dynamic risk analysis with ML-based personalization for Meta Quest 3

---

## 프로젝트 소개

본 프로젝트는 Meta Quest 기반 몰입형 XR 환경에서 사용자의 안전과 몰입감을 동시에 확보하기 위한 상황 인식 기반 Adaptive Passthrough Framework 프로토타입입니다. 기존 Guardian 시스템의 단순 On/Off 방식을 넘어, 정적 경계와 동적 객체(사람·반려동물·이동 물체)의 충돌 위험 및 접근 의도를 실시간으로 분석하고, 사용자 로그 기반 ML 개인화 모델을 통해 사용자별 최적의 Passthrough 시각화를 제공합니다.


## 팀원 및 역할

| 이름 | 담당 |
|---|---|
| 따다소 (팀장) | 로그 수집 및 피드백 전략, ML 기반 개인화 모델 구현 및 경량화, 사용자 테스트 진행 및 결과 분석 |
| 최아영 | 센서 데이터 수집 및 Feature 알고리즘 개발, 위험도 계산 알고리즘 구현 |
| 이승주 | 전체 시스템 레이어 구조 구축, FastAPI 백엔드/PostgreSQL 연동, Dashboard 구현 |

## 시스템 구조

전체 시스템은 5개 레이어로 구성됩니다.

| 레이어 | 기술 스택 | 역할 |
|---|---|---|
| VR Client | Unity, C#, Meta XR SDK | 센서 수집, 위험도 계산, Passthrough 제어 |
| AI/ML | YOLOv8n, SORT/ByteTrack, TCN-GRU, PyTorch, ONNX, Unity Sentis | 동적 객체 인식·예측, 개인화 모델 |
| Backend | FastAPI | 세션/로그/설정 API |
| Data | PostgreSQL | 로그 및 파라미터 저장 |
| Dashboard | React | 세션 분석 시각화 |

## 핵심 기능

- **상황 인식 기반 위험 분석**: 정적 경계 충돌 위험(R_collision), 사용자 상태 위험(R_static), 동적 객체 충돌 위험(R_dynamic), 접근 의도 위험(R_intent)을 통합한 최종 위험도 산출
- **다양한 Passthrough 시각화**: Directional Passthrough, Augmented Virtuality, Volumetric Cue 등 상황별 선택적 적용
- **ML 기반 개인화**: 세션 로그 기반 암묵적 피드백 학습을 통해 사용자별 위험도 가중치 및 시각화 임계값 점진적 최적화

## 비교 평가

Baseline Guardian, Full Passthrough, Proposed Framework 세 가지를 대상으로 Collision Rate, Reaction Time, Presence Score, User Preference, Cognitive Load 지표를 기준으로 비교 평가합니다.

## 브랜치 전략

- `main`: 안정 버전
- `feature/기능명`: 신규 기능 개발
- `fix/버그명`: 버그 수정
- `docs/문서명`: 문서 작업
