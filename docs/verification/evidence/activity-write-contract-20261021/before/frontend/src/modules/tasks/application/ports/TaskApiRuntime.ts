import type { AuthoritativePage, ModuleListQuery } from "@/shared/application";
import type { Activity, ActivityType, RecordRef, Task, TaskPriority, TaskSourceRef } from "../../domain/model/task.types";
import type { RelationshipRef } from "@/platform/identity";

export type TaskApiRuntimeMode = "demo" | "connected" | "test";

export interface TaskListQuery extends ModuleListQuery {
  filters?: {
    status?: Task["status"];
    priority?: TaskPriority;
    assigneeId?: string;
    relationshipType?: RelationshipRef["type"];
    relationshipId?: string;
    recordModuleKey?: string;
    recordId?: string;
    overdueAt?: string;
  };
}

export interface ActivityListQuery extends ModuleListQuery {
  filters?: {
    type?: ActivityType;
    actorId?: string;
    relationshipType?: RelationshipRef["type"];
    relationshipId?: string;
    recordModuleKey?: string;
    recordId?: string;
    occurredFrom?: string;
    occurredTo?: string;
  };
}

export interface CreateTaskInput {
  title: string;
  description?: string;
  priority?: TaskPriority;
  assigneeId: string;
  dueAt: string;
  relationshipRef?: RelationshipRef;
  recordRef?: RecordRef;
  sourceRef?: TaskSourceRef;
  dedupeKey?: string;
}

export interface CompleteTaskInput { outcome: string; }
export interface CancelTaskInput { reason: string; }
export interface AssignTaskInput { assigneeId: string; }
export interface RescheduleTaskInput { dueAt: string; }
export interface ArchiveTaskInput { reason: string; }
export interface LogActivityInput {
  /** Demo recording date; the connected contract currently owns its server timestamp. */
  occurredAt?: string;
  type: ActivityType;
  subject: string;
  body?: string;
  relationshipRef?: RelationshipRef;
  recordRef?: RecordRef;
  sourceRef?: TaskSourceRef;
}

export interface TaskCommandOptions {
  idempotencyKey: string;
  correlationId?: string;
  signal?: AbortSignal;
}

export interface TaskVersionedCommandOptions extends TaskCommandOptions {
  expectedVersion: number;
}

export interface TaskMutationEvidence {
  authority: "backend" | "demo" | "test";
  commandId: string;
  correlationId: string;
  aggregateId: string;
  aggregateType: string;
  version: number;
  occurredAt: string;
  outcome: "COMMITTED" | "REPLAYED" | "DEMO_COMMITTED";
  warnings: readonly string[];
  emittedEventIds: readonly string[];
  auditEvidenceIds: readonly string[];
}

export interface TaskMutationResult { task: Task; evidence: TaskMutationEvidence; }
export interface ActivityMutationResult { activity: Activity; evidence: TaskMutationEvidence; }

export interface TaskQueryPort {
  list(query?: TaskListQuery, signal?: AbortSignal): Promise<AuthoritativePage<Task>>;
  get(taskId: string, signal?: AbortSignal): Promise<Task>;
  listActivities(query?: ActivityListQuery, signal?: AbortSignal): Promise<AuthoritativePage<Activity>>;
}

export interface TaskCommandPort {
  createTask(input: CreateTaskInput, options: TaskCommandOptions): Promise<TaskMutationResult>;
  completeTask(taskId: string, input: CompleteTaskInput, options: TaskVersionedCommandOptions): Promise<TaskMutationResult>;
  cancelTask(taskId: string, input: CancelTaskInput, options: TaskVersionedCommandOptions): Promise<TaskMutationResult>;
  assignTask(taskId: string, input: AssignTaskInput, options: TaskVersionedCommandOptions): Promise<TaskMutationResult>;
  rescheduleTask(taskId: string, input: RescheduleTaskInput, options: TaskVersionedCommandOptions): Promise<TaskMutationResult>;
  archiveTask(taskId: string, input: ArchiveTaskInput, options: TaskVersionedCommandOptions): Promise<TaskMutationResult>;
  logActivity(input: LogActivityInput, options: TaskCommandOptions): Promise<ActivityMutationResult>;
}

export interface TaskApiRuntime {
  mode: TaskApiRuntimeMode;
  queries: TaskQueryPort;
  commands: TaskCommandPort;
}
