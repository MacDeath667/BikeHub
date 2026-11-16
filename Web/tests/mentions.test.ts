import { test } from "node:test";
import assert from "node:assert/strict";
import { completeMention, mentionAtCursor, mentionParts } from "../src/components/mentions.ts";

test("mention completion respects cursor, email boundaries and maximum nickname length", () => {
  assert.deepEqual(mentionAtCursor("Привет @rider!", 10), { start: 7, end: 13, query: "ri" });
  assert.equal(mentionAtCursor("mail@rider", 10), null);
  assert.equal(mentionAtCursor("слово@rider", 11), null);
  assert.equal(mentionAtCursor("@" + "a".repeat(33), 34), null);
  assert.equal(mentionAtCursor("@rider ", 7), null);
});
test("choosing a nickname replaces the full token without destroying following text", () => {
  const text = "Привет @rider, как дела?", range = mentionAtCursor(text, 10)!;
  assert.deepEqual(completeMention(text, range, "roman"), { value: "Привет @roman, как дела?", cursor: 13 });
  assert.deepEqual(completeMention("@ro", { start: 0, end: 3 }, "roman"), { value: "@roman ", cursor: 7 });
});
test("public text links valid nicknames while preserving literal text and email addresses", () => {
  const text = "@Rider и @other_name, mail@rider.test, @@rider, @ab, @" + "x".repeat(33);
  const parts = mentionParts(text);
  assert.deepEqual(parts.filter(p => p.nickname).map(p => p.nickname), ["rider", "other_name"]);
  assert.equal(parts.map(p => p.text).join(""), text);
});
