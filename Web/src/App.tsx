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
  LogOut,
  MailCheck,
  Mountain,
  Search,
  Users,
  X,
} from "lucide-react";
import { api } from "./api";
import type { AuthSession } from "./api";
import { AuthModal } from "./components/AuthModal";
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
  const [authSession, setAuthSession] = useState<AuthSession | null>(null);
  const [booting, setBooting] = useState(true);
  const [authOpen, setAuthOpen] = useState(false);
  const [afterAuth, setAfterAuth] = useState<"post" | "bike" | null>(null);
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
      api<AuthSession>("/auth/session"),
      api<Overview>("/feed/overview"),
      api<Community[]>("/communities"),
    ])
      .then(([session, stats, groups]) => {
        if (active) {
          setMe(session.user);
          setAuthSession(session);
          setOverview(stats);
          setCommunities(groups);
          setBootError("");
        }
      })
      .catch((e) => {
        if (active) setBootError(e.message);
      })
      .finally(() => { if (active) setBooting(false); });
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
  useEffect(() => {
    const requireLogin = () => { setMe(null); setAuthOpen(true); setVersion((value) => value + 1); };
    window.addEventListener("bikehub:auth-required", requireLogin);
    return () => window.removeEventListener("bikehub:auth-required", requireLogin);
  }, []);
  const create = (type: "post" | "bike") => {
    if (me) setModal(type);
    else { setAfterAuth(type); setAuthOpen(true); }
  };
  const logout = async () => {
    try {
      const session = await api<AuthSession>("/auth/logout", { method: "POST" });
      setMe(null); setAuthSession(session); setModal(null); reload(); navigate("/");
      notify("Вы вышли из аккаунта");
    } catch (error) { notify((error as Error).message); }
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
              <>
              <a
                className="user-menu"
                href={`#/user/${me.id}`}
                aria-label="Открыть мой профиль"
              >
                <Avatar user={me} small />
                <span>{me.name.split(" ")[0]}</span>
                {authSession?.emailConfirmed && <MailCheck size={15} aria-label="Почта подтверждена" />}
                <ChevronDown size={14} />
              </a>
              <button className="icon-button" aria-label="Выйти из аккаунта" title="Выйти" onClick={logout}><LogOut size={18} /></button>
              </>
            )}
            {!me && <button className="button primary login-button" onClick={() => setAuthOpen(true)}>Войти</button>}
          </div>
        </header>
        <main className="page">
          {bootError ? (
            <ErrorBox message={bootError} retry={reload} />
          ) : booting ? (
            <Loading />
          ) : (
            <>
              <div className="content-grid">
                <div className="primary-content">
                  {!me && ["saved", "garage"].includes(section) && (
                    <Empty title={section === "garage" ? "Ваш гараж начинается здесь" : "Сохраните свои истории"}
                      description="Войдите по почте, чтобы открыть своё пространство в BikeHub."
                      action={<button className="button primary" onClick={() => setAuthOpen(true)}>Войти в BikeHub</button>} />
                  )}
                  {(!section || (section === "saved" && me)) && (
                    <Feed
                      key={`${section}-${searchQuery}-${version}`}
                      saved={section === "saved"}
                      search={searchQuery}
                      version={version}
                      me={me}
                      notify={notify}
                      onCreate={() => create("post")}
                    />
                  )}
                  {(section === "bikes" || (section === "garage" && me)) && (
                    <BikeDirectory
                      key={`${section}-${version}`}
                      garage={section === "garage"}
                      me={me}
                      version={version}
                      onCreate={() => create("bike")}
                    />
                  )}
                  {section === "bike" && (
                    <BikeDetail
                      key={`${path}-${version}`}
                      id={path.split("/")[2]}
                      me={me}
                      version={version}
                      onCreate={() => create("post")}
                      notify={notify}
                    />
                  )}
                  {section === "post" && (
                    <PostDetail
                      key={`${path}-${version}`}
                      id={path.split("/")[2]}
                      me={me}
                      notify={notify}
                    />
                  )}
                  {section === "user" && (
                    <UserProfile
                      key={`${path}-${version}`}
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
                  onCreate={() => create("bike")}
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
      {authOpen && <AuthModal session={authSession}
        close={() => { setAuthOpen(false); setAfterAuth(null); }}
        onSuccess={(session) => {
          setAuthSession(session); setMe(session.user); setAuthOpen(false);
          setModal(afterAuth); setAfterAuth(null); reload(); notify("Почта подтверждена. Добро пожаловать!");
        }} />}
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
