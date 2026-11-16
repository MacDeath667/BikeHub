import { beforeEach, afterEach, test } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";
const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost:8080/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, Event: dom.window.Event, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
const { render, screen, fireEvent, waitFor, cleanup, act } = await import("@testing-library/react");
const { SearchResults, searchHref, searchKind } = await import("../src/pages/SearchResults");
const { CommunityDetail } = await import("../src/pages/CommunityDetail");
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const person = (name: string) => ({ id: name, name, handle: "nickname", city: "Город", initials: "И", color: "green", bikesCount: 1, postsCount: 2 });
const page = (items: unknown[], pageNumber = 1, hasMore = false) => ({ items, total: hasMore ? 15 : items.length, pageNumber, pageSize: 10, hasMore });
const props = { search: "Гравий", kind: "people" as const, page: 1, version: 0, notify() {} };
beforeEach(() => { window.location.hash = "/"; globalThis.fetch = async () => { throw Error("Unexpected request"); }; });
afterEach(cleanup);

test("changing a search discards stale responses even if the old request ignores cancellation", async () => {
  let resolveOld: (response: Response) => void = () => {};
  const requests: URL[] = [];
  globalThis.fetch = async input => {
    const url = new URL(String(input)); requests.push(url);
    if (url.searchParams.get("q") === "Гравий") { return new Promise(resolve => { resolveOld = resolve; }); }
    return json(page([person("Новый человек")]));
  };
  const view = render(<SearchResults {...props} />);
  await waitFor(() => assert.equal(requests.length, 1));
  view.rerender(<SearchResults {...props} search="Казань" />);
  await waitFor(() => assert.ok(screen.getByText("Новый человек")));
  await act(async () => { resolveOld(json(page([person("Устаревший человек")]))); });
  assert.equal(screen.queryByText("Устаревший человек"), null);
  assert.equal(requests[1].searchParams.get("q"), "Казань"); assert.equal(requests[1].pathname, "/api/search/people");
  assert.equal(screen.getByText("Новый человек").closest("a")?.getAttribute("href"), "#/u/nickname");
});
test("tabs and paging retain literal queries, while a new query returns to the first page", async () => {
  const term = "100%_ & + #/Гравий";
  globalThis.fetch = async () => json(page([{ id: "group-id", name: "Группа <script>", category: "gravel", description: "Совместные поездки", members: 4, imageUrl: "", imageId: null }], 2, true));
  render(<SearchResults {...props} search={term} kind="communities" page={2} />);
  await waitFor(() => assert.ok(screen.getByText("Группа <script>")));
  const next = screen.getByText("Далее →").getAttribute("href")!;
  const nextQuery = new URLSearchParams(next.split("?")[1]); assert.equal(nextQuery.get("q"), term); assert.equal(nextQuery.get("page"), "3");
  assert.equal(new URLSearchParams(screen.getByText("Люди").getAttribute("href")!.split("?")[1]).get("page"), "1");
  fireEvent.change(screen.getByLabelText("Поисковый запрос"), { target: { value: "Новая тема" } });
  fireEvent.submit(screen.getByRole("search"));
  const changed = new URLSearchParams(window.location.hash.split("?")[1]); assert.equal(changed.get("page"), "1"); assert.equal(changed.get("q"), "Новая тема");
  assert.equal(document.querySelector("script"), null);
});
test("request errors can be retried and an empty later page has a path back to the beginning", async () => {
  let failing = true;
  globalThis.fetch = async () => failing ? json({ title: "Поиск недоступен" }, 503) : json(page([], 3));
  render(<SearchResults {...props} page={3} />);
  await waitFor(() => assert.ok(screen.getByText("Поиск недоступен")));
  failing = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("Ничего не нашлось")));
  assert.equal(screen.getByText("В начало").getAttribute("href"), searchHref(props.search, "people"));
});
test("blank search makes no request and unrecognised result kinds fall back to posts", async () => {
  let calls = 0; globalThis.fetch = async () => { calls++; return json(page([])); };
  render(<SearchResults {...props} search="  " />);
  assert.ok(screen.getByText("С чего начнём?")); assert.equal(calls, 0);
  for (const kind of [null, "", "constructor", "__proto__", "unknown"]) { assert.equal(searchKind(kind), "posts"); }
  assert.equal(searchKind("communities"), "communities");
});
test("a failed discussion tab stops loading and can retry without showing old results", async () => {
  let fail = true;
  globalThis.fetch = async input => {
    const url = new URL(String(input));
    if (!url.pathname.endsWith("/entries")) { return json({ id: "group", name: "Тестовое сообщество", category: "gravel", description: "Поездки", members: 2, imageUrl: "", imageId: null, isJoined: false }); }
    if (url.searchParams.get("kind") === "1") { return json(page([])); }
    if (fail) { return json({ title: "Обсуждения пока недоступны" }, 503); }
    return json(page([{ id: "entry", groupId: "group", title: "Восстановленное обсуждение", content: "Поговорим о поездке", photos: [], author: person("Участник"), createdAt: "2026-11-16T12:00:00Z", repliesCount: 1, kind: 2 }]));
  };
  render(<CommunityDetail id="group" me={null} reload={() => {}} />);
  await waitFor(() => assert.ok(screen.getByText("Здесь ещё нет постов")));
  fireEvent.click(screen.getByText("Обсуждения"));
  await waitFor(() => assert.ok(screen.getByText("Обсуждения пока недоступны")));
  assert.equal(screen.queryByText("Загружаем истории…"), null);
  fail = false; fireEvent.click(screen.getByText(/Попробовать снова/));
  await waitFor(() => assert.ok(screen.getByText("Восстановленное обсуждение")));
  assert.equal(screen.queryByText("Обсуждения пока недоступны"), null);
});
