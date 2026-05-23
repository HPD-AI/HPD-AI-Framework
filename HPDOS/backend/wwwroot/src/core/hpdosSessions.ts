import type { Session } from "@hpd/hpd-agent-client";

export function selectPreferredSession(sessions: Session[] | undefined, preferredSessionId: string) {
  if (!sessions?.length) return "";
  return sessions.find((session) => session.id === preferredSessionId)?.id || sessions[0].id;
}

export function sessionTitle(session: Session) {
  const metadata = session.metadata || {};
  return stringFromMetadata(metadata, "hpdos.title")
    || stringFromMetadata(metadata, "title")
    || `Chat ${session.id.slice(0, 6).toUpperCase()}`;
}

export function titleFromPrompt(value: string) {
  const title = value.replace(/\s+/g, " ").trim();
  return title.length <= 64 ? title : `${title.slice(0, 61)}...`;
}

function stringFromMetadata(metadata: Record<string, unknown>, key: string) {
  const value = metadata[key];
  return typeof value === "string" && value.trim() ? value.trim() : "";
}
