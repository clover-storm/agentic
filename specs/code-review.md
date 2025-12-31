# 코드 리뷰: Task CRUD

> 이 문서는 `/pre-pr` 명령어에 의해 자동 생성되었습니다.
>
> - 리뷰 일시: 2025-01-01
> - 담당 에이전트: Reviewer
> - 리뷰 대상: apps/backend/src/

---

## 전체 판정

**APPROVED**

CRITICAL 및 MAJOR 이슈 없음. 머지 가능.

---

## 변경 파일

| 파일 | 변경 | 상태 |
|------|------|------|
| src/types/task.ts | +35 | OK |
| src/services/task.service.ts | +65 | OK |
| src/controllers/task.controller.ts | +80 | OK |
| src/routes/task.routes.ts | +20 | OK |
| src/app.ts | +20 | OK |
| tests/task.test.ts | +150 | OK |

---

## 발견된 이슈

### CRITICAL
없음

### MAJOR
없음

### MINOR

#### [CR-001] In-memory 저장소 사용
- **파일**: src/services/task.service.ts:10
- **내용**: Map을 사용한 in-memory 저장소
- **제안**: TODO 주석이 있으므로 인지된 상태. 추후 DB 연동 필요
- **심각도**: MINOR (파일럿이므로 허용)

#### [CR-002] 에러 타입 미정의
- **파일**: src/controllers/task.controller.ts
- **내용**: catch 블록에서 error 타입이 any
- **제안**: 커스텀 에러 클래스 정의 고려
- **심각도**: MINOR

### SUGGESTION

#### [CR-003] 입력 검증 분리
- **파일**: src/controllers/task.controller.ts
- **내용**: 컨트롤러에서 직접 검증 수행
- **제안**: 별도 validator 미들웨어 또는 DTO 검증 라이브러리(zod, class-validator) 사용 고려
- **이유**: 재사용성 및 테스트 용이성

---

## 잘된 점

1. **일관된 에러 응답**: 모든 에러가 `{code, message}` 형식
2. **레이어 분리**: types → services → controllers → routes 구조 명확
3. **테스트 커버리지**: 모든 엔드포인트에 정상/에러 케이스 테스트
4. **API 스펙 준수**: new_api_endpoint.md와 구현이 일치

---

## 보안 체크리스트

- [x] SQL Injection: 해당 없음 (in-memory)
- [x] XSS: 해당 없음 (API only)
- [x] 인증/인가: 범위 외 (추후 구현)
- [x] 민감 정보 노출: 없음

---

## 성능 체크리스트

- [x] N+1 쿼리: 해당 없음
- [x] 불필요한 연산: 없음
- [x] 메모리 누수: 없음

---

## 결론

Task CRUD API가 스펙대로 구현되었으며, 테스트도 충분히 작성되었습니다.
in-memory 저장소는 파일럿 목적에 적합하며, 실제 프로젝트 적용 시 DB 연동이 필요합니다.

---

## 참조
- `specs/feature.md` - 기능 정의
- `specs/new_api_endpoint.md` - API 스펙
- `specs/test-be.md` - 테스트 결과
