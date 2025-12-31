# AGENTS.md - 에이전트 운영 규칙

이 문서는 에이전틱 레이어의 모든 에이전트가 따르는 단일 진실 원천(Single Source of Truth)이다.

---

## 1. 에이전트 목록 및 역할

| 에이전트 | 파일 | 역할 | 트리거 |
|----------|------|------|--------|
| Planner | `.claude/agents/planner.md` | 요구사항 분석 → plan.md, feature.md 생성 | 새 작업 시작 |
| API Designer | `.claude/agents/api-designer.md` | 기능 정의 → API 스펙 문서 생성 | API 설계 필요 시 |
| Implementer | `.claude/agents/implementer.md` | 스펙 기반 코드 스캐폴딩 및 구현 | 구현 단계 |
| Tester | `.claude/agents/tester.md` | 테스트 케이스 생성 및 실행 | 구현 완료 후 |
| Reviewer | `.claude/agents/reviewer.md` | 코드 리뷰 및 PR 문서 작성 | PR 전 |
| Documenter | `.claude/agents/documenter.md` | 변경사항 → 문서 동기화 | 커밋 후 |

---

## 2. 협업 규칙

### 2.1 작업 흐름
```
[요구사항] → Planner → API Designer → Implementer → Tester → Reviewer
                ↓                                        ↓
            plan.md                               code-review.md
            feature.md                            pull_ticket.md
```

### 2.2 문서 소유권
- 각 에이전트는 자신의 담당 문서만 생성/수정한다
- 다른 에이전트의 문서를 수정해야 할 경우, 해당 에이전트에게 위임한다

| 에이전트 | 소유 문서 |
|----------|-----------|
| Planner | `plan.md`, `feature.md`, `bug.md`, `chore.md` |
| API Designer | `new_api_endpoint.md`, `update_api_endpoint.md` |
| Implementer | 소스 코드 (`apps/`) |
| Tester | `test-be.md`, `test-fe.md`, `reproduce.md` |
| Reviewer | `review.md`, `code-review.md`, `pull_ticket.md` |
| Documenter | `document.md`, `start-apps.md`, `build.md` |

### 2.3 핸드오프 프로토콜
1. 현재 에이전트는 작업 완료 시 담당 문서를 갱신한다
2. 다음 에이전트에게 필요한 컨텍스트를 문서에 명시한다
3. 다음 에이전트는 이전 문서를 읽고 작업을 시작한다

---

## 3. 제약사항

### 3.1 금지 행위
- [ ] 사용자 확인 없이 외부 API 호출
- [ ] 민감 정보(API 키, 비밀번호)를 문서에 기록
- [ ] `specs/` 외부에 스펙 문서 생성
- [ ] 테스트 없이 구현 완료 선언

### 3.2 필수 행위
- [x] 모든 작업은 `plan.md`에 기록된 범위 내에서 수행
- [x] 변경사항은 반드시 관련 문서에 반영
- [x] 에러 발생 시 `reproduce.md`에 재현 절차 기록
- [x] 코드 변경 전 테스트 케이스 확인

---

## 4. 스킬 사용 규칙

### 4.1 스킬 호출 조건
| 스킬 | 호출 조건 | 호출 주체 |
|------|-----------|-----------|
| `generate-plan` | 새 기능/버그/정비 작업 시작 | Planner |
| `generate-api-spec` | API 엔드포인트 필요 시 | API Designer |
| `scaffold-endpoint` | API 스펙 확정 후 | Implementer |
| `generate-tests` | 구현 완료 후 | Tester |
| `run-review` | PR 생성 전 | Reviewer |
| `sync-docs` | 커밋 완료 후 | Documenter |

### 4.2 스킬 체이닝
스킬은 순차적으로 실행되며, 이전 스킬의 출력이 다음 스킬의 입력이 된다.
```
generate-plan → generate-api-spec → scaffold-endpoint → generate-tests → run-review
```

---

## 5. 명령어 매핑

| 명령어 | 실행 에이전트 | 실행 스킬 |
|--------|---------------|-----------|
| `/new-feature` | Planner | `generate-plan` |
| `/new-api` | API Designer | `generate-api-spec` + `scaffold-endpoint` |
| `/run-tests` | Tester | `generate-tests` |
| `/pre-pr` | Reviewer | `run-review` |
| `/start` | Documenter | (start-apps.md 기반 실행) |

---

## 6. 에러 처리

### 6.1 에러 발생 시
1. 현재 작업을 중단하고 에러를 기록
2. `reproduce.md`에 재현 절차 작성
3. 사용자에게 에러 보고 및 다음 단계 확인

### 6.2 복구 절차
1. 에러 원인 분석 (`bug.md` 작성)
2. 수정 후 테스트 (`test-*.md` 갱신)
3. 정상 확인 후 워크플로우 재개

---

## 7. 버전 및 변경 이력

| 버전 | 날짜 | 변경 내용 |
|------|------|-----------|
| 0.1 | 2025-01-01 | 초기 버전 작성 |

---

## 참조
- `CLAUDE.md`: 행동 규칙 및 프롬프트 패턴
- `agentic_workflow.md`: 전체 워크플로우 정의
- `.claude/agents/`: 개별 에이전트 상세 정의
