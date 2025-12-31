# Skill: sync-docs

코드 변경사항을 감지하여 관련 문서를 자동으로 동기화한다.

---

## 메타데이터

```yaml
name: sync-docs
version: 1.0.0
agent: documenter
trigger: (커밋 후 자동), /sync-docs
```

---

## 입력

| 파라미터 | 필수 | 설명 |
|----------|------|------|
| changes | O | 변경된 파일 목록 또는 git diff |
| scope | X | 동기화 범위 (all/api/tests) |

---

## 출력

| 파일 | 조건 |
|------|------|
| `specs/new_api_endpoint.md` | API 코드 변경 시 |
| `specs/update_api_endpoint.md` | 기존 API 변경 시 |
| `specs/test-be.md` | 테스트 코드 변경 시 |
| `specs/test-fe.md` | 테스트 코드 변경 시 |
| `specs/start-apps.md` | 설정/구동 방식 변경 시 |
| `document.md` | 주요 의사결정 발생 시 |

---

## 실행 흐름

```
1. 변경 감지
   ├─ git diff 분석
   └─ 변경 파일 분류

2. 영향 범위 결정
   ├─ routes/* → API 문서
   ├─ tests/* → 테스트 문서
   ├─ config/* → 구동 문서
   └─ src/* → 설계 문서

3. 문서별 동기화
   ├─ 기존 문서 로드
   ├─ 변경사항 반영
   └─ 갱신된 문서 저장

4. 변경 요약 생성
   └─ 동기화된 내용 보고

5. 검증
   └─ 문서-코드 일치 확인
```

---

## 프롬프트

```
## 역할
당신은 기술 문서 작성자입니다.

## 입력
변경된 파일:
{{changes}}

## 작업
1. 변경된 파일이 어떤 문서에 영향을 주는지 파악하세요
2. 해당 문서를 코드와 일치하도록 갱신하세요
3. 변경 내용을 요약하세요

## 동기화 규칙
- API 변경 → API 스펙 문서 갱신
- 테스트 추가/수정 → 테스트 문서 갱신
- 설정 변경 → 구동 문서 갱신
- 주요 로직 변경 → document.md에 기록

## 출력
갱신된 문서 + 변경 요약
```

---

## 동기화 매핑

| 코드 변경 | 갱신 문서 | 갱신 내용 |
|-----------|-----------|-----------|
| `routes/*.ts` | `new_api_endpoint.md` | 엔드포인트 추가/변경 |
| `controllers/*.ts` | `new_api_endpoint.md` | 요청/응답 변경 |
| `services/*.ts` | `document.md` | 비즈니스 로직 변경 |
| `tests/*.test.ts` | `test-be.md` | 테스트 케이스 추가 |
| `package.json` | `start-apps.md` | 의존성/스크립트 변경 |
| `.env.example` | `start-apps.md` | 환경 변수 추가 |

---

## 예시

### 입력
```
changes:
  - apps/backend/src/routes/task.routes.ts (modified)
  - apps/backend/src/services/task.service.ts (modified)
  - apps/backend/tests/task.test.ts (added)
```

### 출력

**동기화 보고:**
```markdown
## 문서 동기화 결과

### 갱신된 문서
1. `specs/new_api_endpoint.md`
   - Task API 라우트 변경사항 반영

2. `specs/test-be.md`
   - 새 테스트 케이스 3개 추가

3. `document.md`
   - TaskService 로직 변경 기록

### 변경 요약
- Task API에 새로운 필터 옵션 추가
- 관련 테스트 케이스 작성 완료
```

**document.md 추가 내용:**
```markdown
## 2025-01-01: TaskService 변경

### 변경 내용
- `findAll` 메서드에 `priority` 필터 추가
- 정렬 옵션 지원 (`sortBy`, `order`)

### 이유
사용자 요청에 따라 우선순위별 필터링 기능 추가

### 영향
- API 응답 구조 변경 없음 (하위 호환)
- 쿼리 파라미터 추가: `priority`, `sortBy`, `order`
```

---

## 자동 감지 패턴

```yaml
# 파일 패턴 → 문서 매핑
patterns:
  - pattern: "routes/**/*.ts"
    docs: ["new_api_endpoint.md"]

  - pattern: "controllers/**/*.ts"
    docs: ["new_api_endpoint.md"]

  - pattern: "services/**/*.ts"
    docs: ["document.md"]

  - pattern: "tests/**/*.test.ts"
    docs: ["test-be.md", "test-fe.md"]

  - pattern: "*.config.*"
    docs: ["start-apps.md", "build.md"]

  - pattern: ".env*"
    docs: ["start-apps.md"]
```

---

## 에러 처리

| 에러 | 처리 |
|------|------|
| 문서 없음 | 새로 생성 |
| 충돌 감지 | 수동 확인 요청 |
| 동기화 실패 | 변경 내용만 보고 |

---

## 연결

- **이전 스킬**: 모든 구현/테스트 스킬
- **다음 스킬**: (없음 - 마무리 단계)
- **트리거**: 커밋 훅 또는 수동 실행
