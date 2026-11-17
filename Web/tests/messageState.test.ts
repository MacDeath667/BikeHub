import { test } from "node:test";
import assert from "node:assert/strict";
import type { DirectMessage } from "../src/api";
import { mergeMessages, prepareMessage } from "../src/components/messageState";

test("retry identity stays stable until the submitted content changes", () => {
  const first = prepareMessage(null, "  Привет  ");
  assert.equal(first.text, "Привет"); assert.match(first.clientMessageId, /^[a-f0-9-]{36}$/);
  assert.equal(prepareMessage(first, "Привет"), first);
  assert.notEqual(prepareMessage(first, "Другая мысль").clientMessageId, first.clientMessageId);
});
test("overlapping histories merge in sequence order and current deletion replaces cached text", () => {
  const message = (id: string, sequence: number): DirectMessage => ({ id, sequence, senderId: "sender", text: "Текст", isDeleted: false, isHidden: false, createdAt: "2026-11-17T12:00:00Z" });
  const first = message("first", 1), second = message("second", 2), third = message("third", 3);
  const result = mergeMessages([third, second], [first, second], [{ ...second, text: null, isDeleted: true }]);
  assert.deepEqual(result.map(m => m.sequence), [1, 2, 3]); assert.equal(result[1].text, null); assert.equal(result[1].isDeleted, true);
  const staleRead = mergeMessages(result, [second]);
  assert.equal(staleRead[1].text, null); assert.equal(staleRead[1].isDeleted, true);
});
