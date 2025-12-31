# Pull Request

> 이 문서는 `/pre-pr` 명령어에 의해 자동 생성되었습니다.

---

## 제목

feat: Task CRUD API 구현

---

## 변경 요약

Task(할 일) 관리를 위한 CRUD API를 구현했습니다. 사용자는 Task를 생성, 조회, 수정, 삭제할 수 있으며, 상태(todo/in_progress/done) 및 우선순위(low/medium/high) 관리가 가능합니다.

---

## 변경 사항

### 신규 파일
- `apps/backend/src/types/task.ts` - Task 타입 정의
- `apps/backend/src/services/task.service.ts` - 비즈니스 로직
- `apps/backend/src/controllers/task.controller.ts` - 요청 처리
- `apps/backend/src/routes/task.routes.ts` - 라우트 정의
- `apps/backend/src/app.ts` - Express 앱 설정
- `apps/backend/tests/task.test.ts` - API 테스트

### 신규 API
| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | /api/v1/tasks | Task 생성 |
| GET | /api/v1/tasks | Task 목록 조회 |
| GET | /api/v1/tasks/:id | Task 단건 조회 |
| PATCH | /api/v1/tasks/:id | Task 수정 |
| DELETE | /api/v1/tasks/:id | Task 삭제 |

---

## 관련 문서

- `specs/feature.md` - 기능 정의
- `specs/new_api_endpoint.md` - API 스펙
- `specs/test-be.md` - 테스트 케이스
- `specs/code-review.md` - 코드 리뷰 결과
- `specs/review.md` - 기능 리뷰 결과

---

## 테스트 결과

| 유형 | 결과 | 상세 |
|------|------|------|
| 단위 테스트 | PASS | 18/18 |
| 통합 테스트 | PASS | 18/18 |
| 커버리지 | 94% | 목표 80% 초과 |

```
Test Suites: 1 passed, 1 total
Tests:       18 passed, 18 total
```

---

## 체크리스트

### 구현
- [x] 코드가 스펙과 일치함
- [x] 입력 검증 적용됨
- [x] 에러 처리 완료
- [x] 타입 정의 완료

### 테스트
- [x] 정상 케이스 테스트
- [x] 에러 케이스 테스트
- [x] 경계값 테스트

### 문서
- [x] API 스펙 문서 작성
- [x] 테스트 문서 작성
- [x] 코드 리뷰 완료

### 품질
- [x] 보안 이슈 없음
- [x] 성능 이슈 없음
- [x] 코드 스타일 일관성

---

## 스크린샷

(API 전용이므로 해당 없음)

---

## 배포 영향

- [ ] 마이그레이션 필요 - 해당 없음 (in-memory)
- [ ] 환경 변수 추가 - 해당 없음
- [ ] 다운타임 예상 - 해당 없음

---

## 롤백 계획

신규 기능이므로 라우트 제거로 롤백 가능:
```typescript
// app.ts에서 주석 처리
// app.use('/api/v1/tasks', taskRoutes);
```

---

## 리뷰어

- [x] Code Review: APPROVED
- [x] Feature Review: PASS

---

*이 PR은 에이전틱 레이어를 통해 자동 생성되었습니다.*
