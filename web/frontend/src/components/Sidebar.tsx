import { useCallback, useEffect, useLayoutEffect, useMemo, useRef } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { NAV } from "./nav";
import { useAuth } from "../lib/auth";
import { isAdmin } from "../lib/types";

interface IndicatorState {
  x: number;
  y: number;
  w: number;
  h: number;
  vx: number;
  vy: number;
  vw: number;
  vh: number;
  targetX: number;
  targetY: number;
  targetW: number;
  targetH: number;
  initialized: boolean;
  moving: boolean;
  raf: number;
  lastTime: number;
}

const makeIndicatorState = (): IndicatorState => ({
  x: 0,
  y: 0,
  w: 0,
  h: 0,
  vx: 0,
  vy: 0,
  vw: 0,
  vh: 0,
  targetX: 0,
  targetY: 0,
  targetW: 0,
  targetH: 0,
  initialized: false,
  moving: false,
  raf: 0,
  lastTime: 0,
});

function isNavPathActive(pathname: string, path: string) {
  return pathname === path || pathname.startsWith(`${path}/`);
}

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const { user } = useAuth();
  const location = useLocation();
  const admin = isAdmin(user);
  const navRef = useRef<HTMLElement | null>(null);
  const indicatorRef = useRef<HTMLDivElement | null>(null);
  const itemRefs = useRef(new Map<string, HTMLAnchorElement>());
  const indicatorState = useRef<IndicatorState>(makeIndicatorState());
  const animateIndicatorRef = useRef<(time: number) => void>(() => {});
  const measureFrame = useRef(0);

  const visibleSections = useMemo(
    () =>
      NAV.map((section) => ({
        ...section,
        items: section.items.filter((it) => !it.adminOnly || admin),
      })).filter((section) => section.items.length),
    [admin],
  );

  const activeKey = useMemo(() => {
    for (const section of visibleSections) {
      const activeItem = section.items.find((it) => isNavPathActive(location.pathname, it.path));
      if (activeItem) return activeItem.key;
    }
    return undefined;
  }, [location.pathname, visibleSections]);

  const paintIndicator = useCallback((stretch = 1, direction = 1) => {
    const el = indicatorRef.current;
    if (!el) return;

    const state = indicatorState.current;
    el.style.width = `${state.w}px`;
    el.style.height = `${state.h}px`;
    el.style.transform = `translate3d(${state.x}px, ${state.y}px, 0) scaleY(${stretch})`;
    el.style.transformOrigin = direction >= 0 ? "50% 0%" : "50% 100%";
    el.style.opacity = state.initialized ? "1" : "0";
  }, []);

  const animateIndicator = useCallback(
    (time: number) => {
      const state = indicatorState.current;
      const dt = state.lastTime ? Math.min((time - state.lastTime) / 1000, 0.032) : 0.016;
      state.lastTime = time;

      const stiffness = 86;
      const damping = 17;
      const settleDistance = 0.18;
      const settleVelocity = 4.5;
      const stepSpring = (current: number, target: number, velocity: number) => {
        const acceleration = (target - current) * stiffness - velocity * damping;
        const nextVelocity = velocity + acceleration * dt;
        return {
          value: current + nextVelocity * dt,
          velocity: nextVelocity,
        };
      };

      const x = stepSpring(state.x, state.targetX, state.vx);
      const y = stepSpring(state.y, state.targetY, state.vy);
      const w = stepSpring(state.w, state.targetW, state.vw);
      const h = stepSpring(state.h, state.targetH, state.vh);

      state.x = x.value;
      state.y = y.value;
      state.w = w.value;
      state.h = h.value;
      state.vx = x.velocity;
      state.vy = y.velocity;
      state.vw = w.velocity;
      state.vh = h.velocity;

      const direction = state.targetY >= state.y ? 1 : -1;
      const stretch = 1 + Math.min(Math.abs(state.vy) * 0.00016, 0.055);
      paintIndicator(stretch, direction);

      const isSettled =
        Math.abs(state.targetX - state.x) < settleDistance &&
        Math.abs(state.targetY - state.y) < settleDistance &&
        Math.abs(state.targetW - state.w) < settleDistance &&
        Math.abs(state.targetH - state.h) < settleDistance &&
        Math.abs(state.vx) < settleVelocity &&
        Math.abs(state.vy) < settleVelocity &&
        Math.abs(state.vw) < settleVelocity &&
        Math.abs(state.vh) < settleVelocity;

      if (isSettled) {
        state.x = state.targetX;
        state.y = state.targetY;
        state.w = state.targetW;
        state.h = state.targetH;
        state.vx = 0;
        state.vy = 0;
        state.vw = 0;
        state.vh = 0;
        state.moving = false;
        state.raf = 0;
        state.lastTime = 0;
        paintIndicator(1, direction);
        return;
      }

      state.raf = window.requestAnimationFrame((nextTime) => animateIndicatorRef.current(nextTime));
    },
    [paintIndicator],
  );
  useLayoutEffect(() => {
    animateIndicatorRef.current = animateIndicator;
  }, [animateIndicator]);

  const setIndicatorTarget = useCallback(
    (metrics: { x: number; y: number; w: number; h: number }) => {
      const state = indicatorState.current;
      state.targetX = metrics.x;
      state.targetY = metrics.y;
      state.targetW = metrics.w;
      state.targetH = metrics.h;

      if (!state.initialized) {
        state.x = metrics.x;
        state.y = metrics.y;
        state.w = metrics.w;
        state.h = metrics.h;
        state.initialized = true;
        paintIndicator(1, 1);
        return;
      }

      if (!state.moving) {
        state.moving = true;
        state.lastTime = 0;
        state.raf = window.requestAnimationFrame(animateIndicator);
      }
    },
    [animateIndicator, paintIndicator],
  );

  const measureActiveItem = useCallback(() => {
    const nav = navRef.current;
    const activeItem = activeKey ? itemRefs.current.get(activeKey) : undefined;
    if (!nav || !activeItem) return;

    const navRect = nav.getBoundingClientRect();
    const itemRect = activeItem.getBoundingClientRect();
    setIndicatorTarget({
      x: itemRect.left - navRect.left,
      y: itemRect.top - navRect.top,
      w: itemRect.width,
      h: itemRect.height,
    });
  }, [activeKey, setIndicatorTarget]);

  const setItemRef = useCallback((key: string, node: HTMLAnchorElement | null) => {
    if (node) {
      itemRefs.current.set(key, node);
      return;
    }
    itemRefs.current.delete(key);
  }, []);

  useLayoutEffect(() => {
    measureActiveItem();
  }, [measureActiveItem]);

  useEffect(() => {
    const scheduleMeasure = () => {
      if (measureFrame.current) window.cancelAnimationFrame(measureFrame.current);
      measureFrame.current = window.requestAnimationFrame(() => {
        measureFrame.current = 0;
        measureActiveItem();
      });
    };

    const observer = new ResizeObserver(scheduleMeasure);
    if (navRef.current) observer.observe(navRef.current);
    const activeItem = activeKey ? itemRefs.current.get(activeKey) : undefined;
    if (activeItem) observer.observe(activeItem);
    window.addEventListener("resize", scheduleMeasure);

    return () => {
      observer.disconnect();
      window.removeEventListener("resize", scheduleMeasure);
      if (measureFrame.current) window.cancelAnimationFrame(measureFrame.current);
    };
  }, [activeKey, measureActiveItem]);

  useEffect(
    () => () => {
      const state = indicatorState.current;
      if (state.raf) window.cancelAnimationFrame(state.raf);
      if (measureFrame.current) window.cancelAnimationFrame(measureFrame.current);
    },
    [],
  );

  return (
    <aside
      className="scroll-thin flex h-full w-[260px] shrink-0 flex-col overflow-y-auto px-3 py-5 text-[var(--sidebar-text)]"
      style={{ background: "var(--sidebar-bg)", backdropFilter: "blur(24px)" }}
    >
      {/* Logo */}
      <div className="mb-6 flex items-center gap-3 px-3">
        <div
          className="flex h-11 w-11 items-center justify-center rounded-2xl text-lg font-black text-white shadow-lg"
          style={{ background: "linear-gradient(135deg, var(--accent), var(--purple))" }}
        >
          CP
        </div>
        <div className="leading-tight">
          <div className="text-sm font-bold text-white">KetoanMini</div>
          <div className="text-[11px] text-[var(--sidebar-text)]">Inox Cường Phát</div>
        </div>
      </div>

      <nav ref={navRef} className="liquid-sidebar-nav relative flex-1">
        <div ref={indicatorRef} className="liquid-active-indicator" aria-hidden="true" />
        <div className="relative z-10 space-y-5">
          {visibleSections.map((section, i) => (
            <div key={i}>
              {section.title && (
                <div className="mb-2 px-3 text-[10px] font-bold tracking-widest text-[var(--sidebar-text)] opacity-60">
                  {section.title}
                </div>
              )}
              <div className="space-y-0.5">
                {section.items.map((it) => {
                  const Icon = it.icon;
                  return (
                    <NavLink
                      key={it.key}
                      ref={(node) => setItemRef(it.key, node)}
                      to={it.path}
                      onClick={onNavigate}
                      className={({ isActive }) =>
                        `liquid-sidebar-link group relative flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-all duration-300 ${
                          isActive
                            ? "is-active text-white"
                            : "hover:bg-white/8 hover:text-white"
                        }`
                      }
                    >
                      <Icon className="h-[18px] w-[18px] shrink-0" />
                      <span className="flex-1">{it.label}</span>
                      {!it.ready && (
                        <span className="rounded-md bg-white/10 px-1.5 py-0.5 text-[9px] font-semibold opacity-70">
                          sắp có
                        </span>
                      )}
                    </NavLink>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      </nav>
    </aside>
  );
}
