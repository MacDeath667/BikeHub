import { categories } from "../api";

export function CategoryFilter({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="category-filter" aria-label="Тип велосипеда">
      <button
        className={!value ? "selected" : ""}
        aria-pressed={!value}
        onClick={() => onChange("")}
      >
        Все велосипеды
      </button>
      {Object.entries(categories).map(([key, name]) => (
        <button
          key={key}
          className={value === key ? "selected" : ""}
          aria-pressed={value === key}
          onClick={() => onChange(key)}
        >
          {name}
        </button>
      ))}
    </div>
  );
}
