import type {
  ActivityMutationResponse,
  ActivityReadModel,
  CreateTaskRequest,
  LogActivityRequest,
  TaskMutationResponse,
  TaskReadModel,
} from "@/platform/api/generated/commercialApi";
import { ApiClientError } from "@/platform/api/errors";
import type {
  ActivityMutationResult,
  CreateTaskInput,
  LogActivityInput,
  TaskMutationEvidence,
  TaskMutationResult,
} from "../../application/ports/TaskApiRuntime";
import type { Activity, Task } from "../../domain/model/task.types";

export function mapTaskReadModel(dto: TaskReadModel): Task {
  return compact({
    id: dto.id,
    title: dto.title,
    description: dto.description,
    status: dto.status,
    priority: dto.priority,
    assigneeId: dto.assigneeId,
    dueAt: dto.dueAt,
    completedAt: dto.completedAt,
    cancelledAt: dto.cancelledAt,
    cancellationReason: dto.cancellationReason,
    outcome: dto.outcome,
    relationshipRef: dto.relationshipRef,
    recordRef: dto.recordRef,
    sourceRef: dto.sourceRef,
    createdAt: dto.createdAt,
    updatedAt: dto.updatedAt,
    archivedAt: dto.archivedAt,
    archiveReason: dto.archiveReason,
    resourceVersion: dto.resourceVersion,
  });
}

export function mapActivityReadModel(dto: ActivityReadModel): Activity {
  return compact({
    id: dto.id,
    type: dto.type,
    subject: dto.subject,
    body: dto.body,
    actorId: dto.actorId,
    occurredAt: dto.occurredAt,
    relationshipRef: dto.relationshipRef,
    recordRef: dto.recordRef,
    sourceRef: dto.sourceRef,
  });
}

export function mapCreateTaskRequest(input: CreateTaskInput): CreateTaskRequest {
  const title = input.title.trim();
  const assigneeId = input.assigneeId.trim();
  if (!title) throw requestViolation("createTask", "title", "Task title is required.");
  if (!assigneeId) throw requestViolation("createTask", "assigneeId", "Task assignee is required.");
  if (!input.dueAt) throw requestViolation("createTask", "dueAt", "Task dueAt is required.");
  return compact({
    title,
    description: text(input.description),
    priority: input.priority,
    assigneeId,
    dueAt: input.dueAt,
    relationshipRef: input.relationshipRef,
    recordRef: input.recordRef,
    sourceRef: input.sourceRef,
    dedupeKey: text(input.dedupeKey),
  });
}

export function mapLogActivityRequest(input: LogActivityInput): LogActivityRequest {
  if (input.occurredAt !== undefined) throw requestViolation("logActivity", "occurredAt", "The connected Activity contract does not accept a recording date.");
  const subject = input.subject.trim();
  if (!subject) throw requestViolation("logActivity", "subject", "Activity subject is required.");
  return compact({
    type: input.type,
    subject,
    body: text(input.body),
    relationshipRef: input.relationshipRef,
    recordRef: input.recordRef,
    sourceRef: input.sourceRef,
  });
}

export function mapTaskMutationResponse(response: TaskMutationResponse): TaskMutationResult {
  return { task: mapTaskReadModel(response.result.task), evidence: mapEvidence(response, "backend") };
}

export function mapActivityMutationResponse(response: ActivityMutationResponse): ActivityMutationResult {
  return { activity: mapActivityReadModel(response.result.activity), evidence: mapEvidence(response, "backend") };
}

function mapEvidence(
  response: Pick<TaskMutationResponse, "commandId" | "correlationId" | "aggregateId" | "aggregateType" | "version" | "occurredAt" | "outcome" | "warnings" | "emittedEventIds" | "auditEvidenceIds">,
  authority: TaskMutationEvidence["authority"],
): TaskMutationEvidence {
  return {
    authority,
    commandId: response.commandId,
    correlationId: response.correlationId,
    aggregateId: response.aggregateId,
    aggregateType: response.aggregateType,
    version: response.version,
    occurredAt: response.occurredAt,
    outcome: response.outcome,
    warnings: response.warnings ?? [],
    emittedEventIds: response.emittedEventIds ?? [],
    auditEvidenceIds: response.auditEvidenceIds ?? [],
  };
}

function text(value: string | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

function compact<T extends Record<string, unknown>>(value: T): T {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined)) as T;
}

function requestViolation(operationId: string, field: string, message: string): ApiClientError {
  return new ApiClientError({
    code: "CONNECTED_CONTRACT_VIOLATION",
    message,
    retryable: false,
    details: { operationId, field, authority: "docs/api/openapi.json" },
  });
}
