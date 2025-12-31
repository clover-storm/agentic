# 기능: Task CRUD

> 이 문서는 `/new-feature "Task CRUD"` 명령어에 의해 자동 생성되었습니다.
>
> - 생성일: 2025-01-01
> - 담당 에이전트: Planner
> - 다음 단계: `/new-api`

---

## 요구사항

사용자가 할 일(Task)을 추가하고, 목록을 보고, 상태를 변경하고, 삭제할 수 있다.

### 사용자 스토리
1. 사용자로서, 새로운 할 일을 추가하고 싶다
2. 사용자로서, 내 할 일 목록을 보고 싶다
3. 사용자로서, 할 일의 상태를 변경하고 싶다 (시작/완료)
4. 사용자로서, 완료된 할 일을 삭제하고 싶다

### 데이터 모델
```typescript
interface Task {
  id: string;           // UUID
  title: string;        // 1-100자
  description?: string; // 선택, 최대 500자
  priority: 'low' | 'medium' | 'high';  // 기본: medium
  status: 'todo' | 'in_progress' | 'done';  // 기본: todo
  createdAt: Date;
  updatedAt: Date;
}
```

---

## 완료 조건

### 기능 완료
- [ ] POST /api/v1/tasks - Task 생성
- [ ] GET /api/v1/tasks - Task 목록 조회
- [ ] GET /api/v1/tasks/:id - Task 단건 조회
- [ ] PATCH /api/v1/tasks/:id - Task 수정
- [ ] DELETE /api/v1/tasks/:id - Task 삭제

### 품질 완료
- [ ] 모든 API에 입력 검증 적용
- [ ] 에러 응답 형식 통일
- [ ] 테스트 커버리지 80% 이상

---

## 영향 범위

### 파일 (예상)
```
apps/backend/src/
├── types/task.ts
├── services/task.service.ts
├── controllers/task.controller.ts
├── routes/task.routes.ts
└── app.ts (라우터 등록)

apps/backend/tests/
└── task.test.ts
```

### API
- 신규: `/api/v1/tasks` (CRUD)

### UI
- 현재 범위 외 (백엔드만 구현)

---

## 테스트 계획

### 단위 테스트
- TaskService 각 메서드

### 통합 테스트
- 각 API 엔드포인트
- 정상 케이스 + 에러 케이스

### E2E 테스트
- Task 생성 → 조회 → 상태 변경 → 삭제 시나리오

---

## 참조
- `specs/plan.md` - 전체 계획
- `.claude/agents/planner.md` - 에이전트 정의
- `.claude/skills/generate-plan/skill.md` - 스킬 정의
