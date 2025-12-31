# Agentic 인프라 기반 프로젝트 진행 워크플로우

이 문서는 이미지에 보이는 파일 구조를 기준으로, 에이전틱 인프라가 프로젝트 코드를 둘러싸는 방식을 정리하고 실제 업무 진행 흐름을 정의한다.

## 1) 구조 개요
- **핵심 제어 문서**: `AGENTS.md`, `CLAUDE.md`, `mcp.json`, `prime_w_tools.md`
- **프로젝트 진행 문서**: `plan.md`, `build.md`, `pull_ticket.md`, `document.md`
- **작업 타입별 산출물**: `feature.md`, `bug.md`, `chore.md`
- **실험/재현/테스트**: `reproduce.md`, `test-fe.md`, `test-be.md`, `test_new_api_endpoint.md`
- **API 변경 추적**: `new_api_endpoint.md`, `update_api_endpoint.md`
- **운영/구동**: `start-apps.md`
- **리뷰**: `review.md`, `code-review.md`
- **에이전트/스킬**: `agents/`, `skills/`

## 2) 파일별 역할 요약
- `AGENTS.md`: 에이전트 운영 규칙/역할/협업 방식의 기준 문서.
- `CLAUDE.md`: 모델/에이전트 프롬프트 또는 행동 규칙의 기준.
- `mcp.json`: MCP 서버/리소스 연결 정의.
- `prime_w_tools.md`: 에이전트 부트스트랩(도구 사용 가이드).
- `plan.md`: 범위/목표/단계/리스크를 담는 실행 계획.
- `feature.md`: 기능 개발 단위의 요구사항/완료 조건.
- `bug.md`: 버그 재현/원인/해결/회귀 방지.
- `chore.md`: 리팩터링/정비/의존성 업데이트.
- `document.md`: 설계 및 의사결정 기록(ADR 포함).
- `reproduce.md`: 이슈 재현 절차와 환경.
- `new_api_endpoint.md`: 신규 API 정의(요청/응답/에러).
- `update_api_endpoint.md`: 기존 API 변경사항(버전/호환성).
- `test-fe.md` / `test-be.md`: 프론트/백엔드 테스트 전략과 결과.
- `test_new_api_endpoint.md`: 신규 API 전용 테스트 케이스.
- `build.md`: 빌드/패키징/배포 절차.
- `review.md`: 기능/요구사항 검증 리뷰.
- `code-review.md`: 코드 품질/리스크 중심 리뷰.
- `pull_ticket.md`: PR/머지 요청 설명 및 체크리스트.
- `start-apps.md`: 로컬/스테이징 실행 방법.
- `agents/`: 역할별 에이전트 정의 및 운영 스크립트.
- `skills/`: 에이전트가 사용하는 스킬 패키지.

## 3) 기본 워크플로우 (권장 순서)
1. **부트스트랩**
   - `AGENTS.md`, `CLAUDE.md`, `mcp.json`, `prime_w_tools.md` 확인.
   - 에이전트/도구 접근 범위와 제약을 확정.
2. **계획 수립**
   - `plan.md`에 목표/범위/일정/리스크/검증 기준을 작성.
3. **작업 타입 결정**
   - 기능이면 `feature.md`, 버그면 `bug.md`, 정비면 `chore.md`를 사용.
4. **설계/정의**
   - 요구사항 및 의사결정은 `document.md`에 기록.
   - 신규 API는 `new_api_endpoint.md`, 변경은 `update_api_endpoint.md`.
   - 재현 가능한 이슈는 `reproduce.md`에 절차를 명시.
5. **구현/테스트**
   - 프론트 테스트는 `test-fe.md`, 백엔드는 `test-be.md`.
   - 신규 API는 `test_new_api_endpoint.md`로 별도 검증.
6. **빌드/검증**
   - `build.md`에 빌드 명령, 환경, 산출물 위치 기록.
7. **리뷰**
   - 결과물 품질은 `review.md`, 코드 품질은 `code-review.md`.
8. **PR/머지**
   - `pull_ticket.md`에 변경 요약, 테스트 결과, 리스크를 작성.
9. **운영/구동**
   - `start-apps.md`에 실행 절차를 유지.

## 4) 문서 작성 규칙 (간단 규약)
- 한 작업당 하나의 주요 문서를 주 문서로 지정(예: `feature.md`).
- 산출물 간 참조는 파일명으로 연결(예: `feature.md` → `test-fe.md`).
- 체크리스트는 최소화하고, 실패 조건/회귀 조건을 명시.
- 외부 링크는 `document.md`에만 기록.

## 5) 최소 템플릿 (필수 섹션)
- `plan.md`: 목적, 범위, 마일스톤, 리스크, 검증 기준
- `feature.md`: 요구사항, 완료 조건, 영향 범위, 테스트 계획
- `bug.md`: 재현 절차, 원인, 수정 내용, 회귀 테스트
- `new_api_endpoint.md`: 엔드포인트, 요청/응답, 에러, 보안 고려사항
- `test-fe.md` / `test-be.md`: 테스트 범위, 케이스, 결과, 미해결
- `code-review.md`: 위험 요소, 복잡도, 스타일/안전성 이슈
- `pull_ticket.md`: 변경 요약, 테스트 결과, 배포 영향

## 6) 운영 원칙
- 에이전트는 `AGENTS.md`를 단일 진실 원천으로 삼는다.
- 프로세스 변경은 `document.md`에 기록 후 `plan.md`에 반영.
- API 변경은 반드시 `update_api_endpoint.md`와 테스트 문서를 함께 갱신.

## 7) Claude CLI 기반 에이전트 레이어 구축 (추가)
이 구조를 Claude CLI로 구현할 때의 기본 설계를 정리한다.

### 디렉터리 레이아웃 (예시)
- `.claude/`: 에이전트 레이어의 루트
  - `agents/`: 역할별 에이전트 정의
  - `commands/`: 자주 쓰는 CLI 명령/루틴
  - `skills/`: 도메인/워크플로우 스킬 패키지
- `ai_docs/`: 에이전트가 참조하는 지식 문서
- `specs/`: 요구사항/명세 문서
- `apps/`, `scripts/`: 실제 코드와 실행 스크립트 (앱 레이어)
- `mcp.json`: MCP 서버 연결
- `AGENTS.md`, `CLAUDE.md`: 행동 규칙과 프롬프트 기준

### 구축 절차 (권장 순서)
1. **에이전트 레이어 폴더 생성**
   - `.claude/agents`, `.claude/commands`, `.claude/skills`를 생성한다.
2. **기준 문서 배치**
   - `AGENTS.md`와 `CLAUDE.md`를 작성해 에이전트 역할, 협업 규칙, 금지사항을 정의한다.
3. **MCP 연결 구성**
   - `mcp.json`에 필요한 외부 도구/데이터 소스를 등록한다.
4. **스킬 캡슐화**
   - 반복되는 업무(예: DB 마이그레이션, 배포 시작/중지)를 `skills/`로 분리한다.
5. **운영 문서 정리**
   - `ai_docs/`와 `specs/`에 설계 및 정책을 저장하고, `plan.md`와 연동한다.

### 운영 원칙
- 에이전트 레이어 변경은 `document.md`에 기록하고, `AGENTS.md`에 반영한다.
- CLI 명령은 `commands/`에 축적해 재사용성을 높인다.
- 스킬은 진행 컨텍스트를 최소로 유지하도록 설계한다.
