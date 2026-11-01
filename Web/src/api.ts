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

let csrfToken = "";
let csrfRequest: Promise<void> | null = null;
export function requestLogin() {
  window.dispatchEvent(new Event("bikehub:auth-required"));
}
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
  color: string;
  owner: User;
}
export interface Post {
  id: string;
  author: User;
  bike: Bike;
  title: string;
  content: string;
  category: string;
  imageUrl: string;
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
}
export interface Community {
  id: string;
  name: string;
  description: string;
  category: string;
  imageUrl: string;
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
  featuredBike: Bike;
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
export async function api<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const method = (options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers);
  headers.set("Content-Type", "application/json");
  if (!["GET", "HEAD", "OPTIONS"].includes(method)) {
    if (!csrfToken) {
      csrfRequest ??= api<AuthSession>("/auth/session")
        .then((session) => { csrfToken = session.csrfToken; })
        .finally(() => { csrfRequest = null; });
      await csrfRequest;
    }
    headers.set("X-CSRF-TOKEN", csrfToken);
  }
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      ...options,
      credentials: "same-origin",
      headers,
    });
  } catch {
    throw new Error(
      "Не удалось связаться с API. Проверьте, что сервер запущен.",
    );
  }
  if (!response.ok) {
    if (response.status === 401 && !path.startsWith("/auth/")) {
      csrfToken = "";
      requestLogin();
    }
    const problem = await response.json().catch(() => null);
    const validation = problem?.errors
      ? Object.values(problem.errors).flat().join(" ")
      : null;
    throw new Error(
      response.status === 401
        ? "Войдите в аккаунт, чтобы продолжить."
        : response.status === 429
        ? problem?.title || "Слишком много запросов. Попробуйте позже."
        : response.status === 404
        ? "Эта страница не найдена."
        : validation ||
          problem?.title ||
          "Не удалось выполнить действие. Попробуйте ещё раз.",
    );
  }
  if (response.status === 204) return undefined as T;
  const result = await response.json();
  if (path.startsWith("/auth/") && typeof result?.csrfToken === "string") csrfToken = result.csrfToken;
  return result as T;
}
export function query(values: Record<string, string | number | undefined>) {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== "") params.set(key, String(value));
  });
  return params.toString();
}
