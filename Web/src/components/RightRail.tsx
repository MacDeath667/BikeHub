import { useState } from "react";
import { CommunityMembers } from "./CommunityMembers";
import { CommunityAvatar } from "./CommunityAvatar";
import {
  ArrowRight,
  ArrowUpRight,
  ChevronRight,
} from "lucide-react";
import { categories, number } from "../api";
import type { Community, Overview } from "../api";
import { Avatar, ErrorBox } from "./ui";

export function RightRail({
  overview,
  communities,
  onCreate,
  error,
  retry,
}: {
  overview: Overview | null;
  communities: Community[];
  onCreate: () => void;
  error?: string;
  retry?: () => void;
}) {
  const [membersOf, setMembersOf] = useState<Community | null>(null);
  return (
    <aside className="right-rail">
      {error && <ErrorBox message={error} retry={retry} />}
      {membersOf && <CommunityMembers community={membersOf} close={() => setMembersOf(null)} />}
      <section className="garage-promo" aria-label="Гараж">
        <button className="button dark" onClick={onCreate}>
          Добавить велосипед
        </button>
        <a href="#/garage">
          Перейти в мой гараж <ArrowRight size={14} />
        </a>
      </section>
      {overview?.featuredBike && (
        <section className="rail-section featured">
          <div className="rail-title">
            <h3>Новый в гараже</h3>
          </div>
          <a
            href={`#/bike/${overview.featuredBike.id}`}
            className="featured-photo"
          >
            <img
              src={overview.featuredBike.imageUrl}
              alt={overview.featuredBike.name}
            />
          </a>
          <a
            className="featured-name"
            href={`#/bike/${overview.featuredBike.id}`}
          >
            {overview.featuredBike.name}
            <ArrowUpRight size={16} />
          </a>
          <div className="featured-owner">
            <Avatar user={overview.featuredBike.owner} small />
            <span>{overview.featuredBike.owner.handle ? `@${overview.featuredBike.owner.handle}` : overview.featuredBike.owner.name}</span>
            <span className="dot-separator">·</span>
            <span>{categories[overview.featuredBike.category]}</span>
          </div>
        </section>
      )}
      <section className="rail-section">
        <div className="rail-title">
          <h3>Сообщества</h3>
          <a href="#/communities" aria-label="Все сообщества">
            <ArrowUpRight size={17} />
          </a>
        </div>
        {communities.slice(0, 3).map((c) => (
          <div className="mini-community" key={c.id}>
            <a href={`#/community/${c.id}`} aria-label={`Открыть сообщество ${c.name}`}><CommunityAvatar community={c} /></a>
            <span>
              <a href={`#/community/${c.id}`}><strong>{c.name}</strong></a>
              <button type="button" className="community-members-button" onClick={() => setMembersOf(c)} aria-label={`Участники сообщества ${c.name}: ${number(c.members)}`}>Участники: {number(c.members)}</button>
            </span>
            <a className="mini-community-arrow" href={`#/community/${c.id}`} aria-label={`Открыть сообщество ${c.name}`}><ChevronRight size={15} /></a>
          </div>
        ))}
      </section>
      {!!overview?.tags.length && (
        <section className="rail-section tag-section">
          <h3>Темы записей</h3>
          <div className="tags">
            {overview.tags.map((tag) => (
              <a key={tag} href={`#/?q=${encodeURIComponent(tag)}`}>
                #{tag}
              </a>
            ))}
          </div>
        </section>
      )}
    </aside>
  );
}
