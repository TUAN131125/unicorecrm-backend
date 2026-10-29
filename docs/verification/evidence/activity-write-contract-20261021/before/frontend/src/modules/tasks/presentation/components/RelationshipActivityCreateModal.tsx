import React from "react";
import { isTaskConnectedApiRuntime } from "../../application/composition/taskApplicationServices";
import { resolveActivityRecordingDate } from "../model/activityRecordingTime";
import { useI18n } from "@/i18n";
import type { ActivityType } from "../../domain/model/task.types";
import {
  CallActivityCreateModal,
  EmailActivityCreateModal,
  MeetingActivityCreateModal,
  NoteActivityCreateModal,
  SmsActivityCreateModal,
  type CallActivityDraft,
  type EmailActivityDraft,
  type MeetingActivityDraft,
  type NoteActivityDraft,
  type SmsActivityDraft,
} from "./ActivityCreateModals";

export type RelationshipActivityAction = "call" | "meeting" | "email" | "sms" | "note";

export interface RelationshipActivityDraft {
  type: ActivityType;
  subject: string;
  body: string;
  occurredAt?: string;
}

export interface RelationshipActivityCreateModalProps {
  action: RelationshipActivityAction | null;
  targetId?: string;
  formId?: string;
  email?: string;
  phone?: string;
  recordLabel?: string;
  ownerName?: string;
  onClose(): void;
  onSave(draft: RelationshipActivityDraft): Promise<boolean>;
}

function nowIso(): string {
  return new Date().toISOString();
}

export function RelationshipActivityCreateModal({ action, targetId, formId, email, phone, recordLabel, ownerName, onClose, onSave }: RelationshipActivityCreateModalProps) {
  const { locale } = useI18n();
  const vi = locale === "vi";
  if (!action) return null;
  const label = recordLabel || (vi ? "quan hệ" : "relationship");

  if (action === "call") {
    return (
      <CallActivityCreateModal
        isOpen
        recordingOnly
        targetId={targetId}
        formId={formId}
        onClose={onClose}
        defaults={{ subject: vi ? `Cuộc gọi với ${label}` : `Call with ${label}`, recipient: phone ?? "" }}
        onSubmit={async () => {}}
        onSave={(draft: CallActivityDraft) => onSave({
          type: "CALL",
          subject: draft.subject,
          occurredAt: resolveActivityRecordingDate(draft),
          body: [
            draft.direction === "outbound" ? "Outbound" : "Inbound",
            draft.result,
            draft.recipient ? `Phone: ${draft.recipient}` : "",
            draft.durationMinutes ? `Duration: ${draft.durationMinutes}m` : "",
            draft.body,
          ].filter(Boolean).join(" · "),
        })}
      />
    );
  }

  if (action === "meeting") {
    return (
      <MeetingActivityCreateModal
        recordingTaskBacked={false}
        isOpen
        recordingOnly
        targetId={targetId}
        formId={formId}
        onClose={onClose}
        defaults={{ title: vi ? `Lịch hẹn với ${label}` : `Meeting with ${label}`, owner: ownerName }}
        onSubmit={async () => {}}
        onSave={(draft: MeetingActivityDraft) => onSave({
          type: "MEETING",
          subject: draft.title,
          occurredAt: draft.recordingTime === "SERVER_NOW" ? undefined : new Date(draft.startAt).toISOString(),
          body: [`Start: ${draft.startAt}`, draft.endAt ? `End: ${draft.endAt}` : "", draft.channel, draft.location, draft.attendees, draft.owner, draft.agenda].filter(Boolean).join(" · "),
        })}
      />
    );
  }

  if (action === "email") {
    return (
      <EmailActivityCreateModal
        isOpen
        recordingOnly
        targetId={targetId}
        formId={formId}
        onClose={onClose}
        defaults={{ to: email ?? "", subject: vi ? `Email với ${label}` : `Email with ${label}` }}
        onSubmit={async () => {}}
        onSave={(draft: EmailActivityDraft) => onSave({
          type: "EMAIL",
          subject: draft.subject,
          occurredAt: isTaskConnectedApiRuntime() ? undefined : nowIso(),
          body: [`To: ${draft.to}`, draft.body].filter(Boolean).join(" · "),
        })}
      />
    );
  }

  if (action === "sms") {
    return (
      <SmsActivityCreateModal
        isOpen
        recordingOnly
        targetId={targetId}
        formId={formId}
        onClose={onClose}
        defaults={{ phone: phone ?? "" }}
        onSubmit={async () => {}}
        onSave={(draft: SmsActivityDraft) => onSave({
          type: "MESSAGE",
          subject: vi ? `SMS với ${label}` : `SMS with ${label}`,
          occurredAt: isTaskConnectedApiRuntime() ? undefined : nowIso(),
          body: [`To: ${draft.phone}`, draft.body].filter(Boolean).join(" · "),
        })}
      />
    );
  }

  return (
    <NoteActivityCreateModal
      isOpen
      recordingOnly
      targetId={targetId}
      formId={formId}
      onClose={onClose}
      defaults={{ title: vi ? `Ghi chú ${label}` : `${label} note` }}
      onSubmit={async () => {}}
      onSave={(draft: NoteActivityDraft) => onSave({
        type: "NOTE",
        subject: draft.title,
        occurredAt: resolveActivityRecordingDate(draft),
        body: `[${draft.category}] ${draft.body}`,
      })}
    />
  );
}
