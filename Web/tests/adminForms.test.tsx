import { afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";
import type { User } from "../src/api";

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost:8080/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
const { render, screen, cleanup, act } = await import("@testing-library/react");
const { Admin } = await import("../src/pages/Admin");
const user: User = { id: "owner", name: "Owner", handle: "owner", city: "", initials: "O", color: "green", bio: "", followers: 0, isFollowing: false, bikesCount: 0, postsCount: 0 };
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
beforeEach(() => { globalThis.fetch = async () => { throw Error("Unexpected request"); }; });
afterEach(cleanup);

test("guests, members and moderators cannot request admin data", () => {
  let requests = 0; globalThis.fetch = async () => { requests++; return json({}); };
  const view = render(<Admin me={null} />);
  view.rerender(<Admin me={user} />);
  view.rerender(<Admin me={{ ...user, isModerator: true }} />);
  assert.ok(screen.getByRole("alert")); assert.equal(requests, 0);
});

test("an administrator sees server counts and links to moderation", async () => {
  globalThis.fetch = async input => {
    assert.equal(new URL(String(input)).pathname, "/api/admin/overview");
    return json({ accounts: 23, blockedAccounts: 2, openReports: 4 });
  };
  render(<Admin me={{ ...user, isAdmin: true, isModerator: true }} />);
  await screen.findByText("23");
  assert.ok(screen.getByText("2")); assert.ok(screen.getByText("4"));
  assert.equal(screen.getByRole("link", { name: "Публичные материалы" }).getAttribute("href"), "#/moderation");
});

test("losing the admin role hides loaded data and discards a late privileged response", async () => {
  let finish: (response: Response) => void = () => {};
  globalThis.fetch = async () => new Promise(resolve => { finish = resolve; });
  const view = render(<Admin me={{ ...user, isAdmin: true }} />);
  view.rerender(<Admin me={user} />);
  await act(async () => { finish(json({ accounts: 9876, blockedAccounts: 8, openReports: 9 })); });
  assert.equal(screen.queryByText("9876"), null);
  assert.equal(screen.queryByRole("link", { name: "Публичные материалы" }), null);
  assert.ok(screen.getByRole("alert"));
});
