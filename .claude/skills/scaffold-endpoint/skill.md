# Skill: scaffold-endpoint

API 스펙을 기반으로 백엔드 코드 뼈대를 자동 생성한다.

---

## 메타데이터

```yaml
name: scaffold-endpoint
version: 1.0.0
agent: implementer
trigger: (generate-api-spec 완료 후 자동)
```

---

## 입력

| 파라미터 | 필수 | 설명 |
|----------|------|------|
| api_spec_path | O | new_api_endpoint.md 경로 |
| framework | X | 프레임워크 (express/fastapi), 자동 감지 |

---

## 출력

| 파일 | 설명 |
|------|------|
| `apps/backend/src/routes/{resource}.ts` | 라우트 정의 |
| `apps/backend/src/controllers/{resource}.ts` | 컨트롤러 |
| `apps/backend/src/services/{resource}.ts` | 비즈니스 로직 |
| `apps/backend/src/types/{resource}.ts` | 타입 정의 |

---

## 실행 흐름

```
1. API 스펙 파싱
   ├─ 엔드포인트 목록 추출
   ├─ 요청/응답 스키마 추출
   └─ 리소스명 결정

2. 프레임워크 감지
   ├─ package.json 확인
   └─ 기존 코드 패턴 분석

3. 디렉터리 구조 생성
   └─ routes/, controllers/, services/, types/

4. 타입 정의 생성
   └─ 스키마 → TypeScript 인터페이스

5. 서비스 스캐폴드
   └─ CRUD 메서드 뼈대

6. 컨트롤러 스캐폴드
   └─ 요청 처리 + 서비스 호출

7. 라우트 정의
   └─ 엔드포인트 → 컨트롤러 매핑

8. index 파일 갱신
   └─ 라우터 등록
```

---

## 프롬프트

```
## 역할
당신은 백엔드 개발자입니다.

## 입력
API 스펙: {{new_api_endpoint.md 내용}}
프레임워크: {{framework}}

## 작업
1. API 스펙에서 필요한 엔드포인트를 파악하세요
2. 타입/인터페이스를 정의하세요
3. 서비스 레이어 뼈대를 작성하세요
4. 컨트롤러 뼈대를 작성하세요
5. 라우트를 정의하세요

## 코드 원칙
- 타입 안전성 확보
- 에러 처리 포함
- TODO 주석으로 구현 필요 부분 표시
- 기존 코드 패턴 준수

## 출력
각 파일의 전체 코드
```

---

## 예시 (Express + TypeScript)

### 입력
```
api_spec_path: specs/new_api_endpoint.md
framework: express
```

### 출력

**apps/backend/src/types/task.ts**
```typescript
export interface Task {
  id: string;
  title: string;
  description: string | null;
  priority: 'low' | 'medium' | 'high';
  status: 'todo' | 'in_progress' | 'done';
  createdAt: Date;
  updatedAt: Date;
}

export interface CreateTaskDto {
  title: string;
  description?: string;
  priority?: 'low' | 'medium' | 'high';
}

export interface UpdateTaskDto {
  title?: string;
  description?: string;
  status?: 'todo' | 'in_progress' | 'done';
}

export interface TaskListQuery {
  status?: string;
  limit?: number;
  offset?: number;
}
```

**apps/backend/src/services/task.service.ts**
```typescript
import { Task, CreateTaskDto, UpdateTaskDto, TaskListQuery } from '../types/task';

export class TaskService {
  async create(dto: CreateTaskDto): Promise<Task> {
    // TODO: 구현
    throw new Error('Not implemented');
  }

  async findAll(query: TaskListQuery): Promise<{ items: Task[]; total: number }> {
    // TODO: 구현
    throw new Error('Not implemented');
  }

  async findById(id: string): Promise<Task | null> {
    // TODO: 구현
    throw new Error('Not implemented');
  }

  async update(id: string, dto: UpdateTaskDto): Promise<Task | null> {
    // TODO: 구현
    throw new Error('Not implemented');
  }

  async delete(id: string): Promise<boolean> {
    // TODO: 구현
    throw new Error('Not implemented');
  }
}

export const taskService = new TaskService();
```

**apps/backend/src/controllers/task.controller.ts**
```typescript
import { Request, Response, NextFunction } from 'express';
import { taskService } from '../services/task.service';

export class TaskController {
  async create(req: Request, res: Response, next: NextFunction) {
    try {
      const task = await taskService.create(req.body);
      res.status(201).json(task);
    } catch (error) {
      next(error);
    }
  }

  async findAll(req: Request, res: Response, next: NextFunction) {
    try {
      const result = await taskService.findAll(req.query);
      res.json(result);
    } catch (error) {
      next(error);
    }
  }

  async findById(req: Request, res: Response, next: NextFunction) {
    try {
      const task = await taskService.findById(req.params.id);
      if (!task) {
        return res.status(404).json({ code: 'NOT_FOUND' });
      }
      res.json(task);
    } catch (error) {
      next(error);
    }
  }

  async update(req: Request, res: Response, next: NextFunction) {
    try {
      const task = await taskService.update(req.params.id, req.body);
      if (!task) {
        return res.status(404).json({ code: 'NOT_FOUND' });
      }
      res.json(task);
    } catch (error) {
      next(error);
    }
  }

  async delete(req: Request, res: Response, next: NextFunction) {
    try {
      const deleted = await taskService.delete(req.params.id);
      if (!deleted) {
        return res.status(404).json({ code: 'NOT_FOUND' });
      }
      res.status(204).send();
    } catch (error) {
      next(error);
    }
  }
}

export const taskController = new TaskController();
```

**apps/backend/src/routes/task.routes.ts**
```typescript
import { Router } from 'express';
import { taskController } from '../controllers/task.controller';

const router = Router();

router.post('/', (req, res, next) => taskController.create(req, res, next));
router.get('/', (req, res, next) => taskController.findAll(req, res, next));
router.get('/:id', (req, res, next) => taskController.findById(req, res, next));
router.patch('/:id', (req, res, next) => taskController.update(req, res, next));
router.delete('/:id', (req, res, next) => taskController.delete(req, res, next));

export default router;
```

---

## 에러 처리

| 에러 | 처리 |
|------|------|
| API 스펙 없음 | 에러 반환 |
| 프레임워크 미지원 | Express 기본값 |
| 디렉터리 충돌 | 기존 파일 백업 후 생성 |

---

## 연결

- **이전 스킬**: `generate-api-spec`
- **다음 스킬**: `generate-tests`
