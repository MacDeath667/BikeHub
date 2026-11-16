import type { useDraft } from "./useDraft";

export function DraftStatus({ draft, disabled }: { draft: ReturnType<typeof useDraft>; disabled: boolean }) {
  return <section className="draft-status" aria-label="Черновик">
    <p role="status">{draft.saving ? "Сохраняем черновик…" : draft.restored ? "Восстановлен черновик" : draft.savedAt ? "Черновик сохранён" : "Черновик сохраняется автоматически"}{draft.savedAt && !draft.saving && <span> · {new Date(draft.savedAt).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}</span>}</p>
    {draft.error && <p role="alert">{draft.error} <button type="button" className="text-button" disabled={disabled || draft.saving} onClick={() => void draft.retry()}>{draft.conflict ? "Сохранить эту версию" : "Повторить сохранение"}</button></p>}
    {draft.storageError && <p role="alert">Не удалось сделать страховочную копию на этом устройстве. Дождитесь сохранения на сервере.</p>}
    {(draft.restored || draft.changed) && <button type="button" className="text-button" disabled={disabled || draft.saving} onClick={() => void draft.discard()}>Удалить черновик</button>}
  </section>;
}
