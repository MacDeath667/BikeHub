import type { Photo, RouteData } from "../api";

export type DraftValue = { title: string; content: string; category: string; bikeId: string | null; photos: Photo[]; route: RouteData | null };
export type DraftState = Omit<DraftValue, "photos"> & { imageIds: string[] };
export type DraftResponse = { revision: number; state: DraftState | null; photos: Photo[]; updatedAt: string | null };
export type DraftCache = { version: 1; modifiedAt: number; revision: number; pending: boolean; state: DraftState | null };
export type DraftStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;
const retentionMs = 30 * 24 * 60 * 60 * 1000;
const maximumCacheLength = 2500000;
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const draftKey = (owner: string, scope: string) => `bikehub.draft.v1:${encodeURIComponent(owner)}:${scope}`;
export const wireDraft = (value: DraftValue): DraftState => ({ title: value.title, content: value.content, category: value.category, bikeId: value.bikeId, imageIds: value.photos.map(photo => photo.id), route: value.route });
export function materializeDraft(state: DraftState, photoUrl: (id: string) => string): DraftValue {
  const { imageIds, ...value } = state;
  return { ...value, photos: imageIds.map(id => ({ id, imageUrl: photoUrl(id) })) };
}
export function readDraftCache(storage: DraftStorage, key: string, now = Date.now()): DraftCache | null {
  try {
    const raw = storage.getItem(key);
    if (!raw || raw.length > maximumCacheLength) { return null; }
    const cache = JSON.parse(raw) as DraftCache;
    if (cache.version !== 1 || !Number.isFinite(cache.modifiedAt) || now - cache.modifiedAt > retentionMs || !Number.isSafeInteger(cache.revision) || cache.revision < 0 || typeof cache.pending !== "boolean") { return null; }
    const state = cache.state;
    if (state === null) { return cache; }
    if (typeof state?.title !== "string" || state.title.length > 160 || typeof state.content !== "string" || state.content.length > 20000 ||
      typeof state.category !== "string" || state.category.length > 20 || state.bikeId !== null && (typeof state.bikeId !== "string" || !uuid.test(state.bikeId)) ||
      !Array.isArray(state.imageIds) || state.imageIds.length > 10 || state.imageIds.some(id => typeof id !== "string" || !uuid.test(id)) || new Set(state.imageIds).size !== state.imageIds.length) { return null; }
    if (state.route !== null) {
      const route = state.route;
      if (!route || typeof route.name !== "string" || route.name.length > 120 || !Array.isArray(route.segments) || !Array.isArray(route.markers) || route.segments.length > 100 || route.markers.length > 100) { return null; }
      let points = 0;
      for (const segment of route.segments) {
        if (!Array.isArray(segment) || segment.length < 2) { return null; }
        points += segment.length;
        if (points > 30000) { return null; }
        if (segment.some(p => !p || !Number.isFinite(p.lat) || !Number.isFinite(p.lng) || Math.abs(p.lat) > 90 || Math.abs(p.lng) > 180)) { return null; }
      }
      if (points + route.markers.length > 30000 || route.markers.some(p => !p || !Number.isFinite(p.lat) || !Number.isFinite(p.lng) || Math.abs(p.lat) > 90 || Math.abs(p.lng) > 180 || typeof p.label !== "string" || p.label.length > 120)) { return null; }
    }
    return cache;
  } catch { return null; }
}
export function writeDraftCache(storage: DraftStorage, key: string, cache: DraftCache): boolean {
  try {
    const raw = JSON.stringify(cache);
    if (raw.length > maximumCacheLength) { return false; }
    storage.setItem(key, raw); return true;
  } catch { return false; }
}

// A single writer drains the most recent snapshot. Overlapping autosaves cannot arrive out of order.
export class DraftSync {
  revision = 0;
  acknowledged = "";
  latest = "";
  known = false;
  conflict = false;
  stopped = false;
  private pending: Promise<void> | null = null;
  private readonly send: (revision: number, state: DraftState) => Promise<DraftResponse>;
  private readonly saved: (response: DraftResponse, snapshot: string) => void;
  constructor(send: (revision: number, state: DraftState) => Promise<DraftResponse>, saved: (response: DraftResponse, snapshot: string) => void) {
    this.send = send; this.saved = saved;
  }
  initialise(revision: number, state: DraftState | null) { this.revision = revision; this.acknowledged = state ? JSON.stringify(state) : ""; this.known = true; this.conflict = false; }
  flush(snapshot: string): Promise<void> {
    this.latest = snapshot;
    if (this.stopped || this.conflict || !this.known) { return Promise.reject(new Error("Черновик пока не синхронизирован с сервером.")); }
    if (!this.pending) { this.pending = this.drain().finally(() => { this.pending = null; }); }
    return this.pending.then(() => {
      if (!this.stopped && !this.conflict && this.latest !== this.acknowledged) { return this.flush(this.latest); }
    });
  }
  private async drain() {
    while (!this.stopped && this.latest !== this.acknowledged) {
      const snapshot = this.latest;
      try {
        const response = await this.send(this.revision, JSON.parse(snapshot));
        this.revision = response.revision; this.acknowledged = snapshot; this.saved(response, snapshot);
      } catch (error) {
        this.conflict = (error as { status?: number }).status === 409; throw error;
      }
    }
  }
  async stop() { this.stopped = true; await this.pending?.catch(() => {}); }
}
