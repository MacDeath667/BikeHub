import { afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost:8080/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, Event: dom.window.Event, FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
window.scrollTo = () => {};
dom.window.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
dom.window.HTMLDialogElement.prototype.close = function () { this.open = false; };
Object.defineProperty(globalThis, "localStorage", { configurable: true, value: window.localStorage });
window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} }) as unknown as MediaQueryList;
const { render, screen, fireEvent, waitFor, cleanup, act } = await import("@testing-library/react");
const { default: App } = await import("../src/App");
const { Feed } = await import("../src/pages/Feed");
const { BikeDirectory, BikeDetail } = await import("../src/pages/Bikes");
const { PostDetail } = await import("../src/pages/PostDetail");
const { UserProfile } = await import("../src/pages/UserProfile");
const { GroupEntryDetail } = await import("../src/pages/CommunityDetail");
const { Moderation } = await import("../src/pages/Moderation");
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const user = { id: "owner", name: "Велосипедист", handle: "rider", city: "Казань", initials: "В", color: "green", bio: "", followers: 0, isFollowing: false, bikesCount: 1, postsCount: 2 };
const bike = { id: "bike", name: "Гравийный велосипед", ownerId: user.id, brand: "Test", description: "Описание велосипеда", category: "gravel", year: 2025, mileage: 100, imageUrl: "/test-bike.webp", color: "", owner: user };
const post = (id: string) => ({ id, title: "История " + id, content: "Поездка", author: user, bike, category: "ride", imageUrl: "/test-post.webp", tags: [], createdAt: "2026-11-16T12:00:00Z", likes: 0, commentsCount: 3, isLiked: false, isSaved: false });
const comment = (id: string) => ({ id, author: user, text: "Комментарий " + id, createdAt: "2026-11-16T12:00:00Z" });
const page = (items: unknown[], pageNumber = 1, hasMore = false) => ({ items, total: 3, pageNumber, pageSize: 2, hasMore });
beforeEach(() => { window.location.hash = "/"; window.confirm = () => true; globalThis.fetch = async () => { throw Error("Unexpected request"); }; });
afterEach(cleanup);

test("the application recognises search while preserving the unknown-page fallback", async () => {
  window.location.hash = "/search?q=Гравий&kind=people";
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user: null, enabled: true, csrfToken: "test" });
    if (path === "/api/feed/overview") return json({ riders: 1, bikes: 1, posts: 0, tags: [], featuredBike: null });
    if (path === "/api/communities/catalog") return json(page([]));
    if (path === "/api/search/people") return json(page([user]));
    throw Error("Unexpected request: " + path);
  };
  render(<App />);
  await waitFor(() => assert.ok(screen.getByText(user.name)));
  assert.ok(!screen.queryByText("Такой страницы нет"));
  await act(async () => { window.location.hash = "/missing-page"; window.dispatchEvent(new Event("hashchange")); });
  await waitFor(() => assert.ok(screen.getByText("Такой страницы нет")));
});

test("a failed feed page keeps readable stories and retries that page without duplicates", async () => {
  let fail = true; const requested: number[] = [];
  globalThis.fetch = async input => {
    const current = Number(new URL(String(input)).searchParams.get("page")); requested.push(current);
    return current === 1 ? json(page([post("1"), post("2")], 1, true)) : fail ? json({ title: "Лента временно недоступна" }, 503) : json(page([post("2"), post("3")], 2));
  };
  render(<Feed saved={false} search="" version={0} me={null} notify={() => {}} onCreate={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText("История 1")));
  fireEvent.click(screen.getByText("Больше историй"));
  await waitFor(() => assert.ok(screen.getByText("Лента временно недоступна")));
  assert.ok(screen.getByText("История 1"));
  fail = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("История 3")));
  assert.equal(screen.getAllByText("История 2").length, 1); assert.deepEqual(requested, [1, 2, 2]);
});

test("a slow or failed recommendation request does not block the page or restart it on retry", async () => {
  window.location.hash = "/search?q=Гравий&kind=people";
  let release: (response: Response) => void = () => {}, fail = true, sessions = 0, searches = 0;
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") { sessions++; return json({ user: null, enabled: true, csrfToken: "test" }); }
    if (path === "/api/search/people") { searches++; return json(page([user])); }
    if (path === "/api/communities/catalog") return json(page([]));
    if (path === "/api/feed/overview") return fail ? new Promise(resolve => { release = resolve; }) : json({ tags: [], featuredBike: null });
    throw Error("Unexpected request: " + path);
  };
  render(<App />);
  await waitFor(() => assert.ok(screen.getByText(user.name)));
  await act(async () => { fail = false; release(json({ title: "Статистика недоступна" }, 503)); });
  await waitFor(() => assert.ok(screen.getByText("Не удалось обновить рекомендации сообщества.")));
  fireEvent.change(screen.getByLabelText("Поисковый запрос"), { target: { value: "Несохранённый запрос" } });
  fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(!screen.queryByText("Не удалось обновить рекомендации сообщества.")));
  assert.equal((screen.getByLabelText("Поисковый запрос") as HTMLInputElement).value, "Несохранённый запрос");
  assert.equal(sessions, 1); assert.equal(searches, 1);
});

test("bike directory failures offer retry, retain the loaded page and merge overlapping bikes", async () => {
  let fail = true;
  globalThis.fetch = async input => {
    const current = Number(new URL(String(input)).searchParams.get("page"));
    return current === 1 ? json(page([bike], 1, true)) : fail ? json({ title: "Каталог временно недоступен" }, 503) : json(page([bike, { ...bike, id: "other", name: "Другой велосипед" }], 2));
  };
  render(<BikeDirectory garage={false} me={null} version={0} onCreate={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText(bike.name)));
  fireEvent.click(screen.getByText("Показать ещё"));
  await waitFor(() => assert.ok(screen.getByText("Каталог временно недоступен")));
  assert.ok(screen.getByText(bike.name)); fail = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("Другой велосипед")));
  assert.equal(screen.getAllByText(bike.name).length, 1);
});

test("bike history ignores rapid extra clicks and retries the failed page without hiding the bike", async () => {
  let release: (response: Response) => void = () => {}; const requested: number[] = []; let failed = false;
  globalThis.fetch = async input => {
    const url = new URL(String(input));
    if (!url.pathname.endsWith("/history")) return json(bike);
    const current = Number(url.searchParams.get("page")); requested.push(current);
    if (current === 1) return json(page([post("1")], 1, true));
    if (!failed) return new Promise(resolve => { release = resolve; });
    return json(page([post("1"), post("2")], 2));
  };
  render(<BikeDetail id="bike" me={null} version={0} onCreate={() => {}} notify={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText("История 1")));
  const more = screen.getByText("Ещё записи"); fireEvent.click(more);
  await waitFor(() => assert.equal(requested.length, 2)); fireEvent.click(more);
  assert.deepEqual(requested, [1, 2]);
  await act(async () => { failed = true; release(json({ title: "Бортжурнал недоступен" }, 503)); });
  await waitFor(() => assert.ok(screen.getByText("Бортжурнал недоступен")));
  assert.ok(screen.getByRole("heading", { name: bike.name }));
  fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("История 2")));
  assert.equal(screen.getAllByText("История 1").length, 1);
});

test("comment pagination cannot skip pages and its failure does not hide the article", async () => {
  let release: (response: Response) => void = () => {}, failed = false, detailReads = 0; const requested: number[] = [];
  globalThis.fetch = async input => {
    const url = new URL(String(input));
    if (!url.pathname.endsWith("/comments")) { detailReads++; return json(post("1")); }
    const current = Number(url.searchParams.get("page")); requested.push(current);
    if (current === 1) return json(page([comment("1"), comment("2")], 1, true));
    if (!failed) return new Promise(resolve => { release = resolve; });
    return json(page([comment("2"), comment("3")], 2));
  };
  render(<PostDetail id="1" me={null} notify={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText("Комментарий 1")));
  const more = screen.getByText("Ещё комментарии"); fireEvent.click(more);
  await waitFor(() => assert.equal(requested.length, 2)); fireEvent.click(more);
  assert.deepEqual(requested, [1, 2]);
  await act(async () => { failed = true; release(json({ title: "Комментарии недоступны" }, 503)); });
  await waitFor(() => assert.ok(screen.getByText("Комментарии недоступны")));
  assert.ok(screen.getByRole("heading", { name: "История 1" })); assert.ok(screen.getByText("Комментарий 1"));
  fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("Комментарий 3")));
  assert.equal(screen.getAllByText("Комментарий 2").length, 1); assert.equal(detailReads, 1);
});

test("mobile navigation traps focus, closes on Escape or navigation and restores scrolling", async () => {
  const originalMedia = window.matchMedia; let mobile = true; const listeners = new Set<() => void>();
  const media = { get matches() { return mobile; }, addEventListener(_type: string, listener: () => void) { listeners.add(listener); }, removeEventListener(_type: string, listener: () => void) { listeners.delete(listener); } };
  window.matchMedia = query => query === "(max-width: 700px)" ? media as unknown as MediaQueryList : originalMedia(query);
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user: null, enabled: true, csrfToken: "test" });
    if (path === "/api/feed/overview") return json({ tags: [], featuredBike: null });
    return json(page([]));
  };
  document.body.style.overflow = "scroll";
  try {
    render(<App />);
    await waitFor(() => assert.ok(screen.getByRole("heading", { name: /Два колеса/ })));
    const trigger = screen.getByLabelText("Открыть меню"), sidebar = document.getElementById("main-sidebar")!;
    assert.ok(sidebar.hasAttribute("inert")); assert.equal(trigger.getAttribute("aria-controls"), sidebar.id);
    trigger.focus(); fireEvent.click(trigger);
    await waitFor(() => assert.ok(screen.getByRole("dialog", { name: "Меню BikeHub" })));
    assert.equal(document.body.style.overflow, "hidden"); assert.ok(document.querySelector(".workspace")!.hasAttribute("inert"));
    const close = screen.getByLabelText("Закрыть меню"), last = Array.from(sidebar.querySelectorAll("a")).at(-1)!;
    assert.ok(document.activeElement === close);
    fireEvent.keyDown(document, { key: "Tab", shiftKey: true }); assert.ok(document.activeElement === last);
    fireEvent.keyDown(document, { key: "Tab" }); assert.ok(document.activeElement === close);
    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => assert.equal(trigger.getAttribute("aria-expanded"), "false"));
    assert.ok(document.activeElement === trigger); assert.equal(document.body.style.overflow, "scroll");
    assert.ok(sidebar.hasAttribute("inert")); assert.ok(!document.querySelector(".workspace")!.hasAttribute("inert"));
    fireEvent.click(trigger);
    await act(async () => { window.location.hash = "/bikes"; window.dispatchEvent(new Event("hashchange")); });
    await waitFor(() => assert.equal(trigger.getAttribute("aria-expanded"), "false"));
    fireEvent.click(trigger);
    await act(async () => { mobile = false; for (const listener of listeners) listener(); });
    assert.equal(trigger.getAttribute("aria-expanded"), "false"); assert.ok(!sidebar.hasAttribute("inert"));
    assert.equal(document.body.style.overflow, "scroll");
  } finally { cleanup(); window.matchMedia = originalMedia; document.body.style.overflow = ""; }
});

test("profile garage pages beyond the first batch remain accessible and failed loads can retry", async () => {
  let fail = true; const requested: number[] = [];
  globalThis.fetch = async input => {
    const url = new URL(String(input));
    if (!url.pathname.endsWith("/bikes")) return json(user);
    const current = Number(url.searchParams.get("page")); requested.push(current);
    assert.equal(url.searchParams.get("pageSize"), "20");
    if (fail) return json({ title: "Гараж временно недоступен" }, 503);
    return json(page(current === 1 ? [bike] : [bike, { ...bike, id: "other", name: "Второй велосипед" }], current, current === 1));
  };
  render(<UserProfile id="owner" me={null} version={0} notify={() => {}} reload={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText("Гараж временно недоступен")));
  assert.ok(screen.getByRole("heading", { name: user.name }));
  fail = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText(bike.name))); fireEvent.click(screen.getByText("Ещё велосипеды"));
  await waitFor(() => assert.ok(screen.getByText("Второй велосипед")));
  assert.equal(screen.getAllByText(bike.name).length, 1); assert.deepEqual(requested, [1, 1, 2]);
});

const group = { id: "group", name: "Велосообщество", category: "gravel", description: "Поездки", members: 2, isJoined: true, imageUrl: "" };
const entry = { id: "entry", groupId: group.id, groupName: group.name, title: "Разговор о поездках", content: "Обсудим маршрут вместе", kind: 2, author: user, imageIds: [], photos: [], createdAt: "2026-11-16T12:00:00Z", repliesCount: 3 };
test("a discussion recovers from initial and later page errors without stale replies or page skipping", async () => {
  let fail = true, failPage = true; const requested: number[] = [];
  globalThis.fetch = async input => {
    const url = new URL(String(input));
    if (fail) return json({ title: "Обсуждение недоступно" }, 503);
    if (url.pathname.endsWith("/replies")) {
      const current = Number(url.searchParams.get("page")); requested.push(current);
      return current === 2 && failPage ? json({ title: "Ответы временно недоступны" }, 503) : json(page([comment(String(current))], current, current === 1));
    }
    return json(url.pathname.startsWith("/api/communities/") ? group : entry);
  };
  render(<GroupEntryDetail id="entry" me={null} />);
  await waitFor(() => assert.ok(screen.getByText("Обсуждение недоступно")));
  fail = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("Комментарий 1"))); fireEvent.click(screen.getByText("Далее"));
  await waitFor(() => assert.ok(screen.getByText("Ответы временно недоступны")));
  assert.ok(screen.getByRole("heading", { name: entry.title })); assert.ok(!screen.queryByText("Комментарий 1"));
  failPage = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("Комментарий 2"))); assert.deepEqual(requested, [1, 2, 2]);
});

test("moderation load errors stop the spinner and can retry without losing the review note", async () => {
  let fail = true;
  globalThis.fetch = async () => fail ? json({ title: "Жалобы временно недоступны" }, 503) : json(page([{ id: "report", targetId: "post", targetType: 2, reason: "Проверить материал", createdAt: "2026-11-16T12:00:00Z" }]));
  render(<Moderation me={{ ...user, isModerator: true }} />);
  await waitFor(() => assert.ok(screen.getByText("Жалобы временно недоступны")));
  assert.ok(!screen.queryByText("Загружаем истории…")); assert.equal((screen.getByText("Далее") as HTMLButtonElement).disabled, true);
  fireEvent.change(screen.getByLabelText("Результат проверки"), { target: { value: "Проверяю обращение" } });
  fail = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("Проверить материал")));
  assert.equal((screen.getByLabelText("Результат проверки") as HTMLTextAreaElement).value, "Проверяю обращение");
});

test("changing accounts in the app clears an unpublished discussion reply", async () => {
  window.location.hash = "/group-entry/entry"; let viewer = user;
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user: viewer, enabled: true, csrfToken: "test" });
    if (path === "/api/notifications/unread-count") return json(0);
    if (path === "/api/feed/overview") return json({ tags: [], featuredBike: null });
    if (path === "/api/communities/catalog") return json(page([]));
    if (path === "/api/group-entries/entry") return json(entry);
    if (path === "/api/group-entries/entry/replies") return json(page([]));
    if (path === "/api/communities/group") return json(group);
    throw Error("Unexpected request: " + path);
  };
  render(<App />);
  await waitFor(() => assert.ok(screen.getByLabelText("Ваш ответ")));
  fireEvent.change(screen.getByLabelText("Ваш ответ"), { target: { value: "Личный ответ первого аккаунта" } });
  await act(async () => { viewer = { ...user, id: "other", handle: "other", name: "Другой участник" }; window.dispatchEvent(new Event("bikehub:content-changed")); });
  await waitFor(() => assert.equal(screen.getByLabelText("Открыть мой профиль").getAttribute("href"), "#/u/other"));
  await waitFor(() => assert.equal((screen.getByLabelText("Ваш ответ") as HTMLTextAreaElement).value, ""));
  assert.ok(!screen.queryByDisplayValue("Личный ответ первого аккаунта"));
});
