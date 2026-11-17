import { afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";

const dom = new JSDOM('<!doctype html><html><head><meta name="theme-color" /></head><body></body></html>', { url: "https://example.test/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, localStorage: dom.window.localStorage, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
let dark = false; const listeners = new Set<() => void>();
window.matchMedia = () => ({ get matches() { return dark; }, addEventListener: (_event: string, listener: () => void) => listeners.add(listener), removeEventListener: (_event: string, listener: () => void) => listeners.delete(listener) }) as unknown as MediaQueryList;
const { render, screen, fireEvent, cleanup, act } = await import("@testing-library/react");
const { ThemePicker } = await import("../src/components/ThemePicker");
beforeEach(() => { localStorage.clear(); dark = false; });
afterEach(cleanup);

test("theme picker shows the selected icon, expands into three modes and collapses after choosing", async () => {
  localStorage.setItem("bikehub.theme", "dark"); render(<ThemePicker />);
  assert.equal(screen.getAllByRole("button").length, 1);
  fireEvent.click(screen.getByRole("button", { name: "Тёмная тема" }));
  assert.equal(screen.getAllByRole("button").length, 3);
  fireEvent.click(screen.getByRole("button", { name: "Светлая тема" }));
  assert.equal(screen.getAllByRole("button").length, 1);
  assert.equal(localStorage.getItem("bikehub.theme"), "light"); assert.equal(document.documentElement.dataset.theme, "light");
  assert.equal(document.activeElement, screen.getByRole("button", { name: "Светлая тема" }));
  fireEvent.click(screen.getByRole("button")); fireEvent.click(screen.getByRole("button", { name: "Как на устройстве" }));
  assert.equal(localStorage.getItem("bikehub.theme"), null);
  await act(async () => { dark = true; listeners.forEach(listener => listener()); });
  assert.equal(document.documentElement.dataset.theme, "dark");
  assert.ok(screen.getByRole("button", { name: "Как на устройстве" })); assert.equal(screen.getAllByRole("button").length, 1);
});

test("theme choice supports keyboard focus and dismisses without changing the mode", () => {
  render(<><ThemePicker /><button>Снаружи</button></>);
  const current = screen.getByRole("button", { name: "Как на устройстве" });
  fireEvent.click(current); fireEvent.keyDown(current, { key: "Home" });
  assert.equal(document.activeElement, screen.getByRole("button", { name: "Светлая тема" }));
  fireEvent.keyDown(document.activeElement!, { key: "Escape" });
  assert.equal(screen.queryByRole("button", { name: "Светлая тема" }), null); assert.equal(document.activeElement, current);
  fireEvent.click(current); fireEvent.pointerDown(screen.getByRole("button", { name: "Снаружи" }));
  assert.equal(screen.queryByRole("button", { name: "Светлая тема" }), null);
  assert.equal(localStorage.getItem("bikehub.theme"), null);
});
