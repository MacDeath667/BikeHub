import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { DraftSync, draftKey, materializeDraft, readDraftCache, wireDraft, writeDraftCache } from "./draftState";
import type { DraftCache, DraftResponse, DraftValue } from "./draftState";

const autosaveDelay = 2000;
const serialize = (value: DraftValue) => JSON.stringify(wireDraft(value));
const photoUrl = (id: string) => new URL(`api/media/${id}`, new URL(".", window.location.href)).href;

export function useDraft(owner: string, scope: string, initial: DraftValue, enabled = true) {
  const base = useRef(initial), [value, setValue] = useState(initial), latest = useRef(value); latest.current = value;
  const [ready, setReady] = useState(!enabled), [restored, setRestored] = useState(false), [error, setError] = useState("");
  const [saving, setSaving] = useState(false), [actionBusy, setActionBusy] = useState(false), [savedAt, setSavedAt] = useState<string | null>(null), [storageError, setStorageError] = useState(false);
  const mounted = useRef(false), timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const key = draftKey(owner, scope), path = `/drafts/${encodeURIComponent(scope)}`;
  const syncRef = useRef<DraftSync | null>(null);
  if (!syncRef.current) {
    syncRef.current = new DraftSync((revision, state) => api<DraftResponse>(path, { method: "PUT", body: JSON.stringify({ expectedRevision: revision, state }) }),
      (response, snapshot) => {
        if (mounted.current) { setSavedAt(response.updatedAt); setError(""); if (serialize(latest.current) === snapshot) { cache(latest.current, false); } }
      });
  }
  const sync = syncRef.current;
  function cache(state: DraftValue | null, pending: boolean) {
    try {
      const data: DraftCache = { version: 1, modifiedAt: Date.now(), revision: sync.revision, pending, state: state ? wireDraft(state) : null };
      const saved = writeDraftCache(window.localStorage, key, data);
      if (mounted.current) { setStorageError(!saved); } return saved;
    } catch { if (mounted.current) { setStorageError(true); } return false; }
  }
  useEffect(() => {
    mounted.current = true; sync.stopped = false;
    if (!enabled) { return () => { mounted.current = false; }; }
    let active = true, local: DraftCache | null = null;
    try { local = readDraftCache(window.localStorage, key); } catch { setStorageError(true); }
    api<DraftResponse>(path).then(remote => {
      if (!active) { return; }
      const remoteValue = remote.state ? materializeDraft(remote.state, photoUrl) : base.current;
      sync.initialise(remote.revision, wireDraft(remoteValue)); setSavedAt(remote.updatedAt);
      const pending = local?.state && local.pending ? materializeDraft(local.state, photoUrl) : null;
      if (pending) {
        latest.current = pending; setValue(pending); setRestored(true);
        if (remote.revision > local!.revision && serialize(pending) !== serialize(remoteValue)) {
          sync.conflict = true; setError("Черновик изменён в другой вкладке. Здесь восстановлена ваша локальная версия.");
        }
      } else if (!(local?.state === null && local.revision >= remote.revision) && remote.state) {
        latest.current = remoteValue; setValue(remoteValue); setRestored(true);
      }
    }).catch(e => {
      if (!active) { return; }
      if (local?.state) { const recovered = materializeDraft(local.state, photoUrl); latest.current = recovered; setValue(recovered); setRestored(true); }
      setError(`Черновик пока не сохранён на сервере. ${(e as Error).message}`);
    }).finally(() => { if (active) { setReady(true); } });
    return () => { active = false; mounted.current = false; sync.stopped = true; clearTimeout(timer.current); };
  }, [owner, scope, enabled]);
  async function flush() {
    clearTimeout(timer.current);
    if (!enabled || sync.stopped) { return; }
    if (mounted.current) { setSaving(true); }
    try { await sync.flush(serialize(latest.current)); }
    catch (e) { if (mounted.current) { setError((e as Error).message); } throw e; }
    finally { if (mounted.current) { setSaving(false); } }
  }
  const snapshot = serialize(value), changed = snapshot !== serialize(base.current);
  useEffect(() => {
    if (!enabled || !ready || sync.stopped || !changed && !restored) { return; }
    cache(value, snapshot !== sync.acknowledged);
    if (sync.known && !sync.conflict && snapshot !== sync.acknowledged) { timer.current = setTimeout(() => { void flush().catch(() => {}); }, autosaveDelay); }
    return () => clearTimeout(timer.current);
  }, [snapshot, ready, enabled]);
  useEffect(() => {
    if (!enabled) { return; }
    const guard = (event: BeforeUnloadEvent) => {
      if (!sync.stopped && serialize(latest.current) !== sync.acknowledged) { event.preventDefault(); event.returnValue = ""; }
    };
    window.addEventListener("beforeunload", guard); return () => window.removeEventListener("beforeunload", guard);
  }, [enabled]);
  async function retry() {
    setActionBusy(true);
    try {
      const remote = await api<DraftResponse>(path), remoteValue = remote.state ? materializeDraft(remote.state, photoUrl) : base.current;
      if (!sync.conflict && remote.revision > sync.revision && serialize(remoteValue) !== serialize(latest.current)) {
        sync.conflict = true; setError("На сервере другая версия. Нажмите «Сохранить эту версию», чтобы заменить её вашим текстом."); return;
      }
      sync.initialise(remote.revision, wireDraft(remoteValue)); setError(""); setSavedAt(remote.updatedAt);
      await flush(); cache(latest.current, false);
    } catch (e) { setError((e as Error).message); }
    finally { setActionBusy(false); }
  }
  async function finish() {
    if (!enabled) { return; }
    clearTimeout(timer.current); await sync.stop();
    try {
      const cleared = await api<DraftResponse>(`${path}?revision=${sync.revision}`, { method: "DELETE" }); sync.revision = cleared.revision;
    } catch { /* The publication succeeded: never encourage a duplicate submit because cleanup failed. */ }
    cache(null, false);
  }
  async function discard() {
    if (!window.confirm("Удалить черновик и вернуться к исходной версии?")) { return; }
    setActionBusy(true); clearTimeout(timer.current); await sync.stop();
    try {
      const remote = await api<DraftResponse>(path);
      const cleared = await api<DraftResponse>(`${path}?revision=${remote.revision}`, { method: "DELETE" });
      sync.initialise(cleared.revision, wireDraft(base.current)); latest.current = base.current; setValue(base.current);
      setRestored(false); setError(""); setSavedAt(null); cache(null, false);
    } catch (e) { setError((e as Error).message); }
    finally { sync.stopped = false; setActionBusy(false); }
  }
  function confirmClose() {
    if (!enabled || sync.stopped || serialize(latest.current) === sync.acknowledged) { return true; }
    const local = cache(latest.current, true);
    return window.confirm(local ? "Изменения ещё не сохранены на сервере. Закрыть редактор? Текст сохранён на этом устройстве." : "Не удалось сохранить черновик. При закрытии изменения будут потеряны. Закрыть редактор?");
  }
  return { value, setValue, ready, restored, error, saving, savedAt, conflict: sync.conflict, storageError, actionBusy, flush, retry, finish, discard, confirmClose, changed };
}

export function clearLocalDrafts(owner: string) {
  try {
    const prefix = `bikehub.draft.v1:${encodeURIComponent(owner)}:`;
    for (const key of Object.keys(window.localStorage).filter(key => key.startsWith(prefix))) { window.localStorage.removeItem(key); }
  } catch { /* A blocked browser storage cannot prevent signing out. */ }
}
