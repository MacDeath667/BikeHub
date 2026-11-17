import { afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost:8080/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, Event: dom.window.Event, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
const { render, screen, fireEvent, waitFor, cleanup } = await import("@testing-library/react");
const { Messages } = await import("../src/pages/Messages");
const { Conversation } = await import("../src/pages/Conversation");
const { MessageReports } = await import("../src/pages/MessageReports");
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const user = { id: "owner", name: "Роман", handle: "rider", city: "Казань", initials: "Р", color: "green", bio: "", followers: 0, isFollowing: false, bikesCount: 1, postsCount: 2 };
const peer = { id: "peer", name: "Собеседник", handle: "companion", avatarUrl: null, available: true };
const conversation = { id: "chat", peer, status: 2, requestedByMe: true, canSend: true, canAccept: false, blockedByMe: false, unreadCount: 0, latestSequence: 1, updatedAt: "2026-11-17T10:00:00Z", lastText: "Привет" };
const message = (id: string, sequence: number) => ({ id, senderId: peer.id, sequence, text: "Сообщение " + sequence, isDeleted: false, isHidden: false, createdAt: "2026-11-17T10:00:00Z" });
const page = (items: unknown[], hasMoreBefore = false, latestSequence = 1) => ({ items, hasMoreBefore, hasMoreAfter: false, latestSequence });
beforeEach(() => { window.location.hash = "/messages"; window.confirm = () => true; globalThis.fetch = async () => { throw Error("Unexpected request"); }; });
afterEach(cleanup);

test("private screens do not fetch without sign-in and pending requests do not show a sending form", async () => {
  let requests = 0, accepted = false;
  globalThis.fetch = async (input, options) => {
    requests++; const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user, enabled: true, csrfToken: "test" });
    if (path.endsWith("/request")) { assert.equal(options?.method, "PUT"); accepted = true; return json(conversation); }
    if (path.endsWith("/messages")) return json(page([message("1", 1)]));
    return json(accepted ? conversation : { ...conversation, status: 1, requestedByMe: false, canSend: false, canAccept: true });
  };
  const view = render(<Messages me={null} />); assert.equal(requests, 0); assert.ok(screen.getByText("Войдите, чтобы общаться с другими участниками."));
  view.rerender(<Conversation id="chat" me={user} />);
  await waitFor(() => assert.ok(screen.getByText("Принять запрос")));
  assert.ok(!screen.queryByLabelText("Ваше сообщение"));
  fireEvent.click(screen.getByText("Принять запрос"));
  await waitFor(() => assert.ok(screen.getByLabelText("Ваше сообщение"))); assert.ok(screen.getByText("Сообщение 1"));
});

test("a failed send retains the text and idempotency key and a retry shows one outgoing message", async () => {
  let fail = true; const submissions: { text: string; clientMessageId: string }[] = [];
  globalThis.fetch = async (input, options) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user, enabled: true, csrfToken: "test" });
    if (path.endsWith("/messages")) {
      if (options?.method === "POST") {
        const body = JSON.parse(String(options.body)); submissions.push(body);
        return fail ? json({ title: "Отправка временно недоступна" }, 503) : json({ ...message("sent", 2), senderId: user.id, text: body.text });
      }
      return json(page([message("1", 1)]));
    }
    return json(conversation);
  };
  render(<Conversation id="chat" me={user} />);
  await waitFor(() => assert.ok(screen.getByLabelText("Ваше сообщение")));
  const textarea = screen.getByLabelText("Ваше сообщение") as HTMLTextAreaElement;
  fireEvent.change(textarea, { target: { value: "Подъеду к парку" } }); fireEvent.click(screen.getByText("Отправить сообщение"));
  await waitFor(() => assert.ok(screen.getByText("Отправка временно недоступна"))); assert.equal(textarea.value, "Подъеду к парку");
  fail = false; fireEvent.click(screen.getByText("Отправить сообщение"));
  await waitFor(() => assert.equal(textarea.value, "")); assert.equal(submissions.length, 2); assert.equal(submissions[0].clientMessageId, submissions[1].clientMessageId);
  assert.equal(screen.getAllByText("Подъеду к парку").length, 1); assert.ok(!screen.queryByText("Отправка временно недоступна"));
});

test("older-history failures keep current messages and retry the same cursor", async () => {
  let fail = true; const cursors: string[] = [];
  globalThis.fetch = async input => {
    const url = new URL(String(input));
    if (url.pathname.endsWith("/messages")) {
      const before = url.searchParams.get("beforeSequence");
      if (before) { cursors.push(before); return fail ? json({ title: "История временно недоступна" }, 503) : json(page([message("7", 7), message("8", 8)], false, 10)); }
      return json(page([message("9", 9), message("10", 10)], true, 10));
    }
    return json({ ...conversation, latestSequence: 10 });
  };
  render(<Conversation id="chat" me={user} />);
  await waitFor(() => assert.ok(screen.getByText("Сообщение 10")));
  fireEvent.click(screen.getByText("Более ранние сообщения")); await waitFor(() => assert.ok(screen.getByText("История временно недоступна")));
  assert.ok(screen.getByText("Сообщение 9")); fail = false; fireEvent.click(screen.getByText("Более ранние сообщения"));
  await waitFor(() => assert.ok(screen.getByText("Сообщение 7"))); assert.deepEqual(cursors, ["9", "9"]);
  assert.equal(screen.getAllByText("Сообщение 9").length, 1);
});

test("message moderation requires a moderator and shows only the reported fragment", async () => {
  let requests = 0;
  globalThis.fetch = async input => {
    requests++; assert.equal(new URL(String(input)).pathname, "/api/moderation/messages/reports");
    return json({ items: [{ id: "report", messageId: "message", reporter: peer, reportedUser: peer, messageText: "Reported fragment", reason: "Reason for the report", createdAt: "2026-11-17T10:00:00Z", canChangeVisibility: false }], total: 1, hasMore: false });
  };
  const view = render(<MessageReports me={user} />); assert.equal(requests, 0);
  view.rerender(<MessageReports me={{ ...user, isModerator: true }} />);
  await waitFor(() => assert.ok(screen.getByText("Reported fragment"))); assert.equal(requests, 1);
  assert.ok(!screen.queryByText("Открыть переписку")); assert.ok(!screen.queryByText("Скрыть сообщение"));
});

test("refreshing after peer erasure drops cached peer text and keeps the reader's own messages", async () => {
  const own = { ...message("own", 2), senderId: user.id, text: "Мой сохранённый текст" };
  let erased = false;
  const originalHidden = Object.getOwnPropertyDescriptor(document, "hidden");
  Object.defineProperty(document, "hidden", { configurable: true, value: false });
  globalThis.fetch = async (input, options) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user, enabled: true, csrfToken: "test" });
    if (path.endsWith("/read")) { assert.equal(options?.method, "PUT"); return new Response(null, { status: 204 }); }
    if (path.endsWith("/messages")) return json(page(erased ? [own] : [message("peer-private", 1), own], false, 2));
    return json(erased ? { ...conversation, peer: { id: null, name: "Удалённый аккаунт", handle: null, avatarUrl: null, available: false }, canSend: false, latestSequence: 2 } : { ...conversation, latestSequence: 2 });
  };
  try {
    render(<Conversation id="chat" me={user} />);
    await waitFor(() => assert.ok(screen.getByText("Сообщение 1")));
    erased = true; fireEvent(window, new Event("focus"));
    await waitFor(() => assert.ok(!screen.queryByText("Сообщение 1")));
    assert.ok(screen.getByText("Мой сохранённый текст")); assert.ok(screen.getByText("Удалённый аккаунт")); assert.ok(!screen.queryByLabelText("Ваше сообщение"));
  } finally {
    if (originalHidden) Object.defineProperty(document, "hidden", originalHidden); else Reflect.deleteProperty(document, "hidden");
  }
});
