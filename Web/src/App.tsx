import { userHref } from "./api";
import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import {
  ArrowUpRight,
  Bike as BikeIcon,
  Bookmark,
  CalendarDays,
  Check,
  ChevronDown,
  ChevronRight,
  Compass,
  House,
  Menu,
  Map as MapIcon,
  MessageCircle,
  LogOut,
  MailCheck,
  Mountain,
  Search,
  Settings as SettingsIcon,
  Users,
  X,
} from "lucide-react";
import { api } from "./api";
import type { AuthSession } from "./api";
import { ThemePicker } from "./components/ThemePicker";
import { ChooseNickname } from "./components/ChooseNickname";
import { AuthModal } from "./components/AuthModal";
import type { Community, Overview, Page, User } from "./api";
import { Avatar, Empty, ErrorBox, Loading } from "./components/ui";
import { RightRail } from "./components/RightRail";
import { CreateModal } from "./components/CreateModal";
import { Feed } from "./pages/Feed";
import { BikeDetail, BikeDirectory } from "./pages/Bikes";
import { PostDetail } from "./pages/PostDetail";
import { UserProfile } from "./pages/UserProfile";
import { Moderation } from "./pages/Moderation";
import { CommunityDetail, GroupEntryDetail } from "./pages/CommunityDetail";
import { Settings } from "./pages/Settings";
import { Communities } from "./pages/Communities";
import { Rides, RideDetail } from "./pages/Rides";
import { RouteLibrary, LibraryRouteDetail } from "./pages/RouteLibrary";
import { Maintenance } from "./pages/Maintenance";
import { Messages, MessageCount } from "./pages/Messages";
import { Conversation } from "./pages/Conversation";
import { MessageReports } from "./pages/MessageReports";
import { NotificationBell, Notifications } from "./pages/Notifications";
import { clearLocalDrafts } from "./components/useDraft";
import { SearchResults, searchKind } from "./pages/SearchResults";
import { useMobileMenu } from "./components/useMobileMenu";

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
  const [railError, setRailError] = useState(""), [railRevision, setRailRevision] = useState(0);
  const [version, setVersion] = useState(0);
  const [modal, setModal] = useState<"post" | "bike" | null>(null);
  const [mobileOpen, setMobileOpen] = useState(false);
  const sidebar = useRef<HTMLElement>(null), menuButton = useRef<HTMLButtonElement>(null);
  const closeMobile = useCallback(() => setMobileOpen(false), []);
  const mobile = useMobileMenu(mobileOpen, closeMobile, sidebar, menuButton);
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
    api<AuthSession>("/auth/session")
      .then(session => {
        if (active) {
          setMe(session.user);
          setAuthSession(session);
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
    let active = true; setRailError("");
    Promise.allSettled([api<Overview>("/feed/overview"), api<Page<Community>>("/communities/catalog?pageSize=5")])
      .then(([stats, groups]) => {
        if (!active) return;
        setOverview(stats.status === "fulfilled" ? stats.value : null);
        setCommunities(groups.status === "fulfilled" ? groups.value.items : []);
        if (stats.status === "rejected" || groups.status === "rejected") setRailError("Не удалось обновить рекомендации сообщества.");
      });
    return () => { active = false; };
  }, [version, railRevision]);
  useEffect(() => {
    setMobileOpen(false);
  }, [route]);
  useEffect(() => () => clearTimeout(toastTimer.current), []);
  const [path, search = ""] = route.split("?");
  const searchQuery = new URLSearchParams(search).get("q") || "";
  const resultKind = searchKind(new URLSearchParams(search).get("kind"));
  const resultPage = Math.min(1000000, Math.max(1, Number.parseInt(new URLSearchParams(search).get("page") || "1", 10) || 1));
  const section = path.split("/")[1];
  const searchSubmit = (event: FormEvent) => {
    event.preventDefault();
    navigate(`/search?q=${encodeURIComponent(searchText.trim())}`);
  };
  useEffect(() => setSearchText(searchQuery), [searchQuery]);
  useEffect(() => {
    const requireLogin = () => { setMe(null); setAuthOpen(true); setVersion((value) => value + 1); };
    const changed = () => setVersion(value => value + 1);
    window.addEventListener("bikehub:auth-required", requireLogin);
    window.addEventListener("bikehub:content-changed", changed);
    return () => {
      window.removeEventListener("bikehub:auth-required", requireLogin);
      window.removeEventListener("bikehub:content-changed", changed);
    };
  }, []);
  const create = (type: "post" | "bike") => {
    if (me) setModal(type);
    else { setAfterAuth(type); setAuthOpen(true); }
  };
  const logout = async () => {
    try {
      const session = await api<AuthSession>("/auth/logout", { method: "POST" });
      if (me) { clearLocalDrafts(me.id); }
      setMe(null); setAuthSession(session); setModal(null); reload(); navigate("/");
      notify("Вы вышли из аккаунта");
    } catch (error) { notify((error as Error).message); }
  };
  const nav = [
    { path: "/search", icon: Search, text: "Поиск", active: section === "search" },
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
      active: ["communities", "community", "group-entry"].includes(section),
    },
    { path: "/rides", icon: CalendarDays, text: "Покатушки", active: section === "rides" || section === "ride" },
    { path: "/routes", icon: MapIcon, text: "Маршруты", active: section === "routes" || section === "route" },
    { path: "/messages", icon: MessageCircle, text: "Сообщения", active: section === "messages" || section === "message" },
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
        <div
          className="sidebar-backdrop"
          aria-hidden="true"
          onClick={closeMobile}
        />
      )}
      <aside id="main-sidebar" ref={sidebar} className={`sidebar ${mobileOpen ? "open" : ""}`} inert={mobile && !mobileOpen}
        role={mobile && mobileOpen ? "dialog" : undefined} aria-modal={mobile && mobileOpen ? true : undefined} aria-label="Меню BikeHub" tabIndex={-1}>
        <button type="button" className="icon-button mobile-menu-close" aria-label="Закрыть меню" onClick={closeMobile}><X size={22} /></button>
        <a className="brand" href="#/" aria-label="BikeHub — на главную">
          <span className="brand-mark">
            <img src="./brand/bikehub-mark-light.svg?v=3" width="36" height="36" alt="" />
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
              {item.path === "/messages" && me && <MessageCount userId={me.id} />}
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
            href={userHref(me)}
            className={`garage-link ${((section === "user" && path.endsWith(me.id)) || (section === "u" && path.endsWith("/" + encodeURIComponent(me.handle)))) ? "active" : ""}`}
          >
            <span className="profile-dot" />
            Мой профиль
            <ChevronRight size={16} />
          </a>
        )}
        {me && <a href="#/settings" className={`garage-link ${section === "settings" ? "active" : ""}`}><SettingsIcon size={19} />Настройки<ChevronRight size={16} /></a>}
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

      <div className="workspace" inert={mobile && mobileOpen}>
        <header className="topbar">
          <button
            className="icon-button menu-button"
            ref={menuButton}
            aria-label="Открыть меню"
            aria-controls="main-sidebar"
            aria-expanded={mobileOpen}
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
                  rides: "Покатушки",
                  ride: "Покатушка",
                  routes: "Маршруты",
                  route: "Маршрут",
                  maintenance: "Обслуживание",
                  messages: "Сообщения",
                  message: "Переписка",
                  "message-reports": "Жалобы на сообщения",
                  community: "Сообщество",
                  "group-entry": "Обсуждение сообщества",
                  moderation: "Модерация",
                  saved: "Закладки",
                  garage: "Мой гараж",
                  user: "Профиль",
                  u: "Профиль",
                  settings: "Настройки",
                  post: "История",
                  notifications: "Уведомления",
                  search: "Поиск",
                } as Record<string, string>
              )[section] || "Лента"}
            </span>
          </div>
          <form className="global-search" onSubmit={searchSubmit}>
            <Search size={18} />
            <input
              aria-label="Поиск по записям, людям и сообществам"
              placeholder="Записи, люди, сообщества…"
              maxLength={200}
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
            />
            <kbd>↵</kbd>
          </form>
          <div className="topbar-right">
            {me && <NotificationBell key={me.id} userId={me.id} />}
            <ThemePicker />
            <span className="demo-label">
              <span />
              Бета
            </span>
            {me && (
              <>
              <a
                className="user-menu"
                href={userHref(me)}
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
              <div className={`content-grid${section === "messages" || section === "message" ? " messaging-grid" : ""}`}>
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
                  {section === "search" && <SearchResults search={searchQuery} kind={resultKind} page={resultPage} version={version} key={me?.id || "guest"} notify={notify} />}
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
                      key={`${path}-${version}-${me?.id || "guest"}`}
                      id={path.split("/")[2]}
                      targetComment={new URLSearchParams(search).get("comment")}
                      me={me}
                      notify={notify}
                    />
                  )}
                  {(section === "user" || section === "u") && (
                    <UserProfile
                      key={`${path}-${version}`}
                      id={path.split("/")[2]}
                      byNickname={section === "u"}
                      me={me}
                      notify={notify}
                      reload={reload}
                      version={version}
                    />
                  )}
                  {section === "settings" && <Settings me={me} maskedEmail={authSession?.maskedEmail || null} account={path.split("/")[2] === "account"} />}
                  {section === "moderation" && <Moderation key={me?.id || "guest"} me={me} />}
                  {section === "notifications" && <Notifications key={me?.id || "guest"} me={me} />}
                  {section === "community" && <CommunityDetail key={`${path}-${searchQuery}-${me?.id || "guest"}`} id={path.split("/")[2]} me={me} reload={reload} search={searchQuery} />}
                  {section === "group-entry" && <GroupEntryDetail key={`${path}-${me?.id || "guest"}`} id={path.split("/")[2]} me={me} targetComment={new URLSearchParams(search).get("comment")} />}
                  {section === "rides" && <Rides key={`${new URLSearchParams(search).get("group") || "all"}-${me?.id || "guest"}`} me={me} groupId={new URLSearchParams(search).get("group")} />}
                  {section === "routes" && <RouteLibrary key={me?.id || "guest"} me={me} />}
                  {section === "maintenance" && <Maintenance key={`${path}-${me?.id || "guest"}`} bikeId={path.split("/")[2]} me={me} />}
                  {section === "messages" && <Messages key={`${new URLSearchParams(search).get("to") || "inbox"}-${me?.id || "guest"}`} me={me} toUserId={new URLSearchParams(search).get("to")} />}
                  {section === "message" && <Conversation key={`${path}-${me?.id || "guest"}`} id={path.split("/")[2]} me={me} />}
                  {section === "message-reports" && <MessageReports key={me?.id || "guest"} me={me} />}
                  {section === "route" && <LibraryRouteDetail key={`${route}-${me?.id || "guest"}`} id={path.split("/")[2]} me={me} source={(["post", "entry", "ride"] as const).map(kind => ({ kind, id: new URLSearchParams(search).get(kind) || "" })).find(item => !!item.id)} />}
                  {section === "ride" && <RideDetail key={`${path}-${new URLSearchParams(search).get("group") || ""}-${me?.id || "guest"}`} id={path.split("/")[2]} me={me} groupId={new URLSearchParams(search).get("group")} />}
                  {section === "communities" && (
                    <Communities
                      version={version}
                      me={me}
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
                      "u",
                      "communities",
                      "community",
                      "group-entry",
                      "moderation",
                      "settings",
                      "notifications",
                      "rides",
                      "ride",
                      "routes",
                      "route",
                      "maintenance",
                      "messages",
                      "message",
                      "message-reports",
                      "search",
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
                {section !== "messages" && section !== "message" && <RightRail
                  overview={overview}
                  communities={communities}
                  error={railError}
                  retry={() => setRailRevision(v => v + 1)}
                  onCreate={() => create("bike")}
                />}
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
      {me && !me.handle && !authOpen && <ChooseNickname saved={user => { setMe(user); reload(); }} logout={logout} />}
      {authOpen && <AuthModal session={authSession}
        close={() => { setAuthOpen(false); setAfterAuth(null); }}
        onSuccess={(session) => {
          setAuthSession(session); setMe(session.user); setAuthOpen(false);
          setModal(afterAuth); setAfterAuth(null); reload(); notify("Почта подтверждена. Добро пожаловать!");
        }} />}
      {modal && me && me.handle && (
        <CreateModal
          key={`${me.id}-${modal}`}
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
