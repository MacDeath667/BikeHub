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
