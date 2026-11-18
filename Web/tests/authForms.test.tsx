import { afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";
import type { AuthSession, User } from "../src/api";

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost:8080/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, Event: dom.window.Event, FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
dom.window.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
dom.window.HTMLDialogElement.prototype.close = function () { this.open = false; };
const { render, screen, fireEvent, waitFor, cleanup, act } = await import("@testing-library/react");
const { AuthModal } = await import("../src/components/AuthModal");
const { UserProfile } = await import("../src/pages/UserProfile");
const { CommunityDetail } = await import("../src/pages/CommunityDetail");
const { Settings } = await import("../src/pages/Settings");
const user: User = { id: "owner", name: "Владелец", handle: "owner", city: "", initials: "В", color: "green", bio: "Публичное описание", followers: 0, isFollowing: false, bikesCount: 0, postsCount: 0 };
const session: AuthSession = { user: null, enabled: true, csrfToken: "test-csrf", emailConfirmed: false, maskedEmail: null, phoneConfirmed: false, developmentMailbox: false };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const challenge = () => ({ challengeId: "challenge", expiresAt: new Date(Date.now() + 600000).toISOString(), resendAt: new Date(Date.now() - 1000).toISOString() });
beforeEach(() => {
  window.localStorage.clear(); window.confirm = () => true;
  globalThis.fetch = async input => {
    if (new URL(String(input)).pathname === "/api/auth/session") return json(session);
    throw Error("Unexpected request");
  };
});
afterEach(cleanup);
const email = () => fireEvent.change(screen.getByLabelText("Ваша почта"), { target: { value: "rider@example.test" } });

test("a definitive delivery failure can request a fresh challenge instead of replaying the saved failure", async () => {
  const mutations: { path: string; body: unknown }[] = []; const polls: string[] = [];
  globalThis.fetch = async (input, init) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json(session);
    if (path.endsWith("/delivery")) { polls.push(path); return json({ status: path.includes("/fresh/") ? "sent" : "failed" }); }
    mutations.push({ path, body: JSON.parse(init!.body as string) }); return json({ ...challenge(), challengeId: mutations.length === 1 ? "challenge" : "fresh" });
  };
  render(<AuthModal session={session} close={() => {}} onSuccess={() => {}} />);
  email(); fireEvent.click(screen.getByText("Получить код"));
  await waitFor(() => assert.ok(screen.getByText(/не смог отправить письмо/)));
  assert.equal(polls.length, 1); assert.deepEqual(mutations[0], { path: "/api/auth/login/email/challenges", body: { email: "rider@example.test" } });
  assert.ok(!screen.queryByLabelText("Ник")); assert.ok(!screen.queryByText("Как к вам обращаться"));
  fireEvent.change(screen.getByLabelText("Код из письма"), { target: { value: "123456" } });
  fireEvent.click(screen.getByText("Запросить новый код"));
  await screen.findByText(/Почтовый сервис отправил письмо/);
  assert.deepEqual(mutations[1], { path: "/api/auth/login/email/challenges", body: { email: "rider@example.test" } });
  assert.ok(polls.includes("/api/auth/email/challenges/fresh/delivery"));
  assert.equal((screen.getByLabelText("Код из письма") as HTMLInputElement).value, "");
});

for (const status of ["sent", "pending", "unknown", "unavailable"]) {
  test(`delivery status ${status} keeps the same code and message identity on HTTP retry`, async () => {
    const mutations: { path: string; body: unknown }[] = [];
    globalThis.fetch = async (input, init) => {
      const path = new URL(String(input)).pathname;
      if (path === "/api/auth/session") return json(session);
      if (path.endsWith("/delivery")) return json({ status });
      mutations.push({ path, body: JSON.parse(init!.body as string) }); return json(challenge());
    };
    render(<AuthModal session={session} close={() => {}} onSuccess={() => {}} />);
    email(); fireEvent.click(screen.getByText("Получить код"));
    await screen.findByLabelText("Код из письма");
    fireEvent.change(screen.getByLabelText("Код из письма"), { target: { value: "123456" } });
    fireEvent.click(screen.getByText("Повторить запрос"));
    await waitFor(() => assert.equal(mutations.length, 2));
    assert.deepEqual(mutations[1], { path: "/api/auth/email/challenges/challenge/resend", body: {} });
    assert.equal((screen.getByLabelText("Код из письма") as HTMLInputElement).value, "123456");
  });
}

test("a failed delivery still observes the resend cooldown before requesting a new code", async () => {
  let creates = 0;
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json(session);
    if (path.endsWith("/delivery")) return json({ status: "failed" });
    creates++; return json({ ...challenge(), resendAt: new Date(Date.now() + 60000).toISOString() });
  };
  render(<AuthModal session={session} close={() => {}} onSuccess={() => {}} />);
  email(); fireEvent.click(screen.getByText("Получить код"));
  const retry = await screen.findByRole("button", { name: /Новый код через/ });
  assert.equal((retry as HTMLButtonElement).disabled, true); fireEvent.click(retry); assert.equal(creates, 1);
});

test("registration recovery retains the chosen nickname and display name for the fresh challenge", async () => {
  const mutations: { path: string; body: unknown }[] = [];
  globalThis.fetch = async (input, init) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json(session);
    if (path.endsWith("/delivery")) return json({ status: path.includes("/fresh/") ? "sent" : "failed" });
    mutations.push({ path, body: JSON.parse(init!.body as string) });
    return json({ ...challenge(), challengeId: mutations.length === 1 ? "challenge" : "fresh" });
  };
  render(<AuthModal session={session} close={() => {}} onSuccess={() => {}} />);
  fireEvent.click(screen.getByText("Нет аккаунта? Зарегистрироваться")); email();
  fireEvent.change(screen.getByLabelText("Ник"), { target: { value: "Gravel_Rider" } });
  fireEvent.change(screen.getByLabelText(/Как к вам обращаться/), { target: { value: "Роман" } });
  fireEvent.click(screen.getByText("Получить код"));
  fireEvent.click(await screen.findByText("Запросить новый код"));
  await screen.findByText(/Почтовый сервис отправил письмо/);
  const expected = { path: "/api/auth/registration/email/challenges", body: { email: "rider@example.test", name: "Роман", nickname: "gravel_rider" } };
  assert.deepEqual(mutations, [expected, expected]);
});

test("a failed fresh request returns to the retained email without trapping the user in the old failed challenge", async () => {
  let creates = 0;
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json(session);
    if (path.endsWith("/delivery")) return json({ status: "failed" });
    creates++; return creates === 1 ? json(challenge()) : json({ title: "Сервис временно недоступен" }, 503);
  };
  render(<AuthModal session={session} close={() => {}} onSuccess={() => {}} />);
  email(); fireEvent.click(screen.getByText("Получить код"));
  fireEvent.click(await screen.findByText("Запросить новый код"));
  await screen.findByRole("alert");
  assert.equal((screen.getByLabelText("Ваша почта") as HTMLInputElement).value, "rider@example.test");
  assert.equal(screen.queryByLabelText("Код из письма"), null);
  assert.equal(creates, 2);
});

test("registration and login are separate flows and a send failure leaves the email form open", async () => {
  let payload: unknown, endpoint = "";
  globalThis.fetch = async (input, init) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json(session);
    endpoint = path; payload = JSON.parse(init!.body as string); return json({ title: "Не удалось отправить письмо" }, 503);
  };
  render(<AuthModal session={session} close={() => {}} onSuccess={() => {}} />);
  fireEvent.click(screen.getByText("Нет аккаунта? Зарегистрироваться")); email();
  fireEvent.change(screen.getByLabelText("Ник"), { target: { value: "Gravel_Rider" } });
  fireEvent.change(screen.getByLabelText(/Как к вам обращаться/), { target: { value: "Роман" } });
  fireEvent.click(screen.getByText("Получить код"));
  await waitFor(() => assert.ok(screen.getByRole("alert")));
  assert.equal(endpoint, "/api/auth/registration/email/challenges");
  assert.deepEqual(payload, { email: "rider@example.test", name: "Роман", nickname: "gravel_rider" });
  assert.ok(!screen.queryByLabelText("Код из письма"));
  fireEvent.click(screen.getByText("Уже есть аккаунт? Войти"));
  assert.ok(screen.getByRole("heading", { name: "Вход в BikeHub" })); assert.ok(!screen.queryByLabelText("Ник"));
});

test("clicking dialog padding keeps the form and closing is blocked during an authentication request", async () => {
  let closed = 0, release: (response: Response) => void = () => {};
  globalThis.fetch = async input => new URL(String(input)).pathname === "/api/auth/session" ? json(session) : new Promise(resolve => { release = resolve; });
  render(<AuthModal session={session} close={() => { closed++; }} onSuccess={() => {}} />);
  const dialog = screen.getByRole("dialog");
  dialog.getBoundingClientRect = () => ({ left: 10, top: 10, right: 510, bottom: 610 }) as DOMRect;
  fireEvent.click(dialog, { clientX: 40, clientY: 40 }); assert.equal(closed, 0);
  email(); fireEvent.click(screen.getByText("Получить код"));
  await waitFor(() => assert.equal((screen.getByLabelText("Закрыть окно входа") as HTMLButtonElement).disabled, true));
  fireEvent.click(dialog, { clientX: 0, clientY: 0 }); fireEvent(dialog, new Event("cancel", { cancelable: true })); assert.equal(closed, 0);
  await act(async () => { release(json({ title: "Сервис временно недоступен" }, 503)); });
  await waitFor(() => assert.ok(screen.getByRole("alert")));
  fireEvent.click(dialog, { clientX: 0, clientY: 0 }); assert.equal(closed, 1);
});

test("an expired code cannot be submitted and a fresh request returns to the email form", async () => {
  let verifies = 0;
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json(session);
    if (path.endsWith("/verify")) verifies++;
    return json({ ...challenge(), expiresAt: new Date(Date.now() - 1000).toISOString() });
  };
  render(<AuthModal session={session} close={() => {}} onSuccess={() => {}} />);
  email(); fireEvent.click(screen.getByText("Получить код"));
  await waitFor(() => assert.ok(screen.getByText("Запросить новый код")));
  fireEvent.change(screen.getByLabelText("Код из письма"), { target: { value: "123456" } });
  assert.equal((screen.getByRole("button", { name: "Войти", exact: true }) as HTMLButtonElement).disabled, true);
  fireEvent.submit(screen.getByLabelText("Код из письма").closest("form")!); assert.equal(verifies, 0);
  fireEvent.click(screen.getByText("Запросить новый код")); assert.ok(screen.getByLabelText("Ваша почта"));
});

test("switching accounts closes a private profile editor and guests request login before following", async () => {
  let mutations = 0, logins = 0;
  globalThis.fetch = async (input, init) => {
    if (init?.method) mutations++;
    return json(new URL(String(input)).pathname.endsWith("/bikes") ? { items: [], total: 0 } : user);
  };
  const props = { id: user.id, notify() {}, reload() {}, version: 0 };
  const view = render(<UserProfile {...props} me={user} />);
  await waitFor(() => assert.ok(screen.getByText("Редактировать профиль")));
  fireEvent.click(screen.getByText("Редактировать профиль")); fireEvent.change(screen.getByLabelText("О себе"), { target: { value: "Неопубликованная личная правка" } });
  view.rerender(<UserProfile {...props} me={null} />); assert.ok(!screen.queryByRole("dialog")); assert.ok(!screen.queryByDisplayValue("Неопубликованная личная правка"));
  const login = () => { logins++; }; window.addEventListener("bikehub:auth-required", login);
  try { fireEvent.click(screen.getByText("Подписаться")); assert.equal(logins, 1); assert.equal(mutations, 0); }
  finally { window.removeEventListener("bikehub:auth-required", login); }
});

test("a community settings editor is removed when the viewer loses ownership", async () => {
  const group = { id: "group", ownerId: user.id, name: "Сообщество", description: "Поездки вместе", category: "gravel", members: 1, imageUrl: "", isJoined: true };
  globalThis.fetch = async input => json(new URL(String(input)).pathname.endsWith("/entries") ? { items: [], total: 0 } : group);
  const view = render(<CommunityDetail id="group" me={user} reload={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText("Настройки"))); fireEvent.click(screen.getByText("Настройки"));
  fireEvent.change(screen.getByLabelText("Название"), { target: { value: "Личный неопубликованный текст" } });
  view.rerender(<CommunityDetail id="group" me={{ ...user, id: "other" }} reload={() => {}} />);
  assert.ok(!screen.queryByDisplayValue("Личный неопубликованный текст")); assert.ok(!screen.queryByLabelText("Название"));
});

test("an account switch clears a pending deletion code instead of reusing the previous owner's form", async () => {
  globalThis.fetch = async input => json(new URL(String(input)).pathname.endsWith("/delivery") ? { status: "sent" } : challenge());
  const view = render(<Settings me={user} maskedEmail="ow***@example.test" account />);
  fireEvent.click(screen.getByText("Удалить аккаунт")); fireEvent.click(screen.getByText("Получить код удаления"));
  await waitFor(() => assert.ok(screen.getByLabelText("Код удаления")));
  fireEvent.change(screen.getByLabelText("Код удаления"), { target: { value: "123456" } });
  view.rerender(<Settings me={{ ...user, id: "other", handle: "other" }} maskedEmail="ot***@example.test" account />);
  assert.ok(!screen.queryByLabelText("Код удаления")); assert.ok(!screen.queryByDisplayValue("123456"));
  fireEvent.click(screen.getByText("Удалить аккаунт")); assert.ok(screen.getByText("Получить код удаления"));
});
