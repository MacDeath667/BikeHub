export function mentionAtCursor(value: string, cursor: number) {
  const before = value.slice(0, cursor);
  const match = /(?<![\p{L}\p{N}_@])@([a-z0-9_]{0,32})$/iu.exec(before);
  if (!match) { return null; }
  const suffix = /^[a-z0-9_]*/i.exec(value.slice(cursor))![0];
  if (match[1].length + suffix.length > 32) { return null; }
  return { start: match.index, end: cursor + suffix.length, query: match[1].toLowerCase() };
}

export function completeMention(value: string, range: { start: number; end: number }, nickname: string) {
  const after = value.slice(range.end);
  const insertion = `@${nickname}${after && /^[\s.,!?;:)\]]/.test(after) ? "" : " "}`;
  return { value: value.slice(0, range.start) + insertion + after, cursor: range.start + insertion.length };
}

export function mentionParts(value: string) {
  const parts: { text: string; nickname?: string }[] = [];
  let position = 0;
  for (const match of value.matchAll(/(?<![\p{L}\p{N}_@])@([a-z0-9_]{3,32})(?![a-z0-9_])/giu)) {
    if (match.index > position) { parts.push({ text: value.slice(position, match.index) }); }
    parts.push({ text: match[0], nickname: match[1].toLowerCase() }); position = match.index + match[0].length;
  }
  if (position < value.length) { parts.push({ text: value.slice(position) }); }
  return parts;
}
