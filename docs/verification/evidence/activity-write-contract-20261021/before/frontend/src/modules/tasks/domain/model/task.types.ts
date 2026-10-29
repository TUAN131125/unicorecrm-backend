import type { RelationshipRef } from "@/platform/identity";

export type TaskStatus = "OPEN" | "COMPLETED" | "CANCELLED";
export type TaskPriority = "LOW" | "NORMAL" | "HIGH" | "URGENT";
export type ActivityType = "CALL" | "EMAIL" | "MEETING" | "NOTE" | "MESSAGE" | "SYSTEM";

export interface RecordRef {
  moduleKey: string;
  recordId: string;
  label?: string;
}

export interface TaskSourceRef {
  type: string;
  id: string;
  evidence?: string;
}

export interface Task {
  /** Backend optimistic-concurrency token. Demo/browser records may omit it. */
  resourceVersion?: number;
  id: string;
  workspaceId?: string;
  title: string;
  description?: string;
  status: TaskStatus;
  priority: TaskPriority;
  assigneeId: string;
  dueAt: string;
  completedAt?: string;
  cancelledAt?: string;
  cancellationReason?: string;
  outcome?: string;
  customerId?: string;
  relationshipRef?: RelationshipRef;
  recordRef?: RecordRef;
  sourceRef?: TaskSourceRef;
  dedupeKey?: string;
  createdAt: string;
  updatedAt: string;
  archivedAt?: string;
  archiveReason?: string;
}

export interface Activity {
  id: string;
  workspaceId?: string;
  type: ActivityType;
  subject: string;
  body?: string;
  actorId: string;
  occurredAt: string;
  customerId?: string;
  relationshipRef?: RelationshipRef;
  recordRef?: RecordRef;
  sourceRef?: TaskSourceRef;
}

export interface TaskActivitySnapshot {
  tasks: Task[];
  activities: Activity[];
}
