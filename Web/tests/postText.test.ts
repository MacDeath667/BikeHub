import { test } from "node:test";
import assert from "node:assert/strict";
import { formatSelection, galleryPhotos, inlinePhotoIds, insertPhoto, maximumPostLength, parsePostText, postExcerpt, removePhotoReferences } from "../src/components/postMarkup.ts";

const first = "019a95c0-00ab-7000-8000-000000000001";
const second = "019a95c0-00ab-7000-8000-000000000002";
const foreign = "019a95c0-00ab-7000-8000-000000000003";
const image = (id: string) => `![Лес](media:${id})`;
const photos = [{ id: first, imageUrl: "/one.webp" }, { id: second, imageUrl: "/two.webp" }];

test("legacy plain text, whitespace and single underscores remain intact", () => {
  const content = "Первый абзац\n\nВторой_абзац\nСтрока";
  assert.deepEqual(parsePostText(content), [{ kind: "text", text: content }]);
});
test("requested styles and nested bold/italic are parsed without HTML", () => {
  assert.deepEqual(parsePostText("*курсив* __линия__ ~~было~~ ||секрет||"), [
    { kind: "italic", children: [{ kind: "text", text: "курсив" }] }, { kind: "text", text: " " },
    { kind: "underline", children: [{ kind: "text", text: "линия" }] }, { kind: "text", text: " " },
    { kind: "strike", children: [{ kind: "text", text: "было" }] }, { kind: "text", text: " " },
    { kind: "spoiler", children: [{ kind: "text", text: "секрет" }] },
  ]);
  assert.deepEqual(parsePostText("***важное***"), [{ kind: "bold", children: [{ kind: "italic", children: [{ kind: "text", text: "важное" }] }] }]);
  assert.deepEqual(parsePostText("__до *внутри* после__")[0], { kind: "underline", children: [
    { kind: "text", text: "до " }, { kind: "italic", children: [{ kind: "text", text: "внутри" }] }, { kind: "text", text: " после" },
  ] });
});
test("unclosed formatting and escaped markers stay readable", () => {
  assert.equal(postExcerpt("До ||не закрыто *слово*"), "До ||не закрыто слово");
  assert.equal(postExcerpt(String.raw`\*курсив\* \_\_линия\_\_ \|\|текст\|\|`), "*курсив* __линия__ ||текст||");
  assert.deepEqual(parsePostText("****"), [{ kind: "text", text: "****" }]);
});
test("only valid attached image IDs are excluded from the gallery, including inside spoilers", () => {
  const content = `${image(first.toUpperCase())}\n||${image(first)}||\n${image(foreign)}`;
  assert.deepEqual([...inlinePhotoIds(content, photos)], [first]);
  assert.deepEqual(galleryPhotos(content, photos), [photos[1]]);
  assert.deepEqual(galleryPhotos(`${image(first)} ${image(second)}`, photos), []);
  assert.deepEqual(galleryPhotos("Без вставок", photos), photos);
});
test("escaped images, external URLs, raw HTML and malformed IDs never become image nodes", () => {
  for (const content of [String.raw`\${image(first)}`, "![remote](https://evil.test/track)", "<img src=x onerror=alert(1)>", "![x](javascript:alert(1))", "![x](media:wrong-id)"]) {
    assert.deepEqual(galleryPhotos(content, photos), photos);
    assert.ok(parsePostText(content).every(node => node.kind === "text"));
  }
});
test("feed excerpts hide spoiler text and image markup before truncation", () => {
  assert.equal(postExcerpt(`*Привет* ||НЕ ПОКАЗЫВАТЬ ${image(first)}|| ${image(second)} __мир__`), "Привет [Спойлер] мир");
  assert.equal(postExcerpt("**Длинная история**", 7), "Длинная…");
});
test("removing an attachment removes its inline references but leaves other text and escaped examples", () => {
  const escaped = `\\${image(first)}`;
  assert.equal(removePhotoReferences(`a${image(first)}b${image(first.toUpperCase())}c${image(second)} ${escaped}`, [first]), `abc${image(second)} ${escaped}`);
});
test("toolbar wraps exactly the selection and photo insertion preserves surrounding text", () => {
  assert.deepEqual(formatSelection("до слово после", 3, 8, "__"), { content: "до __слово__ после", start: 5, end: 10 });
  assert.deepEqual(formatSelection("", 0, 0, "*"), { content: "*текст*", start: 1, end: 6 });
  const inserted = insertPhoto("ПервоеВторое", 6, 6, first);
  assert.equal(inserted.content, `Первое\n\n![Фотография](media:${first})\n\nВторое`);
  assert.equal(inserted.start, inserted.end);
  assert.equal(inserted.content.slice(inserted.end), "Второе");
});
test("large and malformed input stays bounded and does not lose text", () => {
  const content = "*".repeat(maximumPostLength);
  assert.equal(postExcerpt(content, maximumPostLength), content);
  assert.ok(parsePostText("||__~~**".repeat(2500)).length > 0);
});
