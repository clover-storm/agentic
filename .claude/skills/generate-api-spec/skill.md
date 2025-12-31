# Skill: generate-api-spec

기능 정의에서 RESTful API 스펙 문서를 자동 생성한다.

---

## 메타데이터

```yaml
name: generate-api-spec
version: 1.0.0
agent: api-designer
trigger: /new-api
```

---

## 입력

| 파라미터 | 필수 | 설명 |
|----------|------|------|
| feature_path | O | feature.md 경로 |
| resource | X | 리소스명 (자동 추출 가능) |

---

## 출력

| 파일 | 설명 |
|------|------|
| `specs/new_api_endpoint.md` | API 스펙 문서 |

---

## 실행 흐름

```
1. feature.md 로드
   └─ 요구사항에서 필요한 동작 추출

2. 리소스 식별
   ├─ 명사 추출 (Task, User 등)
   └─ RESTful 리소스명 결정 (/tasks, /users)

3. 엔드포인트 설계
   ├─ CRUD 매핑
   │   ├─ Create → POST /resources
   │   ├─ Read → GET /resources, GET /resources/:id
   │   ├─ Update → PATCH /resources/:id
   │   └─ Delete → DELETE /resources/:id
   └─ 추가 동작 매핑

4. 스키마 정의
   ├─ 요청 Body 스키마
   ├─ 응답 스키마
   └─ 에러 응답 스키마

5. 문서 생성
   └─ new_api_endpoint.md 작성

6. 검증
   └─ RESTful 원칙 준수 확인
```

---

## 프롬프트

```
## 역할
당신은 API 설계 전문가입니다.

## 입력
기능 정의: {{feature.md 내용}}

## 작업
1. 필요한 API 엔드포인트를 도출하세요
2. 각 엔드포인트의 요청/응답 스키마를 정의하세요
3. 가능한 에러 케이스와 상태 코드를 정의하세요
4. 보안 요구사항을 명시하세요

## 설계 원칙
- RESTful: 리소스 중심, HTTP 메서드 의미 준수
- 일관성: 네이밍, 응답 구조 통일
- 완전성: 모든 케이스 커버

## 출력 형식
new_api_endpoint.md 템플릿에 맞게 작성
```

---

## 예시

### 입력
```
feature_path: specs/feature.md
```

**feature.md 내용:**
```markdown
# 기능: Task CRUD
사용자가 할 일을 추가, 조회, 완료, 삭제할 수 있다.
```

### 출력

**specs/new_api_endpoint.md**
```markdown
# API: Task 관리

## 엔드포인트 목록

| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | /api/v1/tasks | Task 생성 |
| GET | /api/v1/tasks | Task 목록 조회 |
| GET | /api/v1/tasks/:id | Task 단건 조회 |
| PATCH | /api/v1/tasks/:id | Task 수정 |
| DELETE | /api/v1/tasks/:id | Task 삭제 |

---

## POST /api/v1/tasks

### 설명
새로운 Task를 생성한다.

### 요청
```json
{
  "title": "string (필수, 1-100자)",
  "description": "string (선택)",
  "priority": "low | medium | high (기본: medium)"
}
```

### 응답 (201 Created)
```json
{
  "id": "uuid",
  "title": "string",
  "description": "string | null",
  "priority": "medium",
  "status": "todo",
  "createdAt": "2025-01-01T00:00:00Z",
  "updatedAt": "2025-01-01T00:00:00Z"
}
```

### 에러
| 상태 | 코드 | 조건 |
|------|------|------|
| 400 | INVALID_TITLE | title 누락 또는 길이 초과 |

---

## GET /api/v1/tasks

### 설명
Task 목록을 조회한다.

### Query Parameters
| 파라미터 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| status | string | - | 상태 필터 (todo/in_progress/done) |
| limit | number | 20 | 페이지 크기 |
| offset | number | 0 | 시작 위치 |

### 응답 (200 OK)
```json
{
  "items": [Task],
  "total": 100,
  "limit": 20,
  "offset": 0
}
```

---

## PATCH /api/v1/tasks/:id

### 설명
Task를 수정한다. 상태 변경 포함.

### 요청
```json
{
  "title": "string (선택)",
  "description": "string (선택)",
  "status": "todo | in_progress | done (선택)"
}
```

### 응답 (200 OK)
수정된 Task 객체

### 에러
| 상태 | 코드 | 조건 |
|------|------|------|
| 404 | NOT_FOUND | Task 없음 |

---

## DELETE /api/v1/tasks/:id

### 설명
Task를 삭제한다.

### 응답 (204 No Content)
(빈 응답)

### 에러
| 상태 | 코드 | 조건 |
|------|------|------|
| 404 | NOT_FOUND | Task 없음 |
```

---

## 에러 처리

| 에러 | 처리 |
|------|------|
| feature.md 없음 | 에러 메시지 반환 |
| 동작 불명확 | 기본 CRUD로 가정 |

---

## 연결

- **이전 스킬**: `generate-plan`
- **다음 스킬**: `scaffold-endpoint`
