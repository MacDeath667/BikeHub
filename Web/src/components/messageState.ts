import type { DirectMessage } from "../api";

export const messagesChanged = "bikehub:messages-changed";
export function notifyMessagesChanged() {
  window.dispatchEvent(new Event(messagesChanged)); window.dispatchEvent(new Event("bikehub:notifications-changed"));
}
export type PendingMessage = { text: string; clientMessageId: string };
function newId() {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
  const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
export function prepareMessage(previous: PendingMessage | null, text: string): PendingMessage {
  const normalized = text.trim();
  return previous?.text === normalized ? previous : { text: normalized, clientMessageId: newId() };
}
export function mergeMessages(previous: DirectMessage[], ...pages: DirectMessage[][]) {
  const messages = new Map(previous.map(message => [message.id, message]));
  for (const page of pages) for (const message of page) {
    // Deletion is irreversible: an older in-flight read must not restore cleared text.
    messages.set(message.id, messages.get(message.id)?.isDeleted ? { ...message, isDeleted: true, text: null } : message);
  }
  return [...messages.values()].sort((a, b) => a.sequence - b.sequence);
}
