import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { ArrowUpRight, Bike as BikeIcon, MessageCircle, X } from "lucide-react";
import { api, categories, postCategories } from "../api";
import type { Bike, Page, Photo, Post, User } from "../api";
import { RouteEditor } from "./PostRoute";
import type { RouteData } from "../api";
import { PhotoCollection } from "./PhotoCollection";
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
  const [photos, setPhotos] = useState<Photo[]>(initial?.photos?.length ? initial.photos : initial?.imageId ? [{ id: initial.imageId, imageUrl: initial.imageUrl }] : []);
  const [route, setRoute] = useState<RouteData | null>(initialPost?.route || null);
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
    if (saving || uploading || routeBusy) return;
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
      const result = await api<{ id: string }>(
        (type === "post" ? "/posts" : "/bikes") + (initial ? `/${initial.id}` : ""),
        { method: initial ? "PUT" : "POST", body: JSON.stringify({ ...values, imageIds: photos.map(photo => photo.id), ...(type === "post" ? { route } : {}) }) },
      );
      onSuccess(result.id);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }
  const selectedBike = window.location.hash.startsWith("#/bike/")
    ? window.location.hash.slice(7).split("?")[0]
    : "";
  return (
    <dialog
      className="create-dialog"
      ref={dialogRef}
      onCancel={(e) => {
        e.preventDefault();
        if (!saving && !uploading && !routeBusy) close();
      }}
      onClick={(e) => {
        if (e.target !== dialogRef.current || saving || uploading || routeBusy) return;
        const bounds = e.currentTarget.getBoundingClientRect();
        if (
          e.clientX < bounds.left ||
          e.clientX > bounds.right ||
          e.clientY < bounds.top ||
          e.clientY > bounds.bottom
        )
          close();
      }}
      aria-labelledby="dialog-title"
    >
      <div className="dialog-header">
        <span className="dialog-icon">
          {type === "post" ? (
            <MessageCircle size={24} />
          ) : (
            <BikeIcon size={26} />
          )}
        </span>
        <button
          type="button"
          className="icon-button"
          onClick={close}
          disabled={saving || uploading || routeBusy}
          aria-label="Закрыть"
        >
          <X size={22} />
        </button>
      </div>
      <div className="eyebrow">ЕЩЁ ОДНА ХОРОШАЯ ИСТОРИЯ</div>
      <h2 id="dialog-title">
        {initial ? "Редактирование" : type === "post" ? "Поделитесь своим опытом" : "Пополнение в гараже"}
      </h2>
      <p className="dialog-description">
        {type === "post"
          ? "Поездка, полезное открытие или важное обновление — всё начинается с записи."
          : "Расскажите о велосипеде, который делает ваши дни интереснее."}
      </p>
      {loading ? (
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
          <PhotoCollection photos={photos} onChange={setPhotos} onBusy={setUploading} disabled={saving} />
          {type === "post" ? (
            <>
              <label>
                Велосипед
                <select
                  name="bikeId"
                  required
                  defaultValue={
                    initialPost?.bike.id || (bikes.some((b) => b.id === selectedBike) ? selectedBike : bikes[0]?.id)
                  }
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
                  defaultValue={initialPost?.title}
                  name="title"
                  placeholder="С чего началась ваша история?"
                  minLength={3}
                  maxLength={160}
                  required
                />
              </label>
              <label>
                Тип записи
                <select name="category" defaultValue={initialPost?.category || "ride"}>
                  {Object.entries(postCategories).map(([key, label]) => (
                    <option value={key} key={key}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Ваша история
                <textarea
                  defaultValue={initialPost?.content}
                  name="content"
                  rows={7}
                  minLength={10}
                  maxLength={20000}
                  placeholder="Поделитесь деталями. Именно они делают опыт полезным."
                  required
                />
              </label>
              <RouteEditor value={route} onChange={setRoute} disabled={saving || uploading} onBusy={setRouteBusy} />
              <span className="form-hint">
                Без своих фотографий запись возьмёт обложку велосипеда.
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
                  placeholder="Почему именно он? Что уже поменяли и куда собираетесь?"
                  rows={4}
                  maxLength={2000}
                  defaultValue={initialBike?.description || ""}
                />
              </label>
              <span className="form-hint">
                Расскажите о велосипеде и добавьте фотографии ниже.
              </span>
            </>
          )}
          {error && <ErrorBox message={error} />}
          <div className="dialog-footer">
            <button
              type="button"
              className="button secondary"
              onClick={close}
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
              <ArrowUpRight size={17} />
            </button>
          </div>
        </form>
      )}
    </dialog>
  );
}
