import { getTaskReadAuthorityScope } from "../application/vertical-slice/taskAuthoritativeQueries";
import { getWorkspaceContextSnapshot } from "@/platform/workspace-context";
import {
  LocalMutationAuthority,
  MutationCommandError,
  createMutationMetadata,
  getMutationAuthority,
  runBackendProjection,
  invalidateModuleQueries,
  type MutationCommandMetadata,
  type MutationOutcome,
} from "@/shared/application";
import { createTask, completeTask, cancelTask, archiveTask, logActivity, reassignTask, rescheduleTask } from "../application/commands/taskCommands";
import { getActivitiesForCustomer, getActivitiesForRecord, getOverdueTasks, getTaskStats, getTasksForCustomer, getTasksForRecord, queryTasks } from "../application/queries/taskQueries";
import { getTaskApiRuntime, taskActivityRepository } from "../application/composition/taskApplicationServices";
import type { LeadHandoverTaskSnapshotCommand, LeadHandoverTaskSnapshotProof } from "../application/ports/TaskActivityRepository";
import type { Activity, Task } from "../domain/model/task.types";
import type { ActivityMutationResult, TaskMutationEvidence, TaskMutationResult } from "../application/ports/TaskApiRuntime";

export type { Activity, ActivityType, RecordRef, Task, TaskActivitySnapshot, TaskPriority, TaskSourceRef, TaskStatus } from "../domain/model/task.types";
export type {
  ActivityListQuery,
  ActivityMutationResult,
  ArchiveTaskInput,
  AssignTaskInput,
  CancelTaskInput,
  CompleteTaskInput,
  CreateTaskInput,
  LogActivityInput,
  RescheduleTaskInput,
  TaskApiRuntime,
  TaskCommandOptions,
  TaskListQuery,
  TaskMutationEvidence,
  TaskMutationResult,
  TaskVersionedCommandOptions,
} from "../application/ports/TaskApiRuntime";
export { canTransitionTask, getAllowedTaskTransitions } from "../domain/rules/taskLifecycle";
export type { TaskActivityRepository } from "../application/ports/TaskActivityRepository";
export { getTaskApiRuntime } from "../application/composition/taskApplicationServices";

function assertDemoTaskMutationAllowed(operation: string): void {
  if (getMutationAuthority() instanceof LocalMutationAuthority) return;
  throw new MutationCommandError({
    code: "CONNECTED_COMMAND_REQUIRES_ASYNC_AUTHORITY",
    message: `${operation} is a demo-only synchronous snapshot mutation. Use the corresponding async Task command in connected mode.`,
    category: "INFRASTRUCTURE",
    retryable: false,
    details: { operation, authority: "docs/api/openapi.json", decisionId: "DEC-TASK-API-BOUNDARY" },
  });
}

function commandMetadata(prefix: string, metadata: Partial<MutationCommandMetadata>): MutationCommandMetadata {
  return createMutationMetadata(prefix, metadata);
}

function versionedMetadata(taskId: string, operation: string, metadata: Partial<MutationCommandMetadata>): MutationCommandMetadata & { expectedVersion: number } {
  const expectedVersion = metadata.expectedVersion ?? getTaskSnapshot(taskId)?.resourceVersion;
  if (typeof expectedVersion !== "number") {
    throw new MutationCommandError({
      code: "TASK_RESOURCE_VERSION_REQUIRED",
      message: `${operation} requires an authoritative Task resource version.`,
      category: "CONFLICT",
      retryable: false,
      details: { taskId, operation, authority: "docs/api/openapi.json" },
    });
  }
  return { ...commandMetadata(`task.${operation}:${taskId}`, metadata), expectedVersion };
}

export const getTaskActivitySnapshot = () => {
  const snapshot = taskActivityRepository.snapshot();
  return { ...snapshot, tasks: snapshot.tasks.filter((task) => !task.archivedAt) };
};
export const getRetainedTaskActivitySnapshot = () => taskActivityRepository.snapshot();
export const getTaskSnapshot = (taskId: string) => taskActivityRepository.findTaskById(taskId);
export const subscribeToTaskActivity = (listener: Parameters<typeof taskActivityRepository.subscribe>[0]) => taskActivityRepository.subscribe((snapshot) => listener({ ...snapshot, tasks: snapshot.tasks.filter((task) => !task.archivedAt) }));

export const createTaskCommand = async (
  command: Omit<Parameters<typeof createTask>[1], "workspaceId">,
  metadata: Partial<MutationCommandMetadata> = {},
  transport: { signal?: AbortSignal } = {},
): Promise<MutationOutcome<Task>> => {
  const capturedAuthority = getTaskReadAuthorityScope();
  const assertAuthority = () => { if (getTaskApiRuntime().mode === "connected" && (capturedAuthority === undefined || capturedAuthority !== getTaskReadAuthorityScope())) throw new DOMException("Task creation authority retired.", "AbortError"); transport.signal?.throwIfAborted(); };
  assertAuthority();
  const options = commandMetadata(`task.create:${command.id}`, metadata);
  const result = await getTaskApiRuntime().commands.createTask({
    title: command.title,
    assigneeId: command.assigneeId,
    dueAt: command.dueAt,
    ...(command.description !== undefined ? { description: command.description } : {}),
    ...(command.priority !== undefined ? { priority: command.priority } : {}),
    ...(command.relationshipRef !== undefined ? { relationshipRef: command.relationshipRef } : {}),
    ...(command.recordRef !== undefined ? { recordRef: command.recordRef } : {}),
    ...(command.sourceRef !== undefined ? { sourceRef: command.sourceRef } : {}),
    ...(command.dedupeKey !== undefined ? { dedupeKey: command.dedupeKey } : {}),
  }, { ...options, ...(transport.signal ? { signal: transport.signal } : {}) });
  assertAuthority();
  projectTask(result.task);
  return mutationOutcome("task.create", options, result.evidence, result.task, true, assertAuthority);
};

export const completeTaskCommand = async (taskId: string, input: Parameters<typeof completeTask>[2], metadata: Partial<MutationCommandMetadata> = {}): Promise<MutationOutcome<Task>> => {
  const options = versionedMetadata(taskId, "complete", metadata);
  const result = await getTaskApiRuntime().commands.completeTask(taskId, { outcome: input.outcome }, options);
  projectTask(result.task);
  return taskOutcome("task.complete", options, result);
};

export const cancelTaskCommand = async (taskId: string, input: Parameters<typeof cancelTask>[2], metadata: Partial<MutationCommandMetadata> = {}): Promise<MutationOutcome<Task>> => {
  const options = versionedMetadata(taskId, "cancel", metadata);
  const result = await getTaskApiRuntime().commands.cancelTask(taskId, { reason: input.reason }, options);
  projectTask(result.task);
  return taskOutcome("task.cancel", options, result);
};

export const reassignTaskCommand = async (taskId: string, input: Parameters<typeof reassignTask>[2], metadata: Partial<MutationCommandMetadata> = {}): Promise<MutationOutcome<Task>> => {
  const options = versionedMetadata(taskId, "assign", metadata);
  const result = await getTaskApiRuntime().commands.assignTask(taskId, { assigneeId: input.assigneeId }, options);
  projectTask(result.task);
  return taskOutcome("task.assign", options, result);
};

export const rescheduleTaskCommand = async (taskId: string, input: Parameters<typeof rescheduleTask>[2], metadata: Partial<MutationCommandMetadata> = {}): Promise<MutationOutcome<Task>> => {
  const options = versionedMetadata(taskId, "reschedule", metadata);
  const result = await getTaskApiRuntime().commands.rescheduleTask(taskId, { dueAt: input.dueAt }, options);
  projectTask(result.task);
  return taskOutcome("task.reschedule", options, result);
};

export const archiveTaskCommand = async (taskId: string, input: Parameters<typeof archiveTask>[2], metadata: Partial<MutationCommandMetadata> = {}): Promise<MutationOutcome<Task>> => {
  const options = versionedMetadata(taskId, "archive", metadata);
  const result = await getTaskApiRuntime().commands.archiveTask(taskId, { reason: input.reason }, options);
  projectTask(result.task);
  return taskOutcome("task.archive", options, result);
};

export const logActivityCommand = async (
  command: Omit<Parameters<typeof logActivity>[1], "workspaceId">,
  metadata: Partial<MutationCommandMetadata> = {},
): Promise<MutationOutcome<Activity>> => {
  const api = getTaskApiRuntime();
  if (command.occurredAt !== undefined) {
    if (Number.isNaN(new Date(command.occurredAt).getTime())) throw new Error("ACTIVITY_DATE_INVALID");
    // LogActivityRequest has no date field. Refuse a dated draft before transport rather
    // than silently replace its validated date with the server's recording timestamp.
    if (api.mode === "connected") throw new Error("ACTIVITY_RECORDING_DATE_UNAVAILABLE");
  }
  const options = commandMetadata(`task.log-activity:${command.id}`, metadata);
  const result = await api.commands.logActivity({
    ...(command.occurredAt !== undefined ? { occurredAt: new Date(command.occurredAt).toISOString() } : {}),
    type: command.type,
    subject: command.subject,
    ...(command.body !== undefined ? { body: command.body } : {}),
    ...(command.relationshipRef !== undefined ? { relationshipRef: command.relationshipRef } : {}),
    ...(command.recordRef !== undefined ? { recordRef: command.recordRef } : {}),
    ...(command.sourceRef !== undefined ? { sourceRef: command.sourceRef } : {}),
  }, options);
  projectActivity(result.activity);
  return activityOutcome("task.log-activity", options, result);
};

export interface LoggedActivityResult { activity: Activity; }

export const logActivityViaApi = async (
  command: Omit<Parameters<typeof logActivity>[1], "workspaceId">,
  metadata: Partial<MutationCommandMetadata> = {},
): Promise<MutationOutcome<LoggedActivityResult>> => {
  const outcome = await logActivityCommand(command, metadata);
  return { ...outcome, data: { activity: outcome.data } };
};

/** Tasks-owned demo participant. Connected mode never uses this local command. */
export function handoverLeadTasksSnapshot(command: LeadHandoverTaskSnapshotCommand,
  onCommitted: (proof: LeadHandoverTaskSnapshotProof) => void): void {
  assertDemoTaskMutationAllowed("handoverLeadTasksSnapshot");
  if (!taskActivityRepository.handoverLeadTasks) throw new Error("Demo Tasks handover participant unavailable.");
  taskActivityRepository.handoverLeadTasks(command, onCommitted);
}

/** Demo-only synchronous mutation bridge. Connected mode fails closed. */
export const createTaskSnapshot = (command: Omit<Parameters<typeof createTask>[1], "workspaceId">) => { assertDemoTaskMutationAllowed("createTaskSnapshot"); return createTask(taskActivityRepository, { ...command, workspaceId: getWorkspaceContextSnapshot().workspaceId }); };
export const completeTaskSnapshot = (taskId: string, input: Parameters<typeof completeTask>[2]) => { assertDemoTaskMutationAllowed("completeTaskSnapshot"); return completeTask(taskActivityRepository, taskId, input); };
export const cancelTaskSnapshot = (taskId: string, input: Parameters<typeof cancelTask>[2]) => { assertDemoTaskMutationAllowed("cancelTaskSnapshot"); return cancelTask(taskActivityRepository, taskId, input); };
export const reassignTaskSnapshot = (taskId: string, input: Parameters<typeof reassignTask>[2]) => { assertDemoTaskMutationAllowed("reassignTaskSnapshot"); return reassignTask(taskActivityRepository, taskId, input); };
export const rescheduleTaskSnapshot = (taskId: string, input: Parameters<typeof rescheduleTask>[2]) => { assertDemoTaskMutationAllowed("rescheduleTaskSnapshot"); return rescheduleTask(taskActivityRepository, taskId, input); };
export const logActivitySnapshot = (command: Omit<Parameters<typeof logActivity>[1], "workspaceId">) => { assertDemoTaskMutationAllowed("logActivitySnapshot"); return logActivity(taskActivityRepository, { ...command, workspaceId: getWorkspaceContextSnapshot().workspaceId }); };

export const queryTaskSnapshot = (input?: Parameters<typeof queryTasks>[1]) => queryTasks(taskActivityRepository, input);
export const getOverdueTaskSnapshot = (now?: Date) => getOverdueTasks(taskActivityRepository, now);
export const getTasksForRecordSnapshot = (moduleKey: string, recordId: string) => getTasksForRecord(taskActivityRepository, moduleKey, recordId);
export const getActivitiesForRecordSnapshot = (moduleKey: string, recordId: string) => getActivitiesForRecord(taskActivityRepository, moduleKey, recordId);
export const getTaskStatsSnapshot = () => getTaskStats(taskActivityRepository);
export const getTasksForCustomerSnapshot = (customerId: string) => getTasksForCustomer(taskActivityRepository, customerId);
export const getActivitiesForCustomerSnapshot = (customerId: string) => getActivitiesForCustomer(taskActivityRepository, customerId);
/** Cache/read-model projection, not a production mutation authority. */
export const replaceTaskActivitySnapshot = (snapshot: Parameters<typeof taskActivityRepository.replace>[0]) => taskActivityRepository.replace(snapshot);

function projectTask(task: Task): void {
  runBackendProjection("tasks", () => taskActivityRepository.saveTask(task));
}

function projectActivity(activity: Activity): void {
  runBackendProjection("tasks", () => taskActivityRepository.saveActivity(activity));
}

function taskOutcome(commandType: string, options: MutationCommandMetadata, result: TaskMutationResult): Promise<MutationOutcome<Task>> {
  return mutationOutcome(commandType, options, result.evidence, result.task);
}

function activityOutcome(commandType: string, options: MutationCommandMetadata, result: ActivityMutationResult): Promise<MutationOutcome<Activity>> {
  return mutationOutcome(commandType, options, result.evidence, result.activity);
}

async function mutationOutcome<T>(commandType: string, options: MutationCommandMetadata, evidence: TaskMutationEvidence, data: T, committedRefreshWarning = false, assertAuthority: () => void = () => {}): Promise<MutationOutcome<T>> {
  const warnings = [...evidence.warnings];
  try { await invalidateModuleQueries({ moduleKeys: ["tasks"], commandType, aggregateId: evidence.aggregateId, occurredAt: evidence.occurredAt }); }
  catch (error) { if (!committedRefreshWarning) throw error; warnings.push("TASK_COMMITTED_READ_REFRESH_FAILED"); }
  assertAuthority();
  return {
    data,
    commandId: evidence.commandId,
    commandType,
    aggregateType: evidence.aggregateType,
    aggregateId: evidence.aggregateId,
    idempotencyKey: options.idempotencyKey,
    correlationId: evidence.correlationId,
    occurredAt: evidence.occurredAt,
    version: evidence.version,
    outcome: evidence.outcome,
    warnings,
    emittedEvents: [...evidence.emittedEventIds],
    audit: { authority: evidence.authority === "backend" ? "backend" : "demo", evidenceIds: [...evidence.auditEvidenceIds] },
  };
}
