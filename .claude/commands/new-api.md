# Command: /new-api

API 엔드포인트를 설계하고 코드 뼈대를 생성한다.

---

## 사용법

```
/new-api                      # feature.md 기반 자동 생성
/new-api --resource=tasks     # 리소스명 지정
/new-api --scaffold           # 코드 스캐폴드까지 생성
```

---

## 예시

```
/new-api                           # specs/feature.md 읽고 API 설계
/new-api --resource=users          # users 리소스 API 설계
/new-api --scaffold                # API 설계 + 코드 뼈대 생성
/new-api --scaffold --framework=fastapi  # FastAPI로 생성
```

---

## 실행 흐름

```
1. 기능 정의 로드
   └─ specs/feature.md 읽기

2. API Designer 에이전트 활성화
   └─ .claude/agents/api-designer.md 참조

3. generate-api-spec 스킬 실행
   ├─ 입력: feature.md
   └─ 출력: specs/new_api_endpoint.md

4. (--scaffold 옵션 시) scaffold-endpoint 스킬 실행
   ├─ 입력: new_api_endpoint.md
   └─ 출력: apps/backend/src/* 코드 파일

5. 결과 보고
   ├─ 생성된 엔드포인트 목록
   ├─ 생성된 파일 목록 (scaffold 시)
   └─ 다음 단계 안내
```

---

## 생성되는 문서/코드

### specs/new_api_endpoint.md
```markdown
# API: [리소스명]

## 엔드포인트 목록
| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | /api/v1/resources | 생성 |
| GET | /api/v1/resources | 목록 조회 |
...

## [각 엔드포인트 상세]
- 요청 스키마
- 응답 스키마
- 에러 케이스
```

### (--scaffold 시) 코드 파일
```
apps/backend/src/
├── types/{resource}.ts        # 타입 정의
├── services/{resource}.service.ts  # 비즈니스 로직
├── controllers/{resource}.controller.ts  # 요청 처리
└── routes/{resource}.routes.ts  # 라우트 정의
```

---

## 연결 명령어

| 순서 | 명령어 | 설명 |
|------|--------|------|
| 이전 | `/new-feature` | 기능 정의 |
| 현재 | `/new-api` | API 설계 |
| 다음 | `/run-tests` | 테스트 생성 |

---

## 옵션

| 옵션 | 설명 | 기본값 |
|------|------|--------|
| --resource | 리소스명 지정 | 자동 추출 |
| --scaffold | 코드 뼈대 생성 | false |
| --framework | 백엔드 프레임워크 | express |
| --update | 기존 API 수정 모드 | false |

---

## 프레임워크 지원

| 프레임워크 | 언어 | 상태 |
|------------|------|------|
| Express | TypeScript | 지원 |
| FastAPI | Python | 지원 |
| NestJS | TypeScript | 예정 |
| Gin | Go | 예정 |

---

## 체크리스트

명령어 실행 후 확인:
- [ ] API 스펙 문서가 생성되었는가?
- [ ] RESTful 원칙을 준수하는가?
- [ ] 모든 에러 케이스가 정의되었는가?
- [ ] (scaffold 시) 코드가 에러 없이 생성되었는가?
- [ ] (scaffold 시) 기존 코드 패턴과 일관성이 있는가?
