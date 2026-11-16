import { afterEach, test } from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://example.test/bikehub/" });
Object.defineProperty(globalThis, "window", { configurable: true, value: dom.window });
const { api, ApiError } = await import("../src/api");
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => { globalThis.fetch = async () => { throw Error("Unexpected request"); }; });

test("a stalled read is cancelled after its deadline without retrying", async context => {
  context.mock.timers.enable({ apis: ["setTimeout"] }); let calls = 0, signal: AbortSignal | null = null;
  globalThis.fetch = async (_input, options) => {
    calls++; signal = options!.signal!;
    return new Promise((_resolve, reject) => { signal!.addEventListener("abort", () => reject(signal!.reason), { once: true }); });
  };
  const pending = assert.rejects(api("/feed"), /не ответил вовремя/);
  context.mock.timers.tick(60000); await pending;
  assert.equal((signal as AbortSignal | null)?.aborted, true); assert.equal(calls, 1);
});

test("the deadline includes reading a response body rather than just waiting for headers", async context => {
  context.mock.timers.enable({ apis: ["setTimeout"] });
  globalThis.fetch = async (_input, options) => new Response(new ReadableStream({ start(controller) {
    options!.signal!.addEventListener("abort", () => controller.error(options!.signal!.reason), { once: true });
  } }), { headers: { "Content-Type": "application/json" } });
  const request = api("/feed"); const pending = assert.rejects(request, /не ответил вовремя/);
  await Promise.resolve(); context.mock.timers.tick(60000); await pending;
});

test("caller cancellation stays an abort instead of becoming a connectivity error", async context => {
  context.mock.timers.enable({ apis: ["setTimeout"] }); const caller = new AbortController(); let requestSignal: AbortSignal | null = null;
  globalThis.fetch = async (_input, options) => {
    requestSignal = options!.signal!;
    return new Promise((_resolve, reject) => { requestSignal!.addEventListener("abort", () => reject(requestSignal!.reason), { once: true }); });
  };
  const pending = assert.rejects(api("/search/people?q=test", { signal: caller.signal }), { name: "AbortError" });
  caller.abort(); await pending;
  assert.equal((requestSignal as AbortSignal | null)?.aborted, true); context.mock.timers.tick(120000);
});

test("a timed-out write is never retried and uploads get a longer deadline", async context => {
  globalThis.fetch = async () => json({ csrfToken: "test-csrf" }); await api("/auth/session");
  context.mock.timers.enable({ apis: ["setTimeout"] }); let calls = 0, aborted = false;
  globalThis.fetch = async (_input, options) => {
    calls++;
    return new Promise((_resolve, reject) => { options!.signal!.addEventListener("abort", () => { aborted = true; reject(options!.signal!.reason); }, { once: true }); });
  };
  const form = new FormData(); form.append("file", new Blob(["synthetic"]), "fixture.webp");
  const pending = assert.rejects(api("/media", { method: "POST", body: form }), /Действие могло выполниться/);
  context.mock.timers.tick(60000); assert.equal(aborted, false);
  context.mock.timers.tick(60000); await pending; assert.equal(aborted, true); assert.equal(calls, 1);
});

test("successful path-proxy responses resolve image URLs and clear their deadline", async context => {
  context.mock.timers.enable({ apis: ["setTimeout"] }); let signal: AbortSignal | null = null;
  globalThis.fetch = async (input, options) => {
    assert.equal(String(input), "https://example.test/bikehub/api/posts/post"); signal = options!.signal!;
    return json({ photos: [{ imageUrl: "/api/media/photo" }], author: { avatarUrl: "/api/media/avatar" }, external: { imageUrl: "https://cdn.example.test/image.webp" } });
  };
  const result = await api<{ photos: { imageUrl: string }[]; author: { avatarUrl: string }; external: { imageUrl: string } }>("/posts/post");
  assert.equal(result.photos[0].imageUrl, "https://example.test/bikehub/api/media/photo");
  assert.equal(result.author.avatarUrl, "https://example.test/bikehub/api/media/avatar");
  assert.equal(result.external.imageUrl, "https://cdn.example.test/image.webp");
  context.mock.timers.tick(120000); assert.equal((signal as AbortSignal | null)?.aborted, false);
});

test("HTTP validation errors retain their type and message", async () => {
  globalThis.fetch = async () => json({ errors: { Title: ["Заполните заголовок."] } }, 400);
  await assert.rejects(api("/posts/post"), error => error instanceof ApiError && error.status === 400 && error.message === "Заполните заголовок.");
});
