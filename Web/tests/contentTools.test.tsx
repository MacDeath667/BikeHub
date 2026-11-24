import { afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";
import type { Bike, Post, User } from "../src/api";

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost:8080/#/post/post" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, Event: dom.window.Event, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
dom.window.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
dom.window.HTMLDialogElement.prototype.close = function () { this.open = false; };
const { render, screen, fireEvent, cleanup, waitFor, act } = await import("@testing-library/react");
const { ContentTools } = await import("../src/components/ContentTools");
const { PostCard } = await import("../src/components/PostCard");
const { BikeDetail } = await import("../src/pages/Bikes");
const { Feed } = await import("../src/pages/Feed");
const { PostDetail } = await import("../src/pages/PostDetail");
const { SearchResults } = await import("../src/pages/SearchResults");
const author: User = { id: "author", name: "Автор", handle: "author", city: "", initials: "А", color: "green", bio: "", followers: 0, isFollowing: false, bikesCount: 1, postsCount: 1 };
const admin: User = { ...author, id: "admin", isAdmin: true, isModerator: true };
const bike: Bike = { id: "bike", ownerId: author.id, name: "Велосипед", description: "", brand: "Brand", category: "gravel", year: 2026, mileage: 0, imageUrl: "/bike.webp", color: "green", owner: author };
const post: Post = { id: "post", author, bike, title: "Запись для проверки", content: "Текст", category: "ride", imageUrl: "/post.webp", tags: [], createdAt: "2026-11-23T10:00:00Z", likes: 0, commentsCount: 0, isLiked: false, isSaved: false };
const noop = () => {};
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
let requests: string[];
beforeEach(() => {
  requests = [];
  window.localStorage.clear();
  window.location.hash = "/post/post";
  window.confirm = () => true;
  globalThis.fetch = async (input, init) => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/auth/session") {
      return json({ csrfToken: "test-csrf" });
    }
    requests.push(`${init?.method} ${path}`);
    assert.equal(new Headers(init?.headers).get("X-CSRF-TOKEN"), "test-csrf");
    return new Response(null, { status: 204 });
  };
});
afterEach(cleanup);

test("only the author and administrator see post deletion; administrators cannot edit foreign content", () => {
  const view = render(<ContentTools post={post} me={null} saved={noop} notify={noop} />);
  for (const me of [null, { ...author, id: "other" }, { ...author, id: "moderator", isModerator: true }]) {
    view.rerender(<ContentTools post={post} me={me} saved={noop} notify={noop} />);
    assert.equal(screen.queryByRole("button", { name: /Удалить/ }), null);
    assert.equal(screen.queryByRole("button", { name: "Редактировать" }), null);
  }
  view.rerender(<ContentTools post={post} me={author} saved={noop} notify={noop} />);
  assert.ok(screen.getByRole("button", { name: "Удалить", exact: true }));
  assert.ok(screen.getByRole("button", { name: "Редактировать" }));
  view.rerender(<ContentTools post={post} me={admin} saved={noop} notify={noop} />);
  assert.ok(screen.getByRole("button", { name: "Удалить запись пользователя" }));
  assert.equal(screen.queryByRole("button", { name: "Редактировать" }), null);
  view.rerender(<ContentTools bike={bike} me={admin} saved={noop} notify={noop} />);
  assert.equal(screen.queryByRole("button", { name: /Удалить/ }), null);
});

test("cancelling the admin confirmation leaves the post intact and makes no request", () => {
  let confirmation = "";
  window.confirm = message => { confirmation = message || ""; return false; };
  render(<ContentTools post={post} me={admin} saved={noop} notify={noop} />);
  fireEvent.click(screen.getByRole("button", { name: "Удалить запись пользователя" }));
  assert.ok(confirmation.includes(post.title));
  assert.ok(confirmation.includes(author.name));
  assert.equal(window.location.hash, "#/post/post");
  assert.deepEqual(requests, []);
});

test("admin deletion sends one CSRF-protected request, refreshes content and returns to the feed", async () => {
  let finish: (response: Response) => void = noop;
  const fetch = globalThis.fetch;
  globalThis.fetch = async (input, init) => {
    const response = await fetch(input, init);
    return init?.method === "DELETE" ? new Promise(resolve => { finish = resolve; }) : response;
  };
  let saved = 0, changed = 0;
  const notices: string[] = [];
  const onChanged = () => { changed++; };
  window.addEventListener("bikehub:content-changed", onChanged);
  try {
    render(<ContentTools post={post} me={admin} saved={() => { saved++; }} notify={text => notices.push(text)} />);
    const button = screen.getByRole("button", { name: "Удалить запись пользователя" }) as HTMLButtonElement;
    fireEvent.click(button);
    await waitFor(() => assert.equal(requests.length, 1));
    assert.equal(button.disabled, true);
    fireEvent.click(button);
    assert.deepEqual(requests, ["DELETE /api/posts/post"]);
    await act(async () => { finish(new Response(null, { status: 204 })); });
    assert.equal(saved, 1);
    assert.equal(changed, 1);
    assert.deepEqual(notices, ["Запись удалена"]);
    assert.equal(window.location.hash, "#/");
  } finally {
    window.removeEventListener("bikehub:content-changed", onChanged);
  }
});

test("a rejected deletion reports the error without navigating away or pretending success", async () => {
  const fetch = globalThis.fetch;
  globalThis.fetch = async (input, init) => {
    const response = await fetch(input, init);
    return init?.method === "DELETE" ? json({ title: "Необходимы права администратора." }, 403) : response;
  };
  const notices: string[] = [];
  render(<ContentTools post={post} me={admin} saved={() => assert.fail("Deletion was rejected")} notify={text => notices.push(text)} />);
  const button = screen.getByRole("button", { name: "Удалить запись пользователя" }) as HTMLButtonElement;
  fireEvent.click(button);
  await waitFor(() => assert.deepEqual(notices, ["Необходимы права администратора."]));
  assert.equal(window.location.hash, "#/post/post");
  assert.equal(button.disabled, false);
});

for (const type of ["post", "bike"] as const) {
  test(`the author can still delete their own ${type}`, async () => {
    let saved = false;
    render(<ContentTools {...(type === "post" ? { post } : { bike })} me={author} saved={() => { saved = true; }} notify={noop} />);
    fireEvent.click(screen.getByRole("button", { name: "Удалить", exact: true }));
    await waitFor(() => assert.equal(saved, true));
    assert.deepEqual(requests, [`DELETE /api/${type === "post" ? "posts/post" : "bikes/bike"}`]);
    assert.equal(window.location.hash, type === "post" ? "#/" : "#/garage");
  });
}

test("deleting from a card removes it without leaving the current bike page", async () => {
  window.location.hash = "/bike/bike";
  const notices: string[] = [];
  render(<PostCard post={post} me={admin} update={noop} notify={text => notices.push(text)} />);
  const button = screen.getByRole("button", { name: "Удалить запись пользователя" });
  assert.ok(button.closest(".post-footer"));
  fireEvent.click(button);
  await waitFor(() => assert.equal(screen.queryByRole("article") === null, true));
  assert.deepEqual(requests, ["DELETE /api/posts/post"]);
  assert.equal(window.location.hash, "#/bike/bike");
  assert.deepEqual(notices, ["Запись удалена"]);
});

test("editing from a card loads the full post before opening the form", async () => {
  const full = { ...post, title: "Полный заголовок", content: "Полное содержимое записи", photos: [{ id: "019a95c0-00ab-7000-8000-000000000001", imageUrl: "/attached.webp" }] };
  let finish: (response: Response) => void = noop;
  globalThis.fetch = async input => {
    const path = new URL(String(input)).pathname;
    if (path === "/api/posts/post") {
      return new Promise(resolve => { finish = resolve; });
    }
    if (path === "/api/users/author/bikes") {
      return json({ items: [bike] });
    }
    assert.equal(path, "/api/drafts/post-post");
    return json({ revision: 0, state: null, photos: [], updatedAt: null });
  };
  render(<PostCard post={post} me={author} update={noop} notify={noop} />);
  const button = screen.getByRole("button", { name: "Редактировать" }) as HTMLButtonElement;
  fireEvent.click(button);
  assert.equal(button.disabled, true);
  assert.equal(screen.queryByRole("dialog"), null);
  await act(async () => { finish(json(full)); });
  await waitFor(() => assert.equal((screen.getByLabelText("Заголовок") as HTMLInputElement).value, full.title));
  assert.equal((screen.getByLabelText("Ваша история") as HTMLTextAreaElement).value, full.content);
  assert.ok(screen.getByRole("img", { name: "Фотография 1" }));
});

for (const context of ["bike history", "feed", "search", "post detail"] as const) {
  test(`administrator actions are available in the footer of ${context}`, async () => {
    const page = (items: unknown[]) => ({ items, total: items.length, page: 1, pageSize: 5, hasMore: false });
    globalThis.fetch = async input => {
      const url = new URL(String(input));
      if (url.pathname === "/api/bikes/bike") {
        return json(bike);
      }
      if (url.pathname === "/api/posts/post") {
        return json(post);
      }
      if (url.pathname === "/api/posts/post/comments") {
        return json(page([]));
      }
      if (url.pathname === "/api/bikes/bike/history" || url.pathname === "/api/search/posts") {
        return json(page([post]));
      }
      assert.equal(url.pathname, "/api/feed/timeline");
      return json(page([{ id: "post:post", kind: 1, createdAt: post.createdAt, post, groupEntry: null }]));
    };
    const view = context === "bike history" ? <BikeDetail id={bike.id} me={admin} version={0} onCreate={noop} notify={noop} />
      : context === "feed" ? <Feed saved={false} search="" version={0} me={admin} notify={noop} onCreate={noop} />
      : context === "search" ? <SearchResults search="Запись" kind="posts" page={1} version={0} me={admin} notify={noop} />
      : <PostDetail id={post.id} me={admin} notify={noop} />;
    render(view);
    const button = await screen.findByRole("button", { name: "Удалить запись пользователя" });
    assert.ok(button.closest(".post-footer"));
    assert.equal(screen.queryByRole("button", { name: "Редактировать" }), null);
  });
}
