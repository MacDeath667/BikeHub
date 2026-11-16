// Offset pages can overlap after a new item is inserted. Keep each item once.
export function appendUnique<T extends { id: string }>(previous: T[], next: T[]): T[] {
  return [...new Map([...previous, ...next].map(item => [item.id, item])).values()];
}
