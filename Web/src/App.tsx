import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import {
  ArrowUpRight,
  Bike as BikeIcon,
  Bookmark,
  Check,
  ChevronDown,
  ChevronRight,
  Compass,
  House,
  Menu,
  Mountain,
  Search,
  Users,
  X,
} from "lucide-react";
import { api } from "./api";
import type { Community, Overview, User } from "./api";
import { Avatar, Empty, ErrorBox, Loading } from "./components/ui";
import { RightRail } from "./components/RightRail";
import { CreateModal } from "./components/CreateModal";
import { Feed } from "./pages/Feed";
import { BikeDetail, BikeDirectory } from "./pages/Bikes";
import { PostDetail } from "./pages/PostDetail";
import { UserProfile } from "./pages/UserProfile";
import { Communities } from "./pages/Communities";

function navigate(path: string) {
  window.location.hash = path;
}
function useRoute() {
  const [route, setRoute] = useState(window.location.hash.slice(1) || "/");
  useEffect(() => {
    const change = () => {
      setRoute(window.location.hash.slice(1) || "/");
      window.scrollTo({ top: 0 });
    };
    window.addEventListener("hashchange", change);
    return () => window.removeEventListener("hashchange", change);
  }, []);
  return route;
}

export default function App() {
  const route = useRoute();
  const [me, setMe] = useState<User | null>(null);
  const [overview, setOverview] = useState<Overview | null>(null);
  const [communities, setCommunities] = useState<Community[]>([]);
  const [bootError, setBootError] = useState("");
  const [version, setVersion] = useState(0);
  const [modal, setModal] = useState<"post" | "bike" | null>(null);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [searchText, setSearchText] = useState("");
  const [toast, setToast] = useState("");
  const toastTimer = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined,
  );
  const notify = useCallback((message: string) => {
    setToast(message);
    clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(""), 4000);
  }, []);
  const reload = useCallback(() => setVersion((v) => v + 1), []);
  useEffect(() => {
    let active = true;
    Promise.all([
      api<User>("/users/me"),
      api<Overview>("/feed/overview"),
      api<Community[]>("/communities"),
    ])
      .then(([user, stats, groups]) => {
        if (active) {
          setMe(user);
          setOverview(stats);
          setCommunities(groups);
          setBootError("");
        }
      })
      .catch((e) => {
        if (active) setBootError(e.message);
      });
    return () => {
      active = false;
    };
  }, [version]);
  useEffect(() => {
    setMobileOpen(false);
  }, [route]);
  useEffect(() => () => clearTimeout(toastTimer.current), []);
  const [path, search = ""] = route.split("?");
  const searchQuery = new URLSearchParams(search).get("q") || "";
  const section = path.split("/")[1];
  const searchSubmit = (event: FormEvent) => {
    event.preventDefault();
    navigate(`/?q=${encodeURIComponent(searchText.trim())}`);
  };
  const nav = [
    {
      path: "/",
      icon: House,
      text: "Лента",
      active: !section || section === "post",
    },
    {
      path: "/bikes",
      icon: Compass,
      text: "Велосипеды",
      active: section === "bikes" || section === "bike",
    },
    {
      path: "/communities",
      icon: Users,
      text: "Сообщества",
      active: section === "communities",
    },
    {
      path: "/saved",
      icon: Bookmark,
      text: "Закладки",
      active: section === "saved",
    },
  ];

  return (
    <div className="app-shell">
      {mobileOpen && (
        <button
          className="sidebar-backdrop"
          aria-label="Закрыть меню"
          onClick={() => setMobileOpen(false)}
        />
      )}
      <aside className={`sidebar ${mobileOpen ? "open" : ""}`}>
        <a className="brand" href="#/">
          <span className="brand-mark">
            <BikeIcon size={26} />
          </span>
          bikehub<span className="brand-dot">.</span>
        </a>
        <div className="sidebar-label">МЕСТО ДЛЯ СВОИХ</div>
        <nav className="main-nav" aria-label="Основная навигация">
          {nav.map((item) => (
            <a
              key={item.path}
              href={`#${item.path}`}
              className={item.active ? "active" : ""}
              aria-current={item.active ? "page" : undefined}
            >
              <item.icon size={20} strokeWidth={1.7} />
              {item.text}
              {item.active && <span className="nav-dot" />}
            </a>
          ))}
        </nav>
        <div className="sidebar-divider" />
        <div className="sidebar-label garage-label">МОЁ ПРОСТРАНСТВО</div>
        <a
          href="#/garage"
          className={`garage-link ${section === "garage" ? "active" : ""}`}
        >
          <BikeIcon size={20} />
          Мой гараж<span>{me?.bikesCount ?? "—"}</span>
        </a>
        {me && (
          <a
            href={`#/user/${me.id}`}
            className={`garage-link ${section === "user" && path.endsWith(me.id) ? "active" : ""}`}
          >
            <span className="profile-dot" />
            Мой профиль
            <ChevronRight size={16} />
          </a>
        )}
        <div className="sidebar-invite">
          <span className="invite-icon">
            <Mountain size={25} strokeWidth={1.3} />
          </span>
          <h3>
            Дорога интереснее
            <br />в хорошей компании
          </h3>
          <p>
            Найдите тех, кто разделяет
            <br />
            ваш ритм.
          </p>
          <a href="#/communities">
            Найти сообщество <ArrowUpRight size={16} />
          </a>
        </div>
        <div className="sidebar-bottom">
          <span className="online-dot" /> Крутим педали вместе{" "}
          <span>© 2026 BikeHub</span>
        </div>
      </aside>

      <div className="workspace">
        <header className="topbar">
          <button
            className="icon-button menu-button"
            aria-label="Открыть меню"
            onClick={() => setMobileOpen(true)}
          >
            <Menu size={22} />
          </button>
          <div className="topbar-breadcrumb">
            Сообщество <ChevronRight size={14} />
            <span>
              {(
                {
                  bikes: "Велосипеды",
                  bike: "Бортжурнал",
                  communities: "Сообщества",
                  saved: "Закладки",
                  garage: "Мой гараж",
                  user: "Профиль",
                  post: "История",
                } as Record<string, string>
              )[section] || "Лента"}
            </span>
          </div>
          <form className="global-search" onSubmit={searchSubmit}>
            <Search size={18} />
            <input
              aria-label="Поиск по историям, велосипедам и людям"
              placeholder="Истории, велосипеды, люди…"
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
            />
            <kbd>↵</kbd>
          </form>
          <div className="topbar-right">
            <span className="demo-label">
              <span />
              Демо
            </span>
            {me && (
              <a
                className="user-menu"
                href={`#/user/${me.id}`}
                aria-label="Открыть мой профиль"
              >
                <Avatar user={me} small />
                <span>Александр</span>
                <ChevronDown size={14} />
              </a>
            )}
          </div>
        </header>
        <main className="page">
          {bootError ? (
            <ErrorBox message={bootError} retry={reload} />
          ) : !me ? (
            <Loading />
          ) : (
            <>
              <div className="content-grid">
                <div className="primary-content">
                  {(!section || section === "saved") && (
                    <Feed
                      key={`${section}-${searchQuery}-${version}`}
                      saved={section === "saved"}
                      search={searchQuery}
                      version={version}
                      me={me}
                      notify={notify}
                      onCreate={() => setModal("post")}
                    />
                  )}
                  {(section === "bikes" || section === "garage") && (
                    <BikeDirectory
                      key={`${section}-${version}`}
                      garage={section === "garage"}
                      me={me}
                      version={version}
                      onCreate={() => setModal("bike")}
                    />
                  )}
                  {section === "bike" && (
                    <BikeDetail
                      key={`${path}-${version}`}
                      id={path.split("/")[2]}
                      me={me}
                      version={version}
                      onCreate={() => setModal("post")}
                      notify={notify}
                    />
                  )}
                  {section === "post" && (
                    <PostDetail
                      key={path}
                      id={path.split("/")[2]}
                      me={me}
                      notify={notify}
                    />
                  )}
                  {section === "user" && (
                    <UserProfile
                      key={path}
                      id={path.split("/")[2]}
                      me={me}
                      notify={notify}
                      reload={reload}
                      version={version}
                    />
                  )}
                  {section === "communities" && (
                    <Communities
                      communities={communities}
                      notify={notify}
                      reload={reload}
                    />
                  )}
                  {section &&
                    ![
                      "saved",
                      "bikes",
                      "garage",
                      "bike",
                      "post",
                      "user",
                      "communities",
                    ].includes(section) && (
                      <Empty
                        title="Такой страницы нет"
                        description="Но впереди ещё много интересных историй."
                        action={
                          <a href="#/" className="button primary">
                            Вернуться в ленту
                          </a>
                        }
                      />
                    )}
                </div>
                <RightRail
                  overview={overview}
                  communities={communities}
                  me={me}
                  onCreate={() => setModal("bike")}
                />
              </div>
              <footer className="page-footer">
                <span>У каждого велосипеда своя история.</span>
                <span>
                  Сделано для тех, кто едет <BikeIcon size={15} />
                </span>
              </footer>
            </>
          )}
        </main>
      </div>
      {modal && me && (
        <CreateModal
          type={modal}
          me={me}
          close={() => setModal(null)}
          onSuccess={(id) => {
            const type = modal;
            setModal(null);
            reload();
            navigate(type === "post" ? `/post/${id}` : `/bike/${id}`);
            notify(
              type === "post"
                ? "Запись опубликована. История продолжается!"
                : "Велосипед добавлен в ваш гараж",
            );
          }}
        />
      )}
      {toast && (
        <div className="toast" role="status">
          <Check size={18} />
          {toast}
          <button aria-label="Закрыть уведомление" onClick={() => setToast("")}>
            <X size={16} />
          </button>
        </div>
      )}
    </div>
  );
}
