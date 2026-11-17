import { afterEach, test } from "node:test";
import assert from "node:assert/strict";
import React, { useState } from "react";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://example.test/bikehub/" });
for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document, navigator: dom.window.navigator, HTMLElement: dom.window.HTMLElement, Event: dom.window.Event, IS_REACT_ACT_ENVIRONMENT: true })) {
  Object.defineProperty(globalThis, key, { configurable: true, writable: true, value });
}
const { render, screen, fireEvent, waitFor, cleanup, act } = await import("@testing-library/react");
const { CityInput } = await import("../src/components/CityInput");
const petersburg = { id: "spb", name: "Санкт-Петербург", region: "Санкт-Петербург", label: "Санкт-Петербург" };
const kirov = (region: string) => ({ id: region, name: "Киров", region, label: `Киров, ${region}` });
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function Form() {
  const [city, setCity] = useState("");
  return <form onSubmit={event => { event.preventDefault(); }}><label>Город<CityInput name="city" value={city} onValueChange={setCity} /></label><button>Сохранить</button></form>;
}
afterEach(cleanup);

test("city picker handles aliases, keyboard selection and region-qualified names without native dropdowns", async () => {
  const queries: URL[] = [];
  globalThis.fetch = async input => { const url = new URL(String(input)); queries.push(url); return json(url.searchParams.get("q") === "Питер" ? [petersburg] : [kirov("Калужская область"), kirov("Кировская область")]); };
  render(<Form />);
  const field = screen.getByRole("combobox", { name: "Город" }) as HTMLInputElement;
  fireEvent.focus(field); fireEvent.change(field, { target: { value: "Питер" } });
  await screen.findByRole("option", { name: /Санкт-Петербург/ });
  fireEvent.keyDown(field, { key: "ArrowDown" }); fireEvent.keyDown(field, { key: "Enter" });
  assert.equal(field.value, "Санкт-Петербург"); assert.equal(field.getAttribute("aria-expanded"), "false");
  fireEvent.change(field, { target: { value: "Киров" } });
  await waitFor(() => assert.equal(screen.getAllByRole("option").length, 2));
  fireEvent.keyDown(field, { key: "ArrowUp" }); fireEvent.keyDown(field, { key: "Enter" });
  assert.equal(field.value, "Киров, Кировская область");
  assert.ok(queries.every(url => url.origin === "https://example.test" && url.pathname === "/bikehub/api/cities"));
});

test("outdated city responses cannot replace a later query and failed requests can be retried", async () => {
  let release: (response: Response) => void = () => {}; let fail = false, calls = 0;
  globalThis.fetch = async input => {
    calls++;
    const term = new URL(String(input)).searchParams.get("q");
    if (term === "Питер") { return new Promise(resolve => { release = resolve; }); }
    return fail ? json({ title: "Unavailable" }, 503) : json([kirov("Кировская область")]);
  };
  render(<Form />); const field = screen.getByRole("combobox");
  fireEvent.focus(field); fireEvent.change(field, { target: { value: "Питер" } });
  await waitFor(() => assert.equal(calls, 1));
  fireEvent.change(field, { target: { value: "Киров" } });
  await screen.findByRole("option", { name: /Кировская область/ });
  await act(async () => { release(json([petersburg])); });
  assert.equal(screen.queryByRole("option", { name: /Санкт-Петербург/ }), null);
  fail = true; fireEvent.change(field, { target: { value: "Кировская" } });
  await screen.findByRole("alert"); assert.equal((field as HTMLInputElement).value, "Кировская");
  fail = false; fireEvent.click(screen.getByRole("button", { name: "Повторить" }));
  await screen.findByRole("option", { name: /Кировская область/ });
  fireEvent.keyDown(field, { key: "Escape" }); assert.equal(screen.queryByRole("listbox"), null);
});
