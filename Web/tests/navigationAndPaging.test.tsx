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
const { Maintenance } = await import("../src/pages/Maintenance");
const { LibraryRouteDetail } = await import("../src/pages/RouteLibrary");
const { RideDetail } = await import("../src/pages/Rides");
const { Moderation } = await import("../src/pages/Moderation");
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const user = { id: "owner", name: "Велосипедист", handle: "rider", city: "Казань", initials: "В", color: "green", bio: "", followers: 0, isFollowing: false, bikesCount: 1, postsCount: 2 };
const bike = { id: "bike", name: "Гравийный велосипед", ownerId: user.id, brand: "Test", description: "Описание велосипеда", category: "gravel", year: 2025, mileage: 100, imageUrl: "/test-bike.webp", color: "", owner: user };
const post = (id: string) => ({ id, title: "История " + id, content: "Поездка", author: user, bike, category: "ride", imageUrl: "/test-post.webp", tags: [], createdAt: "2026-11-16T12:00:00Z", likes: 0, commentsCount: 3, isLiked: false, isSaved: false });
const feedPost = (id: string) => ({ id: "post:" + id, kind: 1, createdAt: post(id).createdAt, post: post(id), groupEntry: null });
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
    return current === 1 ? json(page([feedPost("1"), feedPost("2")], 1, true)) : fail ? json({ title: "Лента временно недоступна" }, 503) : json(page([feedPost("2"), feedPost("3")], 2));
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


test("mixed feed retains distinct post and community IDs and nested post reactions", async () => {
  const story = feedPost("same-id");
  const entry = { id: "same-id", groupId: "group", groupName: "Вместе по городу", author: user, kind: 2, title: "Обсудим выходные", content: "Куда поедем?", photos: [], imageIds: [], createdAt: story.createdAt, repliesCount: 4 };
  const paths: string[] = [];
  globalThis.fetch = async (input, options) => {
    const url = new URL(String(input)); paths.push(url.pathname);
    if (url.pathname === "/api/auth/session") return json({ user, csrfToken: "test", enabled: true });
    if (url.pathname.endsWith("/like")) { assert.equal(options?.method, "PUT"); return json({ ...story.post, likes: 1, isLiked: true }); }
    assert.equal(url.pathname, "/api/feed/timeline");
    return json(page([story, { id: "group:same-id", kind: 2, createdAt: entry.createdAt, post: null, groupEntry: entry }]));
  };
  render(<Feed saved={false} search="" version={0} me={user} notify={() => {}} onCreate={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText(entry.title)));
  assert.ok(screen.getByText(story.post.title));
  assert.equal(screen.getByText(entry.groupName).closest("a")?.getAttribute("href"), "#/community/group");
  fireEvent.click(screen.getByLabelText("Нравится"));
  await waitFor(() => assert.equal(screen.getByLabelText("Убрать лайк").getAttribute("aria-pressed"), "true"));
  assert.ok(screen.getByText(entry.title)); assert.ok(paths.includes("/api/posts/same-id/like"));
});

test("replying selects context, sends its ID, and can open a comment outside the current page", async () => {
  const original = comment("parent"), reply = { ...comment("reply"), text: "Ответ с контекстом", replyToId: original.id, replyTo: { id: original.id, authorName: original.author.name, authorHandle: original.author.handle, text: original.text } };
  let sent: unknown;
  globalThis.fetch = async (input, options) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user, csrfToken: "test", enabled: true });
    if (path.endsWith("/comments/reply")) return json(reply);
    if (path.endsWith("/comments")) {
      if (options?.method === "POST") { sent = JSON.parse(String(options.body)); return json(reply); }
      return json(page([original]));
    }
    return json(post("1"));
  };
  render(<PostDetail id="1" me={user} targetComment="reply" notify={() => {}} />);
  await waitFor(() => assert.ok(screen.getByLabelText("Комментарий по ссылке")));
  assert.ok(screen.getByText(reply.text));
  fireEvent.click(screen.getAllByText("Ответить").at(-1)!);
  assert.ok(screen.getByLabelText("Отменить ответ на комментарий"));
  fireEvent.change(screen.getByPlaceholderText("Поделитесь опытом или задайте вопрос…"), { target: { value: reply.text } });
  fireEvent.click(screen.getByLabelText("Отправить комментарий"));
  await waitFor(() => assert.deepEqual(sent, { text: reply.text, replyToId: original.id }));
  await waitFor(() => assert.ok(!screen.queryByLabelText("Отменить ответ на комментарий")));
  assert.equal(screen.getAllByText(reply.text).length, 1);
  assert.equal(screen.getAllByLabelText(`К комментарию ${user.name}`).at(-1)?.getAttribute("href"), "#/post/1?comment=parent");
});


const groupRide = { id: "ride", organizer: user, groupId: null, groupName: null, title: "Поездка по городу", description: "Встречаемся и едем", city: "Казань", meetingName: "Вход в парк", meetingPoint: null, startsAt: "2027-05-01T07:00:00Z", timeZone: "Europe/Moscow", distanceKm: 35, averageSpeedKmh: 20, difficulty: 1 as const, capacity: 2, status: 1 as const, revision: 1, going: 2, maybe: 0, attendance: null, hasRoute: false, route: null };

test("a full ride preserves its detail and allows maybe without retrying the failed mutation", async () => {
  const member = { ...user, id: "member" }; let puts = 0, rosterReads = 0;
  globalThis.fetch = async (input, options) => {
    const url = new URL(String(input));
    if (url.pathname === "/api/auth/session") return json({ user: member, enabled: true, csrfToken: "test" });
    if (url.pathname.endsWith("/attendance")) {
      puts++; const body = JSON.parse(String(options?.body));
      return body.attendance === 1 ? json({ title: "Свободных мест больше нет" }, 409) : json({ ...groupRide, attendance: 2, maybe: 1 });
    }
    if (url.pathname.endsWith("/participants")) { rosterReads++; assert.equal(url.searchParams.get("pageSize"), "20"); return json(page([{ user, attendance: 1 }])); }
    return json(groupRide);
  };
  render(<RideDetail id="ride" me={member} />);
  await waitFor(() => assert.ok(screen.getByRole("heading", { name: groupRide.title, level: 1 })));
  fireEvent.click(screen.getByText("Еду"));
  await waitFor(() => assert.ok(screen.getByText("Свободных мест больше нет")));
  assert.equal(screen.getByText("Еду").getAttribute("aria-pressed"), "false"); assert.equal(puts, 1);
  fireEvent.click(screen.getByText("Возможно"));
  await waitFor(() => assert.equal(screen.getByText("Возможно").getAttribute("aria-pressed"), "true"));
  assert.equal(puts, 2); assert.ok(!screen.queryByText("Свободных мест больше нет"));
  fireEvent.click(screen.getByText(/Участники: 2 едут/));
  await waitFor(() => assert.ok(screen.getByText(`${user.name} @${user.handle}`))); assert.equal(rosterReads, 1);
});

test("new rides submit the chosen meeting zone as a UTC instant and enforce signed-in creation", async () => {
  let submitted: Record<string, unknown> | undefined;
  globalThis.fetch = async (input, options) => {
    if (new URL(String(input)).pathname === "/api/auth/session") return json({ user, enabled: true, csrfToken: "test" });
    submitted = JSON.parse(String(options?.body)); return json({ ...groupRide, ...submitted });
  };
  const view = render(<RideDetail id="new" me={null} />);
  assert.ok(screen.getByText("Войдите, чтобы пригласить участников.")); assert.ok(!screen.queryByLabelText("Название"));
  view.rerender(<RideDetail id="new" me={user} />);
  fireEvent.change(screen.getByLabelText("Название"), { target: { value: "Утренний круг" } });
  fireEvent.change(screen.getByLabelText("О поездке"), { target: { value: "Ровный темп, без гонки" } });
  fireEvent.change(screen.getByLabelText("Город"), { target: { value: "Казань" } });
  fireEvent.change(screen.getByLabelText("Дата и время встречи"), { target: { value: "2027-05-01T10:00" } });
  fireEvent.change(screen.getByLabelText("Часовой пояс"), { target: { value: "Europe/Moscow" } });
  fireEvent.change(screen.getByLabelText("Где встречаемся"), { target: { value: "Вход в парк" } });
  fireEvent.change(screen.getByLabelText("Дистанция, км"), { target: { value: "35" } });
  fireEvent.submit(screen.getByRole("button", { name: "Опубликовать покатушку" }).closest("form")!);
  await waitFor(() => assert.equal(submitted?.startsAt, "2027-05-01T07:00:00.000Z"));
  assert.equal(submitted?.timeZone, "Europe/Moscow"); assert.equal(submitted?.capacity, null); assert.equal(submitted?.groupId, null);
  await waitFor(() => assert.equal(window.location.hash, "#/ride/ride"));
});


test("private route notes survive a failed save and can be retried without reloading the page", async () => {
  const value = { id: "library", author: user, title: "Речной маршрут", description: "Дорога у воды", city: "Казань", surface: 2 as const, distanceKm: 11.12, revision: 1, createdAt: "2026-11-17T10:00:00Z", reviewsCount: 0, rating: null, isSaved: true, collectionId: null, myNote: "Прежняя личная заметка", route: null };
  let fail = true, writes = 0;
  globalThis.fetch = async (input, options) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") return json({ user, enabled: true, csrfToken: "test" });
    if (path === "/api/route-collections") return json([{ id: "collection", name: "Выходные", routesCount: 0 }]);
    if (path.endsWith("/reviews")) return json(page([]));
    if (path.endsWith("/bookmark")) { writes++; const body = JSON.parse(String(options?.body)); return fail ? json({ title: "Сохранение временно недоступно" }, 503) : json({ ...value, myNote: body.note, collectionId: body.collectionId }); }
    return json(value);
  };
  render(<LibraryRouteDetail id="library" me={user} />);
  await waitFor(() => assert.ok(screen.getByLabelText("Личная заметка")));
  await waitFor(() => assert.ok(screen.getByRole("option", { name: "Выходные" })));
  const note = screen.getByLabelText("Личная заметка") as HTMLTextAreaElement;
  fireEvent.change(note, { target: { value: "Заехать за водой до старта" } });
  fireEvent.change(screen.getByLabelText("Подборка"), { target: { value: "collection" } });
  fireEvent.click(screen.getByText("Сохранить заметку и маршрут"));
  await waitFor(() => assert.ok(screen.getByText("Сохранение временно недоступно")));
  assert.equal(note.value, "Заехать за водой до старта"); assert.equal(writes, 1);
  fail = false; fireEvent.click(screen.getByText("Сохранить заметку и маршрут"));
  await waitFor(() => assert.ok(!screen.queryByText("Сохранение временно недоступно")));
  assert.equal(note.value, "Заехать за водой до старта"); assert.equal(writes, 2);
  assert.equal((screen.getByLabelText("Подборка") as HTMLSelectElement).value, "collection");
});


test("maintenance is private and a failed history page leaves the bike and edited odometer intact", async () => {
  let historyFails = true, historyReads = 0, requests = 0;
  const overview = { bikeId: "bike", bikeName: bike.name, mileage: 100, activeComponents: 1, dueComponents: 1 };
  const component = { id: "chain", bikeId: "bike", kind: 1, name: "Цепь с напоминанием", model: "Fixture", notes: "Личная заметка", installedMileage: 0, installedOn: "2026-11-01", serviceIntervalKm: 100, lastServiceMileage: 0, dueMileage: 100, currentMileage: 100, isArchived: false, revision: 1, isDue: true };
  globalThis.fetch = async input => {
    requests++; const path = new URL(String(input)).pathname;
    if (path.endsWith("/components/options")) return json([{ id: component.id, name: component.name, isArchived: false }]);
    if (path.endsWith("/components")) return json(page([component]));
    if (path.endsWith("/records")) { historyReads++; return historyFails ? json({ title: "История временно недоступна" }, 503) : json(page([])); }
    return json(overview);
  };
  const view = render(<Maintenance bikeId="bike" me={null} />);
  assert.ok(screen.getByText(/История, заметки и расходы видны только владельцу/)); assert.equal(requests, 0);
  view.rerender(<Maintenance bikeId="bike" me={user} />);
  await waitFor(() => assert.ok(screen.getByText("История временно недоступна")));
  assert.ok(screen.getByText(component.name));
  const input = screen.getByLabelText("Текущий пробег, км") as HTMLInputElement;
  fireEvent.change(input, { target: { value: "125" } });
  historyFails = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(!screen.queryByText("История временно недоступна")));
  assert.equal(input.value, "125"); assert.equal(historyReads, 2); assert.ok(screen.getByText(component.name));
});
