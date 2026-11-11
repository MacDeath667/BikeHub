// A deliberately small text format: no HTML, remote images or executable links.
export const maximumPostLength = 20000;
const maximumNesting = 16;
const imageSource = String.raw`!\[([^\]\n]{0,120})\]\(media:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\)`;
const markers = { "**": "bold", "*": "italic", "__": "underline", "~~": "strike", "||": "spoiler" } as const;
export type TextFormat = keyof typeof markers;
export type PostTextNode =
  | { kind: "text"; text: string }
  | { kind: "image"; id: string; alt: string }
  | { kind: "bold" | "italic" | "underline" | "strike" | "spoiler"; children: PostTextNode[] };

export function parsePostText(content: string): PostTextNode[] {
  const root: PostTextNode[] = [];
  const stack: { marker: TextFormat; children: PostTextNode[] }[] = [];
  const children = () => stack.at(-1)?.children ?? root;
  const appendText = (text: string) => {
    if (!text) { return; }
    const target = children(), last = target.at(-1);
    if (last?.kind === "text") { last.text += text; }
    else { target.push({ kind: "text", text }); }
  };
  function handleMarker(marker: TextFormat) {
    if (stack.at(-1)?.marker === marker) {
      const frame = stack.pop()!;
      if (frame.children.length) { children().push({ kind: markers[marker], children: frame.children }); }
      else { appendText(marker + marker); }
    } else if (stack.length >= maximumNesting || stack.some(frame => frame.marker === marker)) {
      appendText(marker);
    } else { stack.push({ marker, children: [] }); }
  }
  const tokens = new RegExp(String.raw`\\[\\*~_|!\[\]()]|${imageSource}|\*+|__|~~|\|\|`, "gi");
  let position = 0;
  for (const match of content.matchAll(tokens)) {
    appendText(content.slice(position, match.index));
    const token = match[0];
    position = match.index + token.length;
    if (token.startsWith("\\")) { appendText(token.slice(1)); }
    else if (match[2]) { children().push({ kind: "image", id: match[2].toLowerCase(), alt: match[1] || "Фотография к записи" }); }
    else if (token.startsWith("*")) {
      // Adjacent bold/italic delimiters (***text***) close from the inside out.
      let remaining = token.length;
      while (remaining) {
        const current = stack.at(-1)?.marker;
        const marker = current?.startsWith("*") && current.length <= remaining ? current : remaining >= 2 ? "**" : "*";
        handleMarker(marker); remaining -= marker.length;
      }
    } else { handleMarker(token as TextFormat); }
  }
  appendText(content.slice(position));
  // Unclosed formatting stays readable; it never swallows the remainder of a post.
  while (stack.length) {
    const frame = stack.pop()!;
    appendText(frame.marker);
    children().push(...frame.children);
  }
  return root;
}

export function inlinePhotoIds(content: string, photos: readonly { id: string }[]): Set<string> {
  const available = new Set(photos.map(photo => photo.id.toLowerCase()));
  const used = new Set<string>();
  function visit(nodes: PostTextNode[]) {
    for (const node of nodes) {
      if (node.kind === "image" && available.has(node.id)) { used.add(node.id); }
      else if ("children" in node) { visit(node.children); }
    }
  }
  visit(parsePostText(content));
  return used;
}

export function galleryPhotos<T extends { id: string }>(content: string, photos: readonly T[] = []): T[] {
  const used = inlinePhotoIds(content, photos);
  return photos.filter(photo => !used.has(photo.id.toLowerCase()));
}

export function postExcerpt(content: string, maximum = 280): string {
  function text(nodes: PostTextNode[]): string {
    return nodes.map(node => node.kind === "text" ? node.text : node.kind === "image" ? "\n"
      : node.kind === "spoiler" ? "[Спойлер]" : text(node.children)).join("");
  }
  const plain = text(parsePostText(content)).replace(/\s+/g, " ").trim();
  return plain.length > maximum ? `${plain.slice(0, maximum).trimEnd()}…` : plain;
}

export function removePhotoReferences(content: string, ids: readonly string[]): string {
  const removed = new Set(ids.map(id => id.toLowerCase()));
  // Respect escapes exactly as the renderer does.
  return content.replace(new RegExp(String.raw`\\[\\*~_|!\[\]()]|${imageSource}`, "gi"), (token, _alt: string, id?: string) =>
    id && removed.has(id.toLowerCase()) ? "" : token);
}

export function formatSelection(content: string, start: number, end: number, marker: TextFormat) {
  const selected = content.slice(start, end) || "текст";
  const replacement = marker + selected + marker;
  return { content: content.slice(0, start) + replacement + content.slice(end), start: start + marker.length, end: start + marker.length + selected.length };
}

export function insertPhoto(content: string, start: number, end: number, id: string) {
  const before = content.slice(0, start), after = content.slice(end);
  const prefix = before && !before.endsWith("\n\n") ? (before.endsWith("\n") ? "\n" : "\n\n") : "";
  const suffix = after.startsWith("\n\n") ? "" : after.startsWith("\n") ? "\n" : "\n\n";
  const addition = `${prefix}![Фотография](media:${id})${suffix}`;
  return { content: before + addition + after, start: start + addition.length, end: start + addition.length };
}
