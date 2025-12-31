# Command: /run-tests

테스트 케이스를 생성하고 실행한다.

---

## 사용법

```
/run-tests                    # 전체 테스트
/run-tests --target=backend   # 백엔드만
/run-tests --target=frontend  # 프론트엔드만
/run-tests --generate         # 테스트 코드 생성만
/run-tests --run              # 기존 테스트 실행만
```

---

## 예시

```
/run-tests                          # 테스트 생성 + 실행
/run-tests --target=backend         # 백엔드 테스트만
/run-tests --generate               # 테스트 코드만 생성
/run-tests --coverage               # 커버리지 리포트 포함
```

---

## 실행 흐름

```
1. 대상 분석
   ├─ API 스펙 로드 (specs/new_api_endpoint.md)
   └─ 소스 코드 분석 (apps/)

2. Tester 에이전트 활성화
   └─ .claude/agents/tester.md 참조

3. generate-tests 스킬 실행
   ├─ 테스트 케이스 도출
   ├─ 테스트 코드 생성
   └─ 테스트 문서 작성

4. 테스트 실행
   ├─ 백엔드: npm test (apps/backend)
   └─ 프론트엔드: npm test (apps/frontend)

5. 결과 보고
   ├─ 통과/실패 요약
   ├─ 커버리지 (옵션)
   └─ 실패 시 reproduce.md 생성 안내
```

---

## 생성되는 문서/코드

### specs/test-be.md
```markdown
# 백엔드 테스트

## 테스트 케이스
| ID | 케이스 | 예상 결과 | 상태 |
|----|--------|-----------|------|
| TC-001 | ... | ... | PASS/FAIL |

## 실행 결과
- 통과: X개
- 실패: Y개
- 커버리지: Z%
```

### specs/test-fe.md
```markdown
# 프론트엔드 테스트

## 테스트 케이스
[테스트 목록]

## 실행 결과
[결과 요약]
```

### 테스트 코드
```
apps/backend/tests/
└── {resource}.test.ts

apps/frontend/tests/
└── {Component}.test.tsx
```

---

## 연결 명령어

| 순서 | 명령어 | 설명 |
|------|--------|------|
| 이전 | `/new-api --scaffold` | 코드 구현 |
| 현재 | `/run-tests` | 테스트 |
| 다음 | `/pre-pr` | PR 준비 |

---

## 옵션

| 옵션 | 설명 | 기본값 |
|------|------|--------|
| --target | 테스트 대상 (backend/frontend/all) | all |
| --generate | 테스트 코드 생성만 | false |
| --run | 기존 테스트 실행만 | false |
| --coverage | 커버리지 리포트 | false |
| --watch | 워치 모드 | false |

---

## 테스트 유형

| 유형 | 설명 | 도구 |
|------|------|------|
| 단위 테스트 | 함수/컴포넌트 단위 | Jest/Vitest |
| 통합 테스트 | API 엔드포인트 | Supertest |
| E2E 테스트 | 사용자 시나리오 | Playwright |

---

## 실패 시 처리

테스트 실패 시:
1. 실패 내용을 `specs/test-*.md`에 기록
2. 버그로 판단되면 `specs/reproduce.md` 생성 제안
3. `/new-feature --type=bug` 안내

---

## 체크리스트

명령어 실행 후 확인:
- [ ] 테스트 문서가 갱신되었는가?
- [ ] 모든 API 엔드포인트가 테스트되었는가?
- [ ] 에러 케이스가 포함되었는가?
- [ ] 테스트가 독립적으로 실행 가능한가?
- [ ] 실패한 테스트의 원인이 문서화되었는가?
