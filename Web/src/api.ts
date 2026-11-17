export interface User {
  id: string;
  name: string;
  handle: string;
  city: string;
  initials: string;
  color: string;
  bio: string;
  followers: number;
  isFollowing: boolean;
  bikesCount: number;
  postsCount: number;
  avatarUrl?: string | null;
  isModerator?: boolean;
}
export interface AuthSession {
  user: User | null;
  emailConfirmed: boolean;
  maskedEmail: string | null;
  phoneConfirmed: boolean;
  csrfToken: string;
  enabled: boolean;
  developmentMailbox: boolean;
}

const siteBase = new URL(".", window.location.href);

// Public assets returned by the API are relative to BikeHub, including behind a path proxy.
function resolveImages(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(resolveImages);
  if (value !== null && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key,
      (key === "imageUrl" || key === "avatarUrl") && typeof item === "string" && item.startsWith("/") && !item.startsWith("//")
        ? new URL(item.slice(1), siteBase).href : resolveImages(item),
    ]));
  }
  return value;
}

let csrfToken = "";
let csrfRequest: Promise<void> | null = null;
export function requestLogin() {
  window.dispatchEvent(new Event("bikehub:auth-required"));
}
export interface Photo { id: string; imageUrl: string; }
export interface Bike {
  id: string;
  ownerId: string;
  name: string;
  description: string;
  brand: string;
  category: string;
  year: number;
  mileage: number;
  imageUrl: string;
  imageId?: string | null;
  photos?: Photo[];
  imageIds?: string[];
  color: string;
  owner: User;
}
export interface GeoPoint { lat: number; lng: number; }
export interface RouteData { name: string; segments: GeoPoint[][]; markers: (GeoPoint & { label: string })[]; }
export interface GroupEntry {
  hasRoute?: boolean;
  route?: RouteData | null;
  id: string;
  groupId: string;
  groupName: string;
  author: User;
  kind: number;
  title: string;
  content: string;
  photos: Photo[];
  imageIds: string[];
  createdAt: string;
  repliesCount: number;
}
export interface Post {
  hasRoute?: boolean;
  route?: RouteData | null;
  id: string;
  author: User;
  bike: Bike;
  title: string;
  content: string;
  category: string;
  imageUrl: string;
  imageId?: string | null;
  photos?: Photo[];
  imageIds?: string[];
  tags: string[];
  createdAt: string;
  likes: number;
  commentsCount: number;
  isLiked: boolean;
  isSaved: boolean;
}
export interface Comment {
  id: string;
  author: User;
  text: string;
  createdAt: string;
  replyToId?: string | null;
  replyTo?: { id: string; authorName: string; authorHandle: string; text: string } | null;
}
export type FeedItem =
  | { id: string; kind: 1; createdAt: string; post: Post; groupEntry: null }
  | { id: string; kind: 2; createdAt: string; post: null; groupEntry: GroupEntry };
export interface Ride {
  id: string; organizer: User; groupId: string | null; groupName: string | null;
  title: string; description: string; city: string; meetingName: string; meetingPoint: GeoPoint | null;
  startsAt: string; timeZone: string; distanceKm: number; averageSpeedKmh: number | null;
  difficulty: 1 | 2 | 3; capacity: number | null; status: 1 | 2; revision: number;
  going: number; maybe: number; attendance: 1 | 2 | null; hasRoute: boolean; route: RouteData | null;
}
export interface LibraryRoute {
  id: string; author: User; title: string; description: string; city: string; surface: 1 | 2 | 3 | 4;
  distanceKm: number; revision: number; createdAt: string; reviewsCount: number; rating: number | null;
  isSaved: boolean; collectionId: string | null; myNote: string | null; route: RouteData | null;
}
export interface RouteCollection { id: string; name: string; routesCount: number; }
export interface RouteReview { id: string; author: User; rating: number; text: string; riddenOn: string; updatedAt: string; }
export interface MaintenanceOverview { bikeId: string; bikeName: string; mileage: number; activeComponents: number; dueComponents: number; }
export interface BikeComponent {
  id: string; bikeId: string; kind: number; name: string; model: string; notes: string;
  installedMileage: number; installedOn: string; serviceIntervalKm: number | null; lastServiceMileage: number;
  dueMileage: number | null; currentMileage: number; isArchived: boolean; revision: number; isDue: boolean;
}
export interface MaintenanceRecord {
  id: string; componentId: string | null; componentName: string | null; kind: number; title: string; notes: string;
  performedOn: string; mileage: number; costRub: number | null; resetsInterval: boolean; revision: number;
}
export interface Community {
  ownerId?: string | null;
  id: string;
  name: string;
  description: string;
  category: string;
  imageUrl: string;
  imageId?: string | null;
  photos?: Photo[];
  imageIds?: string[];
  members: number;
  isJoined: boolean;
}
export interface Page<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
  hasMore: boolean;
}
export interface Overview {
  riders: number;
  bikes: number;
  posts: number;
  tags: string[];
  featuredBike: Bike | null;
}
export const categories: Record<string, string> = {
  road: "Шоссе",
  mtb: "MTB",
  gravel: "Гравий",
  city: "Город",
};
export const postCategories: Record<string, string> = {
  ride: "Поездка",
  maintenance: "Обслуживание",
  upgrade: "Апгрейд",
  story: "Личный опыт",
};
export const number = (value: number) =>
  new Intl.NumberFormat("ru-RU").format(value);
export function relativeTime(value: string) {
  const hours = Math.max(
    0,
    Math.floor((Date.now() - new Date(value).getTime()) / 3600000),
  );
  if (hours < 1) return "только что";
  if (hours < 24) return `${hours} ч. назад`;
  return new Date(value).toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "long",
  });
}
export class ApiError extends Error {
  readonly status: number;
  constructor(status: number, message: string) { super(message); this.status = status; }
}
export async function api<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const method = (options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers);
  if (!(options.body instanceof FormData)) headers.set("Content-Type", "application/json");
  if (!["GET", "HEAD", "OPTIONS"].includes(method)) {
    if (!csrfToken) {
      csrfRequest ??= api<AuthSession>("/auth/session")
        .then((session) => { csrfToken = session.csrfToken; })
        .finally(() => { csrfRequest = null; });
      await csrfRequest;
    }
    headers.set("X-CSRF-TOKEN", csrfToken);
  }
  const controller = new AbortController();
  let timedOut = false;
  const cancel = () => controller.abort(options.signal?.reason);
  if (options.signal?.aborted) cancel();
  else options.signal?.addEventListener("abort", cancel, { once: true });
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, options.body instanceof FormData ? 120000 : 60000);
  try {
    let response: Response;
    try {
      response = await fetch(new URL(`api${path}`, siteBase), {
        ...options,
        credentials: "same-origin",
        headers,
        signal: controller.signal,
      });
    } catch (error) {
      if (controller.signal.aborted) throw error;
      throw new Error(["GET", "HEAD", "OPTIONS"].includes(method)
        ? "Не удалось связаться с BikeHub. Проверьте подключение и попробуйте снова."
        : "Не удалось получить ответ BikeHub. Проверьте подключение и результат действия перед повтором.");
    }
    if (!response.ok) {
      if (response.status === 401 && !path.startsWith("/auth/")) {
        csrfToken = "";
        requestLogin();
      }
      const problem = await response.json().catch(error => { if (controller.signal.aborted) throw error; return null; });
      const validation = problem?.errors
        ? Object.values(problem.errors).flat().join(" ")
        : null;
      throw new ApiError(response.status,
        response.status === 401
          ? "Войдите в аккаунт, чтобы продолжить."
          : response.status === 429
          ? problem?.title || "Слишком много запросов. Попробуйте позже."
          : response.status === 404
          ? problem?.title || "Эта страница не найдена."
          : validation ||
            problem?.title ||
            "Не удалось выполнить действие. Попробуйте ещё раз.",
      );
    }
    if (response.status === 204) return undefined as T;
    const result = await response.json();
    if (path.startsWith("/auth/") && typeof result?.csrfToken === "string") csrfToken = result.csrfToken;
    return resolveImages(result) as T;
  } catch (error) {
    if (timedOut && !options.signal?.aborted) {
      throw new Error(["GET", "HEAD", "OPTIONS"].includes(method)
        ? "BikeHub не ответил вовремя. Проверьте подключение и попробуйте загрузить данные ещё раз."
        : "Ответ BikeHub не получен вовремя. Действие могло выполниться — проверьте результат перед повтором.");
    }
    throw error;
  } finally {
    clearTimeout(timer);
    options.signal?.removeEventListener("abort", cancel);
  }
}
export function query(values: Record<string, string | number | undefined>) {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== "") params.set(key, String(value));
  });
  return params.toString();
}

export const userHref = (user: User) => user.handle ? `#/u/${encodeURIComponent(user.handle)}` : `#/user/${user.id}`;
