import { test } from "node:test";
import assert from "node:assert/strict";
import { setTimeout as pause } from "node:timers/promises";
import { attachHeldDrag } from "../src/components/heldDrag.ts";

class Pin extends EventTarget {
  classes = new Set<string>();
  classList = { add: (name: string) => this.classes.add(name), remove: (name: string) => this.classes.delete(name) };
  captured: number | null = null;
  setPointerCapture(id: number) { this.captured = id; }
  hasPointerCapture(id: number) { return this.captured === id; }
  releasePointerCapture(id: number) { this.captured = null; this.pointer("lostpointercapture", 0, 0, id); }
  pointer(type: string, x = 10, y = 10, id = 1) {
    const event = new Event(type, { cancelable: true });
    Object.assign(event, { pointerId: id, isPrimary: true, button: 0, clientX: x, clientY: y });
    this.dispatchEvent(event); return event;
  }
}
function setup() {
  const element = new Pin(); let locked = false, position = [0, 0], saved = [0, 0], commits = 0;
  const cleanup = attachHeldDrag(element as unknown as HTMLElement, {
    lock: () => { locked = true; }, unlock: () => { locked = false; },
    move: (x, y) => { position = [x, y]; }, finish: () => { saved = [...position]; commits++; }, cancel: () => { position = [...saved]; }
  }, 10);
  return { element, cleanup, read: () => ({ locked, position, saved, commits }) };
}
test("a short tap does not move or save a point and cannot add a duplicate", () => {
  const pin = setup(); pin.element.pointer("pointerdown"); pin.element.pointer("pointerup");
  assert.equal(pin.read().commits, 0); assert.equal(pin.read().locked, false);
  assert.equal(pin.element.pointer("click").defaultPrevented, true); pin.cleanup();
});
test("hold then move commits once and restores map panning", async () => {
  const pin = setup(); pin.element.pointer("pointerdown"); await pause(20);
  assert.equal(pin.element.classes.has("moving"), true);
  pin.element.pointer("pointermove", 40, 50); assert.deepEqual(pin.read().position, [40, 50]);
  pin.element.pointer("pointerup", 40, 50); assert.deepEqual(pin.read().saved, [40, 50]);
  assert.equal(pin.read().commits, 1); assert.equal(pin.read().locked, false); pin.cleanup();
});
test("moving before the hold cancels the gesture", async () => {
  const pin = setup(); pin.element.pointer("pointerdown"); pin.element.pointer("pointermove", 50, 50); await pause(20);
  assert.equal(pin.element.classes.has("moving"), false); assert.equal(pin.read().locked, false);
  pin.element.pointer("pointerup"); assert.equal(pin.read().commits, 0); pin.cleanup();
});
test("cancellation and lost capture roll back unsaved coordinates", async () => {
  for (const event of ["pointercancel", "lostpointercapture"]) {
    const pin = setup(); pin.element.pointer("pointerdown"); await pause(20); pin.element.pointer("pointermove", 40, 50); pin.element.pointer(event);
    assert.deepEqual(pin.read().position, [0, 0]); assert.equal(pin.read().commits, 0); assert.equal(pin.read().locked, false); pin.cleanup();
  }
});
test("unmount clears a pending hold and restores an active drag", async () => {
  const pending = setup(); pending.element.pointer("pointerdown"); pending.cleanup(); await pause(20);
  assert.equal(pending.element.classes.has("moving"), false); assert.equal(pending.read().locked, false);
  const active = setup(); active.element.pointer("pointerdown"); await pause(20); active.element.pointer("pointermove", 40, 50); active.cleanup();
  assert.deepEqual(active.read().position, [0, 0]); assert.equal(active.read().locked, false);
});
