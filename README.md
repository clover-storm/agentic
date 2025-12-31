# Agentic Layer

Claude Code 기반의 에이전틱 인프라 구축 프로젝트. 에이전트, 스킬, 명령어를 통해 소프트웨어 개발 워크플로우를 자동화한다.

## 구조

```
agentic/
├── .claude/
│   ├── agents/      # 역할별 에이전트 정의
│   ├── skills/      # 자동화 스킬 패키지
│   └── commands/    # CLI 명령어
├── specs/           # 스펙 문서 (자동 생성)
├── apps/            # 검증용 앱 코드
├── AGENTS.md        # 에이전트 운영 규칙
├── CLAUDE.md        # 행동 규칙/프롬프트 패턴
└── mcp.json         # MCP 연결 설정
```

## 에이전트

| 에이전트 | 역할 |
|----------|------|
| Planner | 요구사항 → plan.md, feature.md 생성 |
| API Designer | 기능 정의 → API 스펙 문서 생성 |
| Implementer | 스펙 기반 코드 스캐폴딩 |
| Tester | 테스트 케이스 생성 및 실행 |
| Reviewer | 코드 리뷰 및 PR 문서 작성 |

## 명령어

| 명령어 | 설명 |
|--------|------|
| `/new-feature` | 새 기능 계획 및 문서 생성 |
| `/new-api` | API 스펙 생성 + 코드 스캐폴드 |
| `/run-tests` | 테스트 생성 및 실행 |
| `/pre-pr` | 코드 리뷰 + PR 문서 생성 |

## 워크플로우

```
/new-feature → /new-api → (구현) → /run-tests → /pre-pr → PR
```

## 시작하기

1. 이 레포를 클론
2. Claude Code에서 프로젝트 열기
3. `/new-feature "기능 설명"` 실행

## 문서

- `agentic_workflow.md` - 전체 워크플로우 정의
- `파일럿프로젝트_플랜.md` - 파일럿 진행 계획
- `document.md` - 의사결정 기록 (ADR)

## 라이선스

MIT
