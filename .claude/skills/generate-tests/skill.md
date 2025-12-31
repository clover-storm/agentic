# Skill: generate-tests

구현된 코드와 API 스펙을 기반으로 테스트 케이스를 자동 생성한다.

---

## 메타데이터

```yaml
name: generate-tests
version: 1.0.0
agent: tester
trigger: /run-tests
```

---

## 입력

| 파라미터 | 필수 | 설명 |
|----------|------|------|
| target | O | 테스트 대상 (backend/frontend/all) |
| api_spec_path | X | API 스펙 경로 |
| source_path | X | 소스 코드 경로 |

---

## 출력

| 파일 | 설명 |
|------|------|
| `specs/test-be.md` | 백엔드 테스트 문서 |
| `specs/test-fe.md` | 프론트엔드 테스트 문서 |
| `apps/backend/tests/*.test.ts` | 백엔드 테스트 코드 |
| `apps/frontend/tests/*.test.ts` | 프론트엔드 테스트 코드 |

---

## 실행 흐름

```
1. 대상 분석
   ├─ API 스펙에서 테스트 케이스 도출
   └─ 소스 코드에서 테스트 포인트 식별

2. 테스트 케이스 설계
   ├─ 정상 케이스 (Happy Path)
   ├─ 에러 케이스 (Error Cases)
   ├─ 경계값 (Boundary)
   └─ 엣지 케이스 (Edge Cases)

3. 백엔드 테스트 생성 (target: backend/all)
   ├─ API 통합 테스트
   ├─ 서비스 단위 테스트
   └─ 테스트 문서 작성

4. 프론트엔드 테스트 생성 (target: frontend/all)
   ├─ 컴포넌트 테스트
   ├─ 훅 테스트
   └─ 테스트 문서 작성

5. 테스트 실행
   └─ 결과를 문서에 기록

6. 커버리지 확인
   └─ 미커버 영역 보고
```

---

## 프롬프트

```
## 역할
당신은 QA 엔지니어입니다.

## 입력
- API 스펙: {{new_api_endpoint.md}}
- 소스 코드: {{source files}}

## 작업
1. 각 엔드포인트/함수에 대한 테스트 케이스를 도출하세요
2. 정상 케이스와 에러 케이스를 모두 포함하세요
3. 경계값 테스트를 추가하세요
4. 테스트 코드를 작성하세요

## 테스트 원칙
- Arrange-Act-Assert 패턴
- 각 테스트는 독립적
- 명확한 테스트 이름
- Mock/Stub 적절히 활용

## 출력
테스트 코드 + 테스트 문서
```

---

## 예시

### 입력
```
target: backend
api_spec_path: specs/new_api_endpoint.md
```

### 출력

**specs/test-be.md**
```markdown
# 백엔드 테스트: Task API

## 테스트 범위
- POST /api/v1/tasks
- GET /api/v1/tasks
- GET /api/v1/tasks/:id
- PATCH /api/v1/tasks/:id
- DELETE /api/v1/tasks/:id

## 테스트 케이스

### POST /api/v1/tasks

| ID | 케이스 | 입력 | 예상 | 상태 |
|----|--------|------|------|------|
| TC-001 | 정상 생성 | {title: "Test"} | 201, Task 반환 | - |
| TC-002 | title 누락 | {} | 400, INVALID_TITLE | - |
| TC-003 | title 빈 문자열 | {title: ""} | 400, INVALID_TITLE | - |
| TC-004 | title 100자 초과 | {title: "a"*101} | 400, INVALID_TITLE | - |
| TC-005 | priority 유효값 | {title: "T", priority: "high"} | 201 | - |
| TC-006 | priority 무효값 | {title: "T", priority: "xxx"} | 400 | - |

### GET /api/v1/tasks

| ID | 케이스 | 입력 | 예상 | 상태 |
|----|--------|------|------|------|
| TC-101 | 전체 조회 | - | 200, 목록 | - |
| TC-102 | status 필터 | ?status=todo | 200, 필터된 목록 | - |
| TC-103 | 페이지네이션 | ?limit=10&offset=0 | 200, 10개 | - |
| TC-104 | 빈 결과 | ?status=xxx | 200, 빈 배열 | - |

## 실행 방법
\`\`\`bash
cd apps/backend && npm test
\`\`\`

## 커버리지 목표
- 라인: 80% 이상
- 브랜치: 70% 이상
```

**apps/backend/tests/task.test.ts**
```typescript
import request from 'supertest';
import app from '../src/app';

describe('Task API', () => {
  describe('POST /api/v1/tasks', () => {
    it('TC-001: 정상적인 Task 생성', async () => {
      const res = await request(app)
        .post('/api/v1/tasks')
        .send({ title: 'Test Task' });

      expect(res.status).toBe(201);
      expect(res.body).toHaveProperty('id');
      expect(res.body.title).toBe('Test Task');
      expect(res.body.status).toBe('todo');
    });

    it('TC-002: title 누락 시 400 에러', async () => {
      const res = await request(app)
        .post('/api/v1/tasks')
        .send({});

      expect(res.status).toBe(400);
      expect(res.body.code).toBe('INVALID_TITLE');
    });

    it('TC-003: title 빈 문자열 시 400 에러', async () => {
      const res = await request(app)
        .post('/api/v1/tasks')
        .send({ title: '' });

      expect(res.status).toBe(400);
    });

    it('TC-004: title 100자 초과 시 400 에러', async () => {
      const res = await request(app)
        .post('/api/v1/tasks')
        .send({ title: 'a'.repeat(101) });

      expect(res.status).toBe(400);
    });
  });

  describe('GET /api/v1/tasks', () => {
    it('TC-101: 전체 목록 조회', async () => {
      const res = await request(app).get('/api/v1/tasks');

      expect(res.status).toBe(200);
      expect(res.body).toHaveProperty('items');
      expect(res.body).toHaveProperty('total');
    });

    it('TC-102: status 필터링', async () => {
      const res = await request(app)
        .get('/api/v1/tasks')
        .query({ status: 'todo' });

      expect(res.status).toBe(200);
      res.body.items.forEach((task: any) => {
        expect(task.status).toBe('todo');
      });
    });
  });

  describe('GET /api/v1/tasks/:id', () => {
    it('존재하는 Task 조회', async () => {
      // TODO: 먼저 Task 생성 후 조회
    });

    it('존재하지 않는 Task 조회 시 404', async () => {
      const res = await request(app).get('/api/v1/tasks/non-existent');
      expect(res.status).toBe(404);
    });
  });

  describe('PATCH /api/v1/tasks/:id', () => {
    it('Task 상태 변경', async () => {
      // TODO: 먼저 Task 생성 후 수정
    });
  });

  describe('DELETE /api/v1/tasks/:id', () => {
    it('Task 삭제', async () => {
      // TODO: 먼저 Task 생성 후 삭제
    });
  });
});
```

---

## 에러 처리

| 에러 | 처리 |
|------|------|
| 스펙 없음 | 소스 코드만으로 테스트 생성 |
| 소스 없음 | 스펙 기반 테스트만 생성 |
| 테스트 실패 | 실패 내용 문서화 |

---

## 연결

- **이전 스킬**: `scaffold-endpoint`
- **다음 스킬**: `sync-docs` (또는 Reviewer에게 핸드오프)
