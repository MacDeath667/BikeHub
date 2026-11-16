import { test } from "node:test";
import assert from "node:assert/strict";
import { DraftSync, draftKey, materializeDraft, readDraftCache, wireDraft, writeDraftCache } from "../src/components/draftState.ts";
import type { DraftCache, DraftState } from "../src/components/draftState.ts";

const image = "019a95c0-00ab-7000-8000-000000000001";
const state: DraftState = { title: "История", content: "Длинная история", category: "ride", bikeId: null, imageIds: [image], route: { name: "Маршрут", segments: [], markers: [{ lat: 55, lng: 37, label: "Начало" }] } };
const response = (revision: number, value = state) => ({ revision, state: value, photos: [], updatedAt: "2026-11-16T12:00:00Z" });
const cache: DraftCache = { version: 1, state, revision: 4, pending: true, modifiedAt: 1000 };
function storage() {
  const entries = new Map<string, string>();
  return { entries, getItem: (key: string) => entries.get(key) ?? null, setItem: (key: string, value: string) => { entries.set(key, value); }, removeItem: (key: string) => { entries.delete(key); } };
}
test("recovery keys isolate accounts and publication contexts while retaining photo order and map", () => {
  const store = storage(), key = draftKey("owner", "post-new");
  assert.notEqual(key, draftKey("other", "post-new")); assert.notEqual(key, draftKey("owner", "group-new"));
  assert.ok(writeDraftCache(store, key, cache));
  const recovered = readDraftCache(store, key, 2000)!;
  assert.deepEqual(wireDraft(materializeDraft(recovered.state!, id => "/api/media/" + id)), state);
  assert.equal(readDraftCache(store, draftKey("other", "post-new"), 2000), null);
});
test("corrupt, stale and malformed recovery data is ignored without changing unrelated entries", () => {
  const store = storage(); store.setItem("unrelated", "keep me");
  for (const raw of ["broken JSON", JSON.stringify({ ...cache, version: 8 }), JSON.stringify({ ...cache, state: { ...state, imageIds: ["bad"] } }), JSON.stringify({ ...cache, state: { ...state, route: { ...state.route, markers: [{ lat: 100, lng: 0 }] } } })]) {
    store.setItem("draft", raw); assert.equal(readDraftCache(store, "draft", 2000), null);
  }
  store.setItem("draft", JSON.stringify(cache)); assert.equal(readDraftCache(store, "draft", 32 * 86400000), null);
  assert.equal(store.getItem("unrelated"), "keep me");
});
test("full or blocked storage preserves the previous copy and reports failure", () => {
  const store = storage(); store.setItem("draft", "previous");
  const blocked = { ...store, setItem() { throw new Error("quota exceeded"); } };
  assert.equal(writeDraftCache(blocked, "draft", cache), false); assert.equal(store.getItem("draft"), "previous");
  assert.equal(writeDraftCache(store, "draft", { ...cache, state: { ...state, content: "x".repeat(2600000) } }), false);
  assert.equal(store.getItem("draft"), "previous");
  assert.ok(writeDraftCache(store, "draft", { ...cache, state: null, pending: false }));
  assert.equal(readDraftCache(store, "draft", 2000)!.state, null);
});
test("overlapping saves coalesce to the latest snapshot and use monotonically increasing revisions", async () => {
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  const sent: { revision: number; text: string }[] = [], saved: number[] = [];
  const sync = new DraftSync(async (revision, value) => { sent.push({ revision, text: value.content }); if (sent.length === 1) { await gate; } return response(revision + 1, value); }, result => saved.push(result.revision));
  sync.initialise(4, state);
  const first = sync.flush(JSON.stringify({ ...state, content: "first" }));
  const middle = sync.flush(JSON.stringify({ ...state, content: "middle" }));
  const last = sync.flush(JSON.stringify({ ...state, content: "last" }));
  release(); await Promise.all([first, middle, last]);
  assert.deepEqual(sent, [{ revision: 4, text: "first" }, { revision: 5, text: "last" }]);
  assert.deepEqual(saved, [5, 6]); assert.equal(sync.revision, 6);
});
test("conflicts stop automatic retries and stopping a writer cannot resurrect an obsolete snapshot", async () => {
  let calls = 0;
  const failed = new DraftSync(async () => { calls++; throw Object.assign(new Error("conflict"), { status: 409 }); }, () => {});
  failed.initialise(1, state);
  await assert.rejects(failed.flush(JSON.stringify({ ...state, title: "changed" })));
  await assert.rejects(failed.flush(JSON.stringify({ ...state, title: "again" })));
  assert.equal(calls, 1);
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; }); const writes: string[] = [];
  const sync = new DraftSync(async (revision, value) => { writes.push(value.content); await gate; return response(revision + 1, value); }, () => {});
  sync.initialise(1, state);
  const pending = sync.flush(JSON.stringify({ ...state, content: "sending" }));
  const queued = sync.flush(JSON.stringify({ ...state, content: "obsolete" }));
  const stopped = sync.stop(); release(); await Promise.all([pending, queued, stopped]);
  assert.deepEqual(writes, ["sending"]);
  await assert.rejects(sync.flush(JSON.stringify(state)));
});
