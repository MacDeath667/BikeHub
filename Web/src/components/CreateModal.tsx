import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { X } from "lucide-react";
import { api, categories, postCategories } from "../api";
import type { Bike, Page, Photo, Post, User } from "../api";
import { RouteEditor } from "./PostRoute";
import type { RouteData } from "../api";
import { PhotoCollection } from "./PhotoCollection";
import { PostEditor } from "./PostEditor";
import { useDraft } from "./useDraft";
import { DraftStatus } from "./DraftStatus";
import { Empty, ErrorBox, Loading } from "./ui";

export function CreateModal({
  type,
  me,
  close,
  onSuccess,
  initialBike,
  initialPost,
}: {
  type: "post" | "bike";
  initialBike?: Bike;
  initialPost?: Post;
  me: User;
  close: () => void;
  onSuccess: (id: string) => void;
}) {
  const initial = initialPost || initialBike;
  const selectedBike = window.location.hash.startsWith("#/bike/") ? window.location.hash.slice(7).split("?")[0] : "";
  const draft = useDraft(me.id, `post-${initialPost?.id || "new"}`, {
    title: initialPost?.title || "", content: initialPost?.content || "", category: initialPost?.category || "ride",
    bikeId: initialPost?.bike.id || selectedBike || null,
    photos: initial?.photos?.length ? initial.photos : initial?.imageId ? [{ id: initial.imageId, imageUrl: initial.imageUrl }] : [], route: initialPost?.route || null,
  }, type === "post");
  const { photos, content, route } = draft.value;
  const setPhotos = (photos: Photo[]) => draft.setValue(value => ({ ...value, photos }));
  const setContent = (content: string) => draft.setValue(value => ({ ...value, content }));
  const setRoute = (route: RouteData | null) => draft.setValue(value => ({ ...value, route }));
  const closeSafely = () => { if (draft.confirmClose()) { close(); } };
  const [routeBusy, setRouteBusy] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [bikes, setBikes] = useState<Bike[]>([]);
  const [loading, setLoading] = useState(type === "post");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const dialogRef = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = dialogRef.current;
    dialog?.showModal();
    return () => dialog?.close();
  }, []);
  useEffect(() => {
    if (type !== "post") return;
    let active = true;
    api<Page<Bike>>(`/users/${me.id}/bikes?pageSize=50`)
      .then((b) => {
        if (active) setBikes(b.items);
      })
      .catch((e) => {
        if (active) setError(e.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [type, me.id]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving || uploading || routeBusy || draft.actionBusy || !draft.ready) return;
    const fields = Object.fromEntries(new FormData(event.currentTarget));
    const values =
      type === "bike"
        ? {
            ...fields,
            year: Number(fields.year),
            mileage: Number(fields.mileage),
          }
        : fields;
    setSaving(true);
    setError("");
    try {
      await draft.flush().catch(() => {});
      const result = await api<{ id: string }>(
        (type === "post" ? "/posts" : "/bikes") + (initial ? `/${initial.id}` : ""),
        { method: initial ? "PUT" : "POST", body: JSON.stringify({ ...values, imageIds: photos.map(photo => photo.id), ...(type === "post" ? { route } : {}) }) },
      );
      await draft.finish();
      onSuccess(result.id);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }
  return (
    <dialog
      className="create-dialog"
      ref={dialogRef}
      onCancel={(e) => {
        e.preventDefault();
        if (!saving && !uploading && !routeBusy && !draft.actionBusy) closeSafely();
      }}
      aria-labelledby="dialog-title"
    >
      <div className="dialog-header">
        <h2 id="dialog-title">
          {initial ? type === "post" ? "Редактировать запись" : "Редактировать велосипед" : type === "post" ? "Новая запись" : "Новый велосипед"}
        </h2>
        <button
          type="button"
          className="icon-button"
          onClick={closeSafely}
          disabled={saving || uploading || routeBusy || draft.actionBusy}
          aria-label="Закрыть"
        >
          <X size={22} />
        </button>
      </div>
      {loading || !draft.ready ? (
        <Loading />
      ) : type === "post" && !bikes.length && error ? (
        <ErrorBox message={error} />
      ) : type === "post" && !bikes.length ? (
        <Empty
          title="Сначала добавьте велосипед"
          description="Каждая запись относится к велосипеду в вашем гараже."
          action={
            <a className="button primary" href="#/garage" onClick={close}>
              В мой гараж
            </a>
          }
        />
      ) : (
        <form onSubmit={submit} className="editor-form">
          <fieldset className="draft-fieldset" disabled={saving || draft.actionBusy}>
          {type === "post" && <DraftStatus draft={draft} disabled={saving || uploading || routeBusy || draft.actionBusy} />}
          {type === "bike" && <PhotoCollection photos={photos} onChange={setPhotos} onBusy={setUploading} disabled={saving || draft.actionBusy} />}
          {type === "post" ? (
            <>
              <label>
                Велосипед
                <select
                  name="bikeId"
                  required
                  value={bikes.some(b => b.id === draft.value.bikeId) ? draft.value.bikeId! : bikes[0]?.id}
                  onChange={e => draft.setValue(value => ({ ...value, bikeId: e.target.value }))}
                >
                  {bikes.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Заголовок
                <input
                  value={draft.value.title}
                  onChange={e => draft.setValue(value => ({ ...value, title: e.target.value }))}
                  name="title"
                  minLength={3}
                  maxLength={160}
                  required
                />
              </label>
              <label>
                Тип записи
                <select name="category" value={draft.value.category} onChange={e => draft.setValue(value => ({ ...value, category: e.target.value }))}>
                  {Object.entries(postCategories).map(([key, label]) => (
                    <option value={key} key={key}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
              <PostEditor value={content} onChange={setContent} photos={photos} onPhotosChange={setPhotos} onBusy={setUploading} disabled={saving || draft.actionBusy} minLength={10} />
              <RouteEditor value={route} onChange={setRoute} disabled={saving || uploading || draft.actionBusy} onBusy={setRouteBusy} />
              <span className="form-hint">
                Без фотографий обложкой станет карта маршрута или фотография велосипеда.
              </span>
            </>
          ) : (
            <>
              <label>
                Название велосипеда
                <input
                  defaultValue={initialBike?.name}
                  name="name"
                  placeholder="Например, Canyon Grizl CF SL 7"
                  required
                  minLength={2}
                  maxLength={100}
                />
              </label>
              <div className="form-row">
                <label>
                  Марка
                  <input
                    defaultValue={initialBike?.brand}
                    name="brand"
                    placeholder="Canyon"
                    required
                    maxLength={80}
                  />
                </label>
                <label>
                  Тип велосипеда
                  <select name="category" defaultValue={initialBike?.category || "gravel"}>
                    {Object.entries(categories).map(([key, label]) => (
                      <option key={key} value={key}>
                        {label}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <div className="form-row">
                <label>
                  Год выпуска
                  <input
                    name="year"
                    type="number"
                    min={1950}
                    max={2100}
                    defaultValue={initialBike?.year || new Date().getFullYear()}
                    required
                  />
                </label>
                <label>
                  Пробег, км
                  <input
                    name="mileage"
                    type="number"
                    min={0}
                    max={1000000}
                    step="0.1"
                    defaultValue={initialBike?.mileage || 0}
                    required
                  />
                </label>
              </div>
              <label>
                Немного о нём
                <textarea
                  name="description"
                  rows={4}
                  maxLength={2000}
                  defaultValue={initialBike?.description || ""}
                />
              </label>
            </>
          )}
          {error && <ErrorBox message={error} />}
          <div className="dialog-footer">
            <button
              type="button"
              className="button secondary"
              onClick={closeSafely}
              disabled={saving || uploading || routeBusy}
            >
              Отмена
            </button>
            <button className="button primary" disabled={saving || uploading || routeBusy}>
              {saving
                ? "Сохраняем…"
                : initial ? "Сохранить" : type === "post"
                  ? "Опубликовать запись"
                  : "Добавить велосипед"}
            </button>
          </div>
          </fieldset>
        </form>
      )}
    </dialog>
  );
}
