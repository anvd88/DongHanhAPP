import type { AnimationEvent, CSSProperties } from "react";
import { CheckCircle2 } from "lucide-react";
import "./login-transition.css";

export type LoginTransitionPhase = "covering" | "waiting" | "revealing";

export type LoginTransitionOrigin = {
  left: number;
  top: number;
  width: number;
  height: number;
};

export type LoginTransitionState = {
  id: number;
  phase: LoginTransitionPhase;
  origin: LoginTransitionOrigin;
};

type TransitionStyle = CSSProperties & {
  "--login-transition-center-x": string;
  "--login-transition-center-y": string;
  "--login-transition-radius": string;
  "--login-transition-origin-left": string;
  "--login-transition-origin-top": string;
  "--login-transition-origin-right": string;
  "--login-transition-origin-bottom": string;
  "--login-transition-origin-width": string;
  "--login-transition-origin-height": string;
  "--login-transition-origin-radius": string;
  "--login-transition-band-top": string;
  "--login-transition-band-bottom": string;
};

export function LoginTransitionLayer({
  transition,
  onCoverComplete,
  onRevealComplete,
}: {
  transition: LoginTransitionState | null;
  onCoverComplete: () => void;
  onRevealComplete: () => void;
}) {
  if (!transition) return null;

  const viewportWidth = typeof window === "undefined" ? 1440 : window.innerWidth;
  const viewportHeight = typeof window === "undefined" ? 900 : window.innerHeight;
  const hasButtonOrigin = transition.origin.width >= 40 && transition.origin.height >= 24;
  const originWidth = Math.min(
    viewportWidth,
    hasButtonOrigin ? transition.origin.width : Math.min(220, viewportWidth - 32),
  );
  const originHeight = Math.min(
    viewportHeight,
    hasButtonOrigin ? transition.origin.height : 52,
  );
  const fallbackLeft = (viewportWidth - originWidth) / 2;
  const fallbackTop = (viewportHeight - originHeight) / 2;
  const originLeft = Math.max(
    0,
    Math.min(hasButtonOrigin ? transition.origin.left : fallbackLeft, viewportWidth - originWidth),
  );
  const originTop = Math.max(
    0,
    Math.min(hasButtonOrigin ? transition.origin.top : fallbackTop, viewportHeight - originHeight),
  );
  const originRight = Math.max(0, viewportWidth - originLeft - originWidth);
  const originBottom = Math.max(0, viewportHeight - originTop - originHeight);
  const bandPadding = Math.max(10, originHeight * 0.22);
  const centerX = originLeft + originWidth / 2;
  const centerY = originTop + originHeight / 2;
  const style: TransitionStyle = {
    "--login-transition-center-x": `${centerX}px`,
    "--login-transition-center-y": `${centerY}px`,
    "--login-transition-radius": "150vmax",
    "--login-transition-origin-left": `${originLeft}px`,
    "--login-transition-origin-top": `${originTop}px`,
    "--login-transition-origin-right": `${originRight}px`,
    "--login-transition-origin-bottom": `${originBottom}px`,
    "--login-transition-origin-width": `${originWidth}px`,
    "--login-transition-origin-height": `${originHeight}px`,
    "--login-transition-origin-radius": `${Math.min(14, originHeight / 2)}px`,
    "--login-transition-band-top": `${Math.max(0, originTop - bandPadding)}px`,
    "--login-transition-band-bottom": `${Math.max(0, originBottom - bandPadding)}px`,
  };

  const handleSurfaceAnimationEnd = (event: AnimationEvent<HTMLDivElement>) => {
    if (event.target !== event.currentTarget || event.pseudoElement) return;
    if (
      transition.phase === "covering"
      && event.animationName === "login-route-portal-cover"
    ) {
      onCoverComplete();
    }
    if (
      transition.phase === "revealing"
      && event.animationName === "login-route-portal-reveal"
    ) {
      onRevealComplete();
    }
  };

  return (
    <div
      key={transition.id}
      className="login-route-transition"
      data-phase={transition.phase}
      data-button-origin={hasButtonOrigin ? "true" : "false"}
      style={style}
      role="status"
      aria-live="polite"
      aria-atomic="true"
      aria-label={
        transition.phase === "revealing"
          ? "Đồng bộ hoàn tất, đang mở ứng dụng"
          : "Đăng nhập thành công, đang đồng bộ không gian làm việc"
      }
    >
      <div
        className="login-route-transition-surface"
        onAnimationEnd={handleSurfaceAnimationEnd}
      >
        <span className="login-route-transition-grid" aria-hidden="true" />
        <span className="login-route-transition-glow" aria-hidden="true" />
        <span className="login-route-transition-beam" aria-hidden="true" />

        <span className="login-route-transition-launcher" aria-hidden="true">
          <span>Đăng nhập thành công</span>
          <CheckCircle2 />
        </span>

        <span className="login-route-transition-echo" aria-hidden="true">
          <i />
          <i />
          <i />
        </span>

        <span className="login-route-transition-tunnel" aria-hidden="true">
          <i />
          <i />
          <i />
        </span>

        <span className="login-route-transition-status">
          <span className="login-route-transition-kicker">PHIÊN LÀM VIỆC ĐÃ SẴN SÀNG</span>
          <span className="login-route-transition-check" aria-hidden="true">
            <CheckCircle2 />
          </span>
          <span className="login-route-transition-copy">
            <strong>Đăng nhập thành công</strong>
            <small className="login-route-transition-syncing">
              Đang đồng bộ không gian làm việc của bạn
            </small>
            <small className="login-route-transition-synced">
              Đồng bộ hoàn tất · Đang đi vào ứng dụng
            </small>
          </span>
          <span className="login-route-transition-progress" aria-hidden="true"><i /></span>
        </span>
      </div>

      <span className="login-route-transition-screen" aria-hidden="true">
        <i />
        <i />
        <i />
      </span>
    </div>
  );
}
