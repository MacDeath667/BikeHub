import type { ReactNode } from "react";
import { ArrowRight, Bike as BikeIcon } from "lucide-react";
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
      {user.initials}
    </span>
  );
}
export function Empty({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="empty">
      <BikeIcon size={38} strokeWidth={1.4} />
      <h3>{title}</h3>
      <p>{description}</p>
      {action}
    </div>
  );
}
export function Loading() {
  return (
    <div className="loading" role="status">
      <span className="spinner" />
      Загружаем истории…
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
