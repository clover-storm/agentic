# API: Task 관리

> 이 문서는 `/new-api` 명령어에 의해 자동 생성되었습니다.
>
> - 생성일: 2025-01-01
> - 담당 에이전트: API Designer
> - 입력: `specs/feature.md`
> - 다음 단계: 구현 또는 `/run-tests`

---

## 엔드포인트 목록

| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | /api/v1/tasks | Task 생성 |
| GET | /api/v1/tasks | Task 목록 조회 |
| GET | /api/v1/tasks/:id | Task 단건 조회 |
| PATCH | /api/v1/tasks/:id | Task 수정 |
| DELETE | /api/v1/tasks/:id | Task 삭제 |

---

## 공통 사항

### Base URL
```
/api/v1
```

### 공통 에러 응답
```json
{
  "code": "ERROR_CODE",
  "message": "Human readable message"
}
```

### 공통 헤더
| 헤더 | 필수 | 설명 |
|------|------|------|
| Content-Type | O | application/json |

---

## POST /api/v1/tasks

### 설명
새로운 Task를 생성한다.

### 요청 Body
```json
{
  "title": "string (필수, 1-100자)",
  "description": "string (선택, 최대 500자)",
  "priority": "low | medium | high (선택, 기본: medium)"
}
```

### 응답

#### 성공 (201 Created)
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "title": "API 설계하기",
  "description": "Task API 스펙 작성",
  "priority": "high",
  "status": "todo",
  "createdAt": "2025-01-01T00:00:00.000Z",
  "updatedAt": "2025-01-01T00:00:00.000Z"
}
```

#### 에러
| 상태 | 코드 | 조건 |
|------|------|------|
| 400 | INVALID_TITLE | title 누락, 빈 문자열, 100자 초과 |
| 400 | INVALID_PRIORITY | priority가 유효값이 아님 |

### 예시
```bash
curl -X POST http://localhost:3000/api/v1/tasks \
  -H "Content-Type: application/json" \
  -d '{"title": "API 설계하기", "priority": "high"}'
```

---

## GET /api/v1/tasks

### 설명
Task 목록을 조회한다. 필터링과 페이지네이션을 지원한다.

### Query Parameters
| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
|----------|------|------|--------|------|
| status | string | X | - | 상태 필터 (todo/in_progress/done) |
| priority | string | X | - | 우선순위 필터 |
| limit | number | X | 20 | 페이지 크기 (1-100) |
| offset | number | X | 0 | 시작 위치 |

### 응답

#### 성공 (200 OK)
```json
{
  "items": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "title": "API 설계하기",
      "description": null,
      "priority": "high",
      "status": "todo",
      "createdAt": "2025-01-01T00:00:00.000Z",
      "updatedAt": "2025-01-01T00:00:00.000Z"
    }
  ],
  "total": 1,
  "limit": 20,
  "offset": 0
}
```

### 예시
```bash
# 전체 조회
curl http://localhost:3000/api/v1/tasks

# 상태 필터
curl "http://localhost:3000/api/v1/tasks?status=todo"

# 페이지네이션
curl "http://localhost:3000/api/v1/tasks?limit=10&offset=0"
```

---

## GET /api/v1/tasks/:id

### 설명
특정 Task를 조회한다.

### Path Parameters
| 파라미터 | 타입 | 설명 |
|----------|------|------|
| id | string | Task UUID |

### 응답

#### 성공 (200 OK)
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "title": "API 설계하기",
  "description": null,
  "priority": "high",
  "status": "todo",
  "createdAt": "2025-01-01T00:00:00.000Z",
  "updatedAt": "2025-01-01T00:00:00.000Z"
}
```

#### 에러
| 상태 | 코드 | 조건 |
|------|------|------|
| 404 | NOT_FOUND | Task가 존재하지 않음 |

---

## PATCH /api/v1/tasks/:id

### 설명
Task를 수정한다. 상태 변경도 이 API로 처리한다.

### Path Parameters
| 파라미터 | 타입 | 설명 |
|----------|------|------|
| id | string | Task UUID |

### 요청 Body
```json
{
  "title": "string (선택)",
  "description": "string (선택)",
  "priority": "low | medium | high (선택)",
  "status": "todo | in_progress | done (선택)"
}
```

### 응답

#### 성공 (200 OK)
수정된 Task 객체 반환

#### 에러
| 상태 | 코드 | 조건 |
|------|------|------|
| 400 | INVALID_TITLE | title이 빈 문자열 또는 100자 초과 |
| 400 | INVALID_STATUS | status가 유효값이 아님 |
| 404 | NOT_FOUND | Task가 존재하지 않음 |

### 예시
```bash
# 상태 변경
curl -X PATCH http://localhost:3000/api/v1/tasks/550e8400-... \
  -H "Content-Type: application/json" \
  -d '{"status": "done"}'
```

---

## DELETE /api/v1/tasks/:id

### 설명
Task를 삭제한다.

### Path Parameters
| 파라미터 | 타입 | 설명 |
|----------|------|------|
| id | string | Task UUID |

### 응답

#### 성공 (204 No Content)
빈 응답

#### 에러
| 상태 | 코드 | 조건 |
|------|------|------|
| 404 | NOT_FOUND | Task가 존재하지 않음 |

---

## 참조
- `specs/feature.md` - 기능 정의
- `.claude/agents/api-designer.md` - 에이전트 정의
- `.claude/skills/generate-api-spec/skill.md` - 스킬 정의
