import { afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import React from "react";
import type { DraftCache, DraftState } from "../src/components/draftState";
import type { Bike, Post, User } from "../src/api";
import { draftKey } from "../src/components/draftState";

// DOM unit tests only: no browser process, network, real account or production storage.
const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost:8080/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, Event: dom.window.Event, FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true,
  requestAnimationFrame: (callback: FrameRequestCallback) => setTimeout(callback, 0), cancelAnimationFrame: clearTimeout })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
dom.window.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
dom.window.HTMLDialogElement.prototype.close = function () { this.open = false; };
const { render, screen, fireEvent, waitFor, cleanup, act } = await import("@testing-library/react");
const { useDraft, clearLocalDrafts } = await import("../src/components/useDraft");
const { DraftStatus } = await import("../src/components/DraftStatus");
const { CreateModal } = await import("../src/components/CreateModal");
const { ContentTools } = await import("../src/components/ContentTools");
const storage = window.localStorage;
const blank = { title: "", content: "", category: "ride", bikeId: null, photos: [], route: null };
const model = new Map<string, { revision: number; state: DraftState | null }>();
let owner = "owner", offline = false, releaseWrite: (() => void) | null = null, deferWrite = false;
const writes: DraftState[] = [];
let publication: Record<string, unknown> | null = null;
const user: User = { id: "owner", name: "Owner", handle: "owner", city: "", initials: "O", color: "", bio: "", followers: 0, isFollowing: false, bikesCount: 1, postsCount: 1 };
const bike: Bike = { id: "019a95c0-00ab-7000-8000-000000000001", ownerId: user.id, name: "My bike", description: "", brand: "Test", category: "gravel", year: 2025, mileage: 0, imageUrl: "", color: "", owner: user };
const post: Post = { id: "019a95c0-00ab-7000-8000-000000000002", author: user, bike, title: "Исходный заголовок", content: "Исходная история поездки", category: "ride", imageUrl: "", tags: [], createdAt: "2026-11-16T12:00:00Z", likes: 0, commentsCount: 0, isLiked: false, isSaved: false };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
beforeEach(() => {
  owner = "owner"; offline = false; deferWrite = false; releaseWrite = null; publication = null; model.clear(); writes.length = 0; storage.clear();
  Object.defineProperty(window, "localStorage", { configurable: true, value: storage });
  window.confirm = () => true;
  globalThis.fetch = async (input, init) => {
    const url = new URL(String(input));
    if (url.pathname === "/api/auth/session") { return json({ csrfToken: "test-csrf", user: null }); }
    if (offline) { throw new Error("offline"); }
    if (url.pathname === "/api/users/owner/bikes") { return json({ items: [bike], total: 1, pageNumber: 1, pageSize: 50, hasMore: false }); }
    if (url.pathname.startsWith("/api/posts/") && init?.method === "PUT") { publication = JSON.parse(init.body as string); return json({ id: post.id }); }
    assert.ok(url.pathname.startsWith("/api/drafts/"), `Unexpected request: ${url.pathname}`);
    const key = `${owner}:${url.pathname.split("/").at(-1)}`, current = model.get(key) || { revision: 0, state: null };
    if (init?.method === "PUT") {
      const body = JSON.parse(init.body as string); writes.push(body.state);
      if (deferWrite) { deferWrite = false; await new Promise<void>(resolve => { releaseWrite = resolve; }); }
      if (body.expectedRevision !== current.revision) { return json({ title: "Черновик изменён в другой вкладке" }, 409); }
      model.set(key, { revision: current.revision + 1, state: body.state });
    } else if (init?.method === "DELETE") {
      if (Number(url.searchParams.get("revision")) !== current.revision) { return json({ title: "Конфликт" }, 409); }
      model.set(key, { revision: current.revision + 1, state: null });
    }
    return json({ ...(model.get(key) || current), photos: [], updatedAt: new Date().toISOString() });
  };
});
afterEach(() => { releaseWrite?.(); cleanup(); Object.defineProperty(window, "localStorage", { configurable: true, value: storage }); });
function Editor({ account = "owner" }: { account?: string }) {
  const draft = useDraft(account, "post-new", blank);
  return <><span>{draft.ready ? "Ready" : "Loading"}</span><textarea aria-label="Draft title" disabled={!draft.ready || draft.actionBusy} value={draft.value.title} onChange={e => draft.setValue(v => ({ ...v, title: e.target.value }))} />
    <pre data-testid="value">{JSON.stringify(draft.value)}</pre>
    <button onClick={() => void draft.flush().catch(() => {})}>Flush</button><button onClick={() => void draft.finish()}>Published</button>
    <button onClick={() => draft.confirmClose()}>Close</button><DraftStatus draft={draft} disabled={draft.actionBusy} /></>;
}
async function ready() { await waitFor(() => assert.ok(screen.getByText("Ready"))); }
const title = () => screen.getByLabelText("Draft title") as HTMLTextAreaElement;
const value = (name: string): DraftState => ({ title: name, content: "Body", category: "ride", bikeId: null, imageIds: [], route: null });

test("actual React form autosaves, restores server text and keeps pending local edits on immediate close", async () => {
  const first = render(<Editor />); await ready();
  fireEvent.change(title(), { target: { value: "Автосохранение" } });
  await waitFor(() => assert.equal(model.get("owner:post-new")?.state?.title, "Автосохранение"), { timeout: 5000 });
  fireEvent.change(title(), { target: { value: "Последняя правка" } }); first.unmount();
  render(<Editor />); await ready(); assert.equal(title().value, "Последняя правка");
  fireEvent.click(screen.getByText("Flush"));
  await waitFor(() => assert.equal(model.get("owner:post-new")?.state?.title, "Последняя правка"));
});
test("cached text is isolated across accounts and logout clears only the departing account", async () => {
  const cached: DraftCache = { version: 1, modifiedAt: Date.now(), revision: 0, pending: true, state: value("Private owner text") };
  storage.setItem(draftKey("owner", "post-new"), JSON.stringify(cached));
  owner = "other"; const other = render(<Editor account="other" />); await ready(); assert.equal(title().value, ""); other.unmount();
  owner = "owner"; render(<Editor />); await ready(); assert.equal(title().value, "Private owner text");
  storage.setItem(draftKey("other", "post-new"), JSON.stringify(cached));
  clearLocalDrafts("owner"); assert.equal(storage.getItem(draftKey("owner", "post-new")), null); assert.notEqual(storage.getItem(draftKey("other", "post-new")), null);
});
test("a conflict preserves local text and requires an explicit choice before replacing the server version", async () => {
  model.set("owner:post-new", { revision: 2, state: value("Другой текст") });
  storage.setItem(draftKey("owner", "post-new"), JSON.stringify({ version: 1, modifiedAt: Date.now(), revision: 1, pending: true, state: value("Моя правка") }));
  render(<Editor />); await ready(); assert.equal(title().value, "Моя правка");
  fireEvent.click(screen.getByText("Flush")); assert.equal(writes.length, 0);
  await waitFor(() => assert.equal((screen.getByText("Сохранить эту версию") as HTMLButtonElement).disabled, false));
  fireEvent.click(screen.getByText("Сохранить эту версию"));
  await waitFor(() => assert.equal(model.get("owner:post-new")?.state?.title, "Моя правка"));
  assert.equal(model.get("owner:post-new")?.revision, 3);
});
test("publishing waits for an in-flight save and cannot resurrect queued obsolete text", async () => {
  render(<Editor />); await ready(); deferWrite = true;
  fireEvent.change(title(), { target: { value: "Sending" } }); fireEvent.click(screen.getByText("Flush"));
  await waitFor(() => assert.equal(writes.length, 1));
  fireEvent.change(title(), { target: { value: "Obsolete" } }); fireEvent.click(screen.getByText("Flush")); fireEvent.click(screen.getByText("Published"));
  await act(async () => { releaseWrite!(); });
  await waitFor(() => assert.equal(model.get("owner:post-new")?.revision, 2));
  assert.equal(model.get("owner:post-new")?.state, null); assert.equal(writes.length, 1);
  cleanup(); render(<Editor />); await ready(); assert.equal(title().value, "");
});
test("offline and full browser storage leave the typed text visible and warn before losing it", async () => {
  offline = true;
  Object.defineProperty(window, "localStorage", { configurable: true, value: { getItem: () => null, setItem() { throw new Error("quota"); } } });
  let warning = ""; window.confirm = message => { warning = String(message); return false; };
  render(<Editor />); await ready(); fireEvent.change(title(), { target: { value: "Не потерять" } }); fireEvent.click(screen.getByText("Close"));
  assert.equal(title().value, "Не потерять"); assert.match(warning, /потеряны/);
  assert.ok(screen.getByText(/страховочную копию/));
});
test("the post editor restores controlled fields, discards to the original and publishes the current form", async () => {
  model.set(`owner:post-${post.id}`, { revision: 1, state: { ...value("Серверный черновик"), bikeId: bike.id } });
  let published = "";
  render(<CreateModal type="post" me={user} initialPost={post} close={() => {}} onSuccess={id => { published = id; }} />);
  await waitFor(() => assert.equal((screen.getByLabelText("Заголовок") as HTMLInputElement).value, "Серверный черновик"));
  assert.equal((screen.getByLabelText("Ваша история") as HTMLTextAreaElement).value, "Body");
  fireEvent.click(screen.getByText("Удалить черновик"));
  await waitFor(() => assert.equal((screen.getByLabelText("Заголовок") as HTMLInputElement).value, post.title));
  assert.equal((screen.getByLabelText("Ваша история") as HTMLTextAreaElement).value, post.content);
  await waitFor(() => assert.equal((screen.getByLabelText("Заголовок") as HTMLInputElement).disabled, false));
  fireEvent.change(screen.getByLabelText("Заголовок"), { target: { value: "Новая версия поста" } });
  fireEvent.change(screen.getByLabelText("Ваша история"), { target: { value: "Новая история после восстановления" } });
  fireEvent.submit(screen.getByLabelText("Заголовок").closest("form")!);
  await waitFor(() => assert.equal(published, post.id));
  assert.equal(publication?.title, "Новая версия поста"); assert.equal(publication?.content, "Новая история после восстановления"); assert.equal(publication?.bikeId, bike.id);
  assert.equal(model.get(`owner:post-${post.id}`)?.state, null);
});
test("switching away from the author unmounts an open private post editor", async () => {
  const props = { post, saved() {}, notify() {} };
  const view = render(<ContentTools {...props} me={user} />);
  fireEvent.click(screen.getByText("Редактировать")); await waitFor(() => assert.ok(screen.getByLabelText("Заголовок")));
  fireEvent.change(screen.getByLabelText("Заголовок"), { target: { value: "Private edit" } });
  view.rerender(<ContentTools {...props} me={{ ...user, id: "other" }} />);
  assert.equal(screen.queryByLabelText("Заголовок"), null);
  await new Promise(resolve => setTimeout(resolve, 2200)); assert.equal(writes.length, 0);
});

for (const type of ["post", "bike"] as const) {
  for (const editing of [false, true]) {
    test(`${editing ? "editing" : "creating"} a ${type} keeps text when a selection ends outside and closes only explicitly`, async () => {
      let closed = 0, confirmations = 0;
      window.confirm = () => { confirmations++; return true; };
      render(<CreateModal type={type} me={user} initialPost={editing && type === "post" ? post : undefined}
        initialBike={editing && type === "bike" ? bike : undefined} close={() => { closed++; }} onSuccess={() => {}} />);
      const field = await screen.findByLabelText(type === "post" ? "Ваша история" : "Немного о нём") as HTMLTextAreaElement;
      fireEvent.change(field, { target: { value: "Правка, которую нельзя потерять" } });
      const dialog = screen.getByRole("dialog") as HTMLDialogElement;
      dialog.getBoundingClientRect = () => ({ left: 10, top: 10, right: 510, bottom: 610 }) as DOMRect;
      fireEvent.pointerDown(field, { clientX: 30, clientY: 40 });
      fireEvent.pointerUp(dialog, { clientX: 600, clientY: 700 });
      fireEvent.click(dialog, { clientX: 600, clientY: 700 });
      fireEvent.click(dialog, { clientX: 0, clientY: 0 });
      assert.equal(closed, 0); assert.equal(confirmations, 0); assert.equal(dialog.open, true);
      assert.equal(field.value, "Правка, которую нельзя потерять");
      fireEvent.click(screen.getByLabelText("Закрыть")); assert.equal(closed, 1);
    });
  }
}
