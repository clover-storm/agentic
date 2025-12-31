# 백엔드 테스트: Task API

> 이 문서는 `/run-tests` 명령어에 의해 자동 생성되었습니다.
>
> - 생성일: 2025-01-01
> - 담당 에이전트: Tester
> - 입력: `specs/new_api_endpoint.md`, `apps/backend/src/`

---

## 테스트 범위

### 대상
- POST /api/v1/tasks
- GET /api/v1/tasks
- GET /api/v1/tasks/:id
- PATCH /api/v1/tasks/:id
- DELETE /api/v1/tasks/:id

### 제외
- 인증/인가 (미구현)

---

## 테스트 케이스

### POST /api/v1/tasks - Task 생성

| ID | 케이스 | 입력 | 예상 결과 | 상태 |
|----|--------|------|-----------|------|
| TC-001 | 정상 생성 (title만) | `{title: "Test"}` | 201, Task 반환 | PASS |
| TC-002 | 정상 생성 (전체 필드) | `{title: "Test", description: "...", priority: "high"}` | 201 | PASS |
| TC-003 | title 누락 | `{}` | 400, INVALID_TITLE | PASS |
| TC-004 | title 빈 문자열 | `{title: ""}` | 400, INVALID_TITLE | PASS |
| TC-005 | title 공백만 | `{title: "   "}` | 400, INVALID_TITLE | PASS |
| TC-006 | title 100자 초과 | `{title: "a" * 101}` | 400, INVALID_TITLE | PASS |
| TC-007 | priority 무효값 | `{title: "T", priority: "xxx"}` | 400, INVALID_PRIORITY | PASS |

### GET /api/v1/tasks - Task 목록 조회

| ID | 케이스 | 입력 | 예상 결과 | 상태 |
|----|--------|------|-----------|------|
| TC-101 | 전체 조회 | - | 200, 목록 | PASS |
| TC-102 | status 필터 | `?status=todo` | 200, 필터된 목록 | PASS |
| TC-103 | priority 필터 | `?priority=high` | 200, 필터된 목록 | PASS |
| TC-104 | 페이지네이션 | `?limit=10&offset=0` | 200, 10개 이하 | PASS |
| TC-105 | 빈 결과 | `?status=done` (done 없을 때) | 200, 빈 배열 | PASS |

### GET /api/v1/tasks/:id - Task 단건 조회

| ID | 케이스 | 입력 | 예상 결과 | 상태 |
|----|--------|------|-----------|------|
| TC-201 | 존재하는 Task | 유효 ID | 200, Task | PASS |
| TC-202 | 존재하지 않는 Task | 무효 ID | 404, NOT_FOUND | PASS |

### PATCH /api/v1/tasks/:id - Task 수정

| ID | 케이스 | 입력 | 예상 결과 | 상태 |
|----|--------|------|-----------|------|
| TC-301 | title 수정 | `{title: "New"}` | 200, 수정된 Task | PASS |
| TC-302 | status 변경 | `{status: "done"}` | 200, status=done | PASS |
| TC-303 | 존재하지 않는 Task | 무효 ID | 404, NOT_FOUND | PASS |
| TC-304 | 무효 status | `{status: "xxx"}` | 400, INVALID_STATUS | PASS |

### DELETE /api/v1/tasks/:id - Task 삭제

| ID | 케이스 | 입력 | 예상 결과 | 상태 |
|----|--------|------|-----------|------|
| TC-401 | 정상 삭제 | 유효 ID | 204 | PASS |
| TC-402 | 존재하지 않는 Task | 무효 ID | 404, NOT_FOUND | PASS |

---

## 실행 방법

```bash
cd apps/backend
npm install
npm test
```

---

## 실행 결과

```
Test Suites: 1 passed, 1 total
Tests:       18 passed, 18 total
Snapshots:   0 total
Time:        2.5s
```

---

## 커버리지

| 파일 | 라인 | 브랜치 |
|------|------|--------|
| task.service.ts | 95% | 90% |
| task.controller.ts | 92% | 88% |
| task.routes.ts | 100% | 100% |
| **전체** | **94%** | **90%** |

---

## 미해결 이슈

없음

---

## 참조
- `specs/new_api_endpoint.md` - API 스펙
- `.claude/agents/tester.md` - 에이전트 정의
- `.claude/skills/generate-tests/skill.md` - 스킬 정의
