# Command: /pre-pr

PR 생성 전 리뷰를 수행하고 PR 문서를 자동 생성한다.

---

## 사용법

```
/pre-pr                       # 전체 리뷰 + PR 문서 생성
/pre-pr --review-only         # 리뷰만 수행
/pre-pr --quick               # 빠른 검토 (보안/주요 이슈만)
```

---

## 예시

```
/pre-pr                            # 코드 리뷰 + PR 문서 생성
/pre-pr --review-only              # 리뷰만, PR 문서 생성 안함
/pre-pr --base=develop             # develop 브랜치 기준
/pre-pr --quick                    # 핵심 이슈만 빠르게 검토
```

---

## 실행 흐름

```
1. 변경사항 수집
   ├─ git diff 분석
   ├─ 변경 파일 목록 추출
   └─ 관련 스펙 문서 로드

2. Reviewer 에이전트 활성화
   └─ .claude/agents/reviewer.md 참조

3. 코드 리뷰 수행
   ├─ 보안 취약점 검사
   ├─ 성능 이슈 확인
   ├─ 코드 스타일 검토
   └─ 테스트 커버리지 확인

4. 기능 리뷰 수행
   ├─ 요구사항 충족 여부
   └─ 스펙 일치 여부

5. 문서 생성
   ├─ specs/code-review.md
   ├─ specs/review.md
   └─ specs/pull_ticket.md

6. 결과 보고
   ├─ 이슈 요약 (CRITICAL/MAJOR/MINOR)
   ├─ PR 준비 상태
   └─ 다음 단계 안내
```

---

## 생성되는 문서

### specs/code-review.md
```markdown
# 코드 리뷰

## 전체 판정
APPROVED | CHANGES_REQUESTED | COMMENT

## 발견된 이슈

### CRITICAL
[즉시 수정 필요]

### MAJOR
[머지 전 수정 필요]

### MINOR
[권장 수정]

## 잘된 점
[칭찬할 부분]
```

### specs/review.md
```markdown
# 기능 리뷰

## 요구사항 충족 여부
| 요구사항 | 구현 | 테스트 | 상태 |
|----------|------|--------|------|
| ... | O/X | O/X | PASS/FAIL |

## 결론
[전체 평가]
```

### specs/pull_ticket.md
```markdown
# Pull Request

## 제목
[type]: [설명]

## 변경 요약
[2-3문장 요약]

## 변경 사항
- [변경 1]
- [변경 2]

## 테스트 결과
- 단위 테스트: PASS
- 통합 테스트: PASS

## 체크리스트
- [ ] 코드가 스펙과 일치
- [ ] 테스트 통과
- [ ] 문서 갱신
- [ ] 보안 이슈 없음
```

---

## 연결 명령어

| 순서 | 명령어 | 설명 |
|------|--------|------|
| 이전 | `/run-tests` | 테스트 완료 |
| 현재 | `/pre-pr` | PR 준비 |
| 다음 | (git push + PR 생성) | 수동 |

---

## 옵션

| 옵션 | 설명 | 기본값 |
|------|------|--------|
| --review-only | 리뷰만, PR 문서 생성 안함 | false |
| --quick | 빠른 검토 모드 | false |
| --base | 비교 기준 브랜치 | main |
| --strict | 엄격 모드 (MINOR도 블로킹) | false |

---

## 리뷰 기준

### 보안 검사
- [ ] SQL Injection 가능성
- [ ] XSS 취약점
- [ ] 인증/인가 누락
- [ ] 민감 정보 노출

### 성능 검사
- [ ] N+1 쿼리
- [ ] 불필요한 연산
- [ ] 메모리 누수 가능성

### 품질 검사
- [ ] 코드 중복
- [ ] 복잡도 (함수 길이, 중첩)
- [ ] 네이밍 일관성
- [ ] 에러 처리

---

## 판정 기준

| 판정 | 조건 |
|------|------|
| APPROVED | CRITICAL/MAJOR 이슈 없음 |
| CHANGES_REQUESTED | CRITICAL 또는 MAJOR 이슈 존재 |
| COMMENT | MINOR 이슈만 존재 (머지 가능) |

---

## 체크리스트

명령어 실행 후 확인:
- [ ] 모든 CRITICAL 이슈가 해결되었는가?
- [ ] 모든 MAJOR 이슈가 해결되었는가?
- [ ] 테스트가 모두 통과했는가?
- [ ] PR 문서가 완성되었는가?
- [ ] 변경사항이 정확히 요약되었는가?
