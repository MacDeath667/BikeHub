import { useEffect, useState } from "react";
import type { Community } from "../api";

export function CommunityAvatar({ community, large = false }: { community: Community; large?: boolean }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [community.imageId, community.imageUrl]);
  const words = community.name.trim().split(/\s+/u).filter(Boolean);
  const initials = (words.length > 1 ? words.slice(0, 2).map(word => Array.from(word)[0]) : Array.from(words[0] || "?").slice(0, 2)).join("").toLocaleUpperCase("ru-RU");
  return <span className={`community-avatar${large ? " large" : ""}`} aria-hidden="true">
    {community.imageId && community.imageUrl && !failed ? <img src={community.imageUrl} alt="" onError={() => setFailed(true)} /> : initials}
  </span>;
}
