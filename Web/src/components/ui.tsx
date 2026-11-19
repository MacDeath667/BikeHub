import type { ReactNode } from "react";
import { ArrowRight } from "lucide-react";
import type { User } from "../api";

export function Avatar({
  user,
  small = false,
}: {
  user: User;
  small?: boolean;
}) {
  return (
    <span
      className={`avatar ${user.color} ${small ? "small" : ""}`}
      aria-hidden="true"
    >
      {user.avatarUrl ? <img src={user.avatarUrl} alt="" style={{ width: "100%", height: "100%", objectFit: "cover", borderRadius: "50%" }} /> : user.initials}
    </span>
  );
}
export function Empty({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="empty">
      <h3>{title}</h3>
      {description && <p>{description}</p>}
      {action}
    </div>
  );
}
export function Loading() {
  return (
    <div className="loading" role="status">
      <span className="spinner" />
      Загружаем…
    </div>
  );
}
export function ErrorBox({
  message,
  retry,
}: {
  message: string;
  retry?: () => void;
}) {
  return (
    <div className="error-box" role="alert">
      <p>{message}</p>
      {retry && (
        <button className="text-button" onClick={retry}>
          Попробовать снова <ArrowRight size={15} />
        </button>
      )}
    </div>
  );
}
