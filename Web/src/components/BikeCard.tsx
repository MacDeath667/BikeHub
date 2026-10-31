import { ArrowUpRight, MapPin } from "lucide-react";
import { categories, number } from "../api";
import type { Bike } from "../api";
import { Avatar } from "./ui";

export function BikeCard({ bike }: { bike: Bike }) {
  return (
    <article className="bike-card">
      <a href={`#/bike/${bike.id}`} className="bike-card-image">
        <img src={bike.imageUrl} alt={bike.name} loading="lazy" />
        <span className="image-category">{categories[bike.category]}</span>
        <span className="bike-image-arrow">
          <ArrowUpRight size={21} />
        </span>
      </a>
      <div className="bike-card-content">
        <span className="eyebrow">
          {bike.brand} / {bike.year}
        </span>
        <a href={`#/bike/${bike.id}`}>
          <h2>{bike.name}</h2>
        </a>
        <p>{number(bike.mileage)} км своей истории</p>
        <a className="bike-owner" href={`#/user/${bike.ownerId}`}>
          <Avatar user={bike.owner} small />
          {bike.owner.name}
          <span>
            <MapPin size={12} />
            {bike.owner.city}
          </span>
        </a>
      </div>
    </article>
  );
}
