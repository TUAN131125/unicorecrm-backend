import { resolveEffectiveAccess } from "@/platform/access-control";
import { runBackendProjection } from "@/shared/application";
import type { CRMActivity } from "@/shared/domain";
import { logActivityViaApi } from "@/modules/tasks";
import { leadRepository } from "../composition/leadApplicationServices";
import { saveLead } from "./leadRepositoryCommands";

export async function logLeadActivityViaApi(leadId: string, input: CRMActivity): Promise<CRMActivity> {
  const access = resolveEffectiveAccess();
  if (!access.memberId) throw new Error("LEAD_ACTIVITY_ACTOR_REQUIRED");
  const outcome = await logActivityViaApi({
    id: input.id,
    type: normalizeActivityType(input.type),
    subject: input.title,
    ...(input.description === undefined ? {} : { body: input.description }),
    actorId: access.memberId,
    ...(input.author === undefined ? {} : { actorName: input.author }),
    recordRef: { moduleKey: "leads", recordId: leadId },
  });
  const activity = outcome.data.activity;
  const projected: CRMActivity = {
    id: activity.id,
    title: activity.subject,
    ...(activity.body === undefined ? {} : { description: activity.body }),
    createdAt: activity.occurredAt,
    author: input.author,
    type: activity.type.toLocaleLowerCase(),
  };
  const current = leadRepository.getById(leadId);
  if (current) {
    runBackendProjection("leads", () => saveLead(leadRepository, {
      ...current,
      activities: [projected, ...(current.activities ?? []).filter((item) => item.id !== projected.id)],
      activitiesAuthority: "NOT_INCLUDED",
    }));
  }
  return projected;
}

function normalizeActivityType(value: string): "CALL" | "EMAIL" | "MEETING" | "NOTE" | "MESSAGE" | "SYSTEM" {
  const normalized = value.trim().toLocaleUpperCase();
  if (normalized === "CALL" || normalized === "EMAIL" || normalized === "MEETING" || normalized === "NOTE" || normalized === "MESSAGE" || normalized === "SYSTEM") return normalized;
  return "SYSTEM";
}
