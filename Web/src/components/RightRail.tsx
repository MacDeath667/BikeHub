import {
  ArrowRight,
  ArrowUpRight,
  Bike as BikeIcon,
  ChevronRight,
  Plus,
  Sparkles,
} from "lucide-react";
import { categories, number } from "../api";
import type { Community, Overview, User } from "../api";
import { Avatar } from "./ui";

export function RightRail({
  overview,
  communities,
  me,
  onCreate,
}: {
  overview: Overview | null;
  communities: Community[];
  me: User;
  onCreate: () => void;
}) {
  return (
    <aside className="right-rail">
      <section className="garage-promo">
        <div className="eyebrow">
          <span /> ВАША ИСТОРИЯ НАЧИНАЕТСЯ ЗДЕСЬ
        </div>
        <div className="garage-illustration">
          <BikeIcon size={100} strokeWidth={1} />
          <div className="illustration-orbit" />
        </div>
        <h2>
          Больше, чем
          <br />
          просто велосипед
        </h2>
        <p>
          Сохраните каждый апгрейд,
          <br />
          километр и маленькую победу.
        </p>
        <button className="button dark" onClick={onCreate}>
          <Plus size={17} /> Добавить велосипед
        </button>
        <a href="#/garage">
          Перейти в мой гараж <ArrowRight size={14} />
        </a>
      </section>
      {overview && (
        <section className="rail-section featured">
          <div className="rail-title">
            <h3>Велосипед недели</h3>
            <span className="tiny-badge">
              <Sparkles size={12} />
            </span>
          </div>
          <a
            href={`#/bike/${overview.featuredBike.id}`}
            className="featured-photo"
          >
            <img
              src={overview.featuredBike.imageUrl}
              alt={overview.featuredBike.name}
            />
            <span>ВЫБОР СООБЩЕСТВА</span>
          </a>
          <a
            className="featured-name"
            href={`#/bike/${overview.featuredBike.id}`}
          >
            {overview.featuredBike.name}
            <ArrowUpRight size={16} />
          </a>
          <div className="featured-owner">
            <Avatar user={me} small />
            <span>@{me.handle}</span>
            <span className="dot-separator">·</span>
            <span>{categories[overview.featuredBike.category]}</span>
          </div>
        </section>
      )}
      <section className="rail-section">
        <div className="rail-title">
          <h3>Найдите своих</h3>
          <a href="#/communities" aria-label="Все сообщества">
            <ArrowUpRight size={17} />
          </a>
        </div>
        {communities.slice(0, 3).map((c) => (
          <a className="mini-community" href="#/communities" key={c.id}>
            <img src={c.imageUrl} alt="" />
            <span>
              <strong>{c.name}</strong>
              <small>{number(c.members)} участников</small>
            </span>
            <ChevronRight size={15} />
          </a>
        ))}
      </section>
      {overview && (
        <section className="rail-section tag-section">
          <h3>Сейчас обсуждают</h3>
          <div className="tags">
            {overview.tags.map((tag) => (
              <a key={tag} href={`#/?q=${encodeURIComponent(tag)}`}>
                #{tag}
              </a>
            ))}
          </div>
        </section>
      )}
      <div className="rail-note">
        <span className="online-dot" />
        Место, где понимают
        <br />
        <span>любовь к двум колёсам.</span>
      </div>
    </aside>
  );
}
