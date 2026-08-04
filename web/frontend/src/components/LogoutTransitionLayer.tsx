import { useEffect, useLayoutEffect, useState, type CSSProperties } from "react";
import { LogOut, Sparkles } from "lucide-react";
import { MotionConfig, motion } from "motion/react";
import type { LoginTransitionOrigin } from "./LoginTransitionLayer";
import "./login-transition.css";
import "./logout-transition.css";

export type LogoutTransitionPhase = "covering" | "waiting" | "revealing";

export type LogoutAvatarSnapshot = {
  origin: LoginTransitionOrigin;
  imageSrc: string | null;
  label: string;
  backgroundImage: string;
  backgroundColor: string;
  color: string;
  fontFamily: string;
  fontSize: string;
  fontWeight: string;
};

export type LogoutTransitionState = {
  id: number;
  phase: LogoutTransitionPhase;
  avatar: LogoutAvatarSnapshot;
  accountName: string;
};

type MeasuredLoginTarget = {
  transitionId: number;
  rect: LoginTransitionOrigin;
};

// Đăng xuất đảo đúng nhịp của đăng nhập: 0.82s là chiều ngược của đoạn card -> avatar,
// còn 0.9s là chiều ngược của đoạn nút Login -> curtain kín.
const AVATAR_TO_COVER_EASE = [0.76, 0, 0.24, 1] as const;
const COVER_TO_LOGIN_EASE = [0.7, 0, 0.84, 0] as const;
const COVER_DURATION_SECONDS = 0.82;
const REVEAL_DURATION_SECONDS = 0.9;
const MIN_COVERED_MS = 600;

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function sameRect(current: LoginTransitionOrigin | null, next: LoginTransitionOrigin) {
  return Boolean(
    current
    && Math.abs(current.left - next.left) < 0.5
    && Math.abs(current.top - next.top) < 0.5
    && Math.abs(current.width - next.width) < 0.5
    && Math.abs(current.height - next.height) < 0.5,
  );
}

export function LogoutTransitionLayer({
  transition,
  onCoverComplete,
  onLoginReady,
  onRevealComplete,
}: {
  transition: LogoutTransitionState | null;
  onCoverComplete: () => void;
  onLoginReady: () => void;
  onRevealComplete: () => void;
}) {
  const [measuredLoginTarget, setMeasuredLoginTarget] = useState<MeasuredLoginTarget | null>(null);
  const transitionId = transition?.id ?? null;
  const transitionPhase = transition?.phase ?? null;
  const loginTarget = measuredLoginTarget?.transitionId === transitionId
    ? measuredLoginTarget.rect
    : null;

  // Sau khi phiên được đóng, trang Login thật sẽ được router dựng dưới lớp phủ. Đo đúng nút
  // Đăng nhập để nửa sau kết thúc tại chính nơi nửa đầu của hiệu ứng đăng nhập bắt đầu.
  useLayoutEffect(() => {
    if (transitionId === null || transitionPhase === "covering") return;

    let animationFrame = 0;
    const measureTarget = () => {
      const target = document.querySelector<HTMLElement>("[data-logout-transition-target='true']");
      const loginPage = target?.closest<HTMLElement>(".login-page")
        ?? document.querySelector<HTMLElement>(".login-page");
      // Dừng các animation vào trang mặc định của Login trong lần mount này. Nếu để chúng chạy
      // đồng thời với iris thì trình duyệt phải compositing hai chuyển cảnh và dễ rơi frame.
      loginPage?.setAttribute("data-logout-arrival", "true");
      if (!target) return;
      const rect = target.getBoundingClientRect();
      if (rect.width < 24 || rect.height < 20) return;
      const measured = { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
      setMeasuredLoginTarget((current) => (
        current?.transitionId === transitionId && sameRect(current.rect, measured)
          ? current
          : { transitionId, rect: measured }
      ));
    };
    const scheduleMeasure = () => {
      window.cancelAnimationFrame(animationFrame);
      animationFrame = window.requestAnimationFrame(measureTarget);
    };
    const observer = new MutationObserver(scheduleMeasure);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener("resize", scheduleMeasure, { passive: true });
    measureTarget();

    return () => {
      observer.disconnect();
      window.removeEventListener("resize", scheduleMeasure);
      window.cancelAnimationFrame(animationFrame);
    };
  }, [transitionId, transitionPhase]);

  useEffect(() => {
    if (transition?.phase !== "waiting") return;

    let firstFrame = 0;
    let secondFrame = 0;
    let readyTimer = 0;
    let finished = false;
    const reveal = () => {
      if (finished) return;
      finished = true;
      firstFrame = window.requestAnimationFrame(() => {
        secondFrame = window.requestAnimationFrame(onLoginReady);
      });
    };

    // Nút thật là mốc chính. Timer chỉ là lối thoát cho một route lỗi không dựng được Login.
    const safetyTimer = window.setTimeout(reveal, 2_500);
    // Giữ curtain kín cùng 600ms như chiều đăng nhập để lời xác nhận đọc được và Login thật
    // hoàn tất layout trước khi nửa sau bắt đầu.
    if (loginTarget) readyTimer = window.setTimeout(reveal, MIN_COVERED_MS);

    return () => {
      finished = true;
      window.clearTimeout(safetyTimer);
      window.clearTimeout(readyTimer);
      window.cancelAnimationFrame(firstFrame);
      window.cancelAnimationFrame(secondFrame);
    };
  }, [loginTarget, onLoginReady, transition?.phase]);

  if (!transition) return null;

  const viewportWidth = typeof window === "undefined" ? 1440 : window.innerWidth;
  const viewportHeight = typeof window === "undefined" ? 900 : window.innerHeight;
  const origin = transition.avatar.origin;
  const hasVisibleOrigin = origin.width >= 20 && origin.height >= 20;
  const originX = hasVisibleOrigin
    ? clamp(origin.left + origin.width / 2, 0, viewportWidth)
    : viewportWidth / 2;
  const originY = hasVisibleOrigin
    ? clamp(origin.top + origin.height / 2, 0, viewportHeight)
    : viewportHeight / 2;
  const avatarWidth = hasVisibleOrigin ? origin.width : 48;
  const avatarHeight = hasVisibleOrigin ? origin.height : 48;
  const originRadius = Math.max(24, Math.hypot(avatarWidth, avatarHeight) / 2);
  const coverRadius = Math.ceil(Math.max(
    Math.hypot(originX, originY),
    Math.hypot(viewportWidth - originX, originY),
    Math.hypot(originX, viewportHeight - originY),
    Math.hypot(viewportWidth - originX, viewportHeight - originY),
  )) + 72;

  const fallbackTargetX = viewportWidth >= 900 ? viewportWidth * 0.75 : viewportWidth / 2;
  const fallbackTargetY = clamp(viewportHeight * 0.62, 280, viewportHeight - 72);
  const targetX = loginTarget ? loginTarget.left + loginTarget.width / 2 : fallbackTargetX;
  const targetY = loginTarget ? loginTarget.top + loginTarget.height / 2 : fallbackTargetY;
  const targetWidth = loginTarget?.width ?? Math.min(420, viewportWidth - 40);
  const targetHeight = loginTarget?.height ?? 52;
  const targetRadius = Math.min(18, targetHeight / 2);
  const centerX = viewportWidth / 2;
  const centerY = viewportHeight / 2;
  const statusWidth = Math.min(390, viewportWidth - 40);
  const statusHeight = viewportWidth <= 600 ? 268 : 286;
  const routeX = centerX - targetX;
  const routeY = centerY - targetY;
  const routeLength = Math.max(1, Math.hypot(routeX, routeY));
  const routeAngle = Math.atan2(routeY, routeX) * (180 / Math.PI);
  const beaconSize = clamp(Math.min(targetWidth, targetHeight), 52, 96);
  const isRevealing = transition.phase === "revealing";

  const surfaceTarget = isRevealing
    ? { clipPath: `circle(0px at ${targetX}px ${targetY}px)`, opacity: 0.98 }
    : { clipPath: `circle(${coverRadius}px at ${originX}px ${originY}px)`, opacity: 1 };
  const surfaceTransition = transition.phase === "covering"
    ? { duration: COVER_DURATION_SECONDS, ease: AVATAR_TO_COVER_EASE }
    : isRevealing
      ? { duration: REVEAL_DURATION_SECONDS, ease: COVER_TO_LOGIN_EASE }
      : { duration: 0 };

  const statusTarget = isRevealing
    ? {
        x: targetX - centerX,
        y: targetY - centerY,
        width: targetWidth,
        height: targetHeight,
        borderRadius: targetRadius,
        opacity: 0,
        scale: 0.9,
      }
    : {
        x: 0,
        y: 0,
        width: statusWidth,
        height: statusHeight,
        borderRadius: viewportWidth <= 600 ? 26 : 30,
        opacity: 1,
        scale: 1,
      };
  const statusTransition = transition.phase === "covering"
    ? { duration: 0.76, ease: AVATAR_TO_COVER_EASE }
    : isRevealing
      ? { duration: 0.58, ease: COVER_TO_LOGIN_EASE }
      : { duration: 0 };

  const layerStyle = {
    "--login-route-origin-x": `${originX}px`,
    "--login-route-origin-y": `${originY}px`,
    "--login-route-reveal-x": `${targetX}px`,
    "--login-route-reveal-y": `${targetY}px`,
  } as CSSProperties;

  return (
    <MotionConfig reducedMotion="never">
      <div
        key={transition.id}
        className="login-route-transition logout-route-transition"
        data-phase={transition.phase}
        style={layerStyle}
        role="status"
        aria-live="polite"
        aria-atomic="true"
        aria-label={isRevealing
          ? "Đăng xuất hoàn tất, đang mở trang đăng nhập"
          : "Đang đóng phiên làm việc an toàn"}
      >
        <motion.div
          className="login-route-transition-curtain"
          initial={{ clipPath: `circle(${originRadius}px at ${originX}px ${originY}px)`, opacity: 1 }}
          animate={surfaceTarget}
          transition={surfaceTransition}
          onAnimationComplete={() => {
            if (transition.phase === "covering") onCoverComplete();
            if (transition.phase === "revealing") onRevealComplete();
          }}
          aria-hidden="true"
        >
          <span className="login-route-transition-grain" />
          <span className="login-route-transition-flare" />
          <span className="login-route-transition-horizon" />
        </motion.div>

        <motion.span
          className="login-route-transition-trajectory"
          style={{ left: targetX, top: targetY, width: routeLength, rotate: routeAngle }}
          initial={{ opacity: 0, scaleX: 1 }}
          animate={isRevealing
            ? { opacity: [0, 0.72, 0], scaleX: [1, 1, 0] }
            : { opacity: 0, scaleX: 1 }}
          transition={{ duration: 0.7, delay: 0.04, ease: COVER_TO_LOGIN_EASE, times: [0, 0.48, 1] }}
          aria-hidden="true"
        />

        <motion.span
          className="login-route-transition-beacon"
          style={{ left: targetX, top: targetY, width: beaconSize, height: beaconSize }}
          initial={{ opacity: 0, scale: 2.36 }}
          animate={isRevealing
            ? { opacity: [0, 0.62, 1, 0], scale: [2.36, 1.72, 1, 0.72] }
            : { opacity: 0, scale: 2.36 }}
          transition={{ duration: 0.76, ease: COVER_TO_LOGIN_EASE, times: [0, 0.42, 0.78, 1] }}
          aria-hidden="true"
        />

        <motion.div
          className="login-route-transition-status"
          initial={{
            x: originX - centerX,
            y: originY - centerY,
            width: avatarWidth,
            height: avatarHeight,
            borderRadius: Math.max(avatarWidth, avatarHeight) / 2,
            opacity: 1,
            scale: 1,
          }}
          animate={statusTarget}
          transition={statusTransition}
        >
          <motion.div
            className="login-route-transition-status-content logout-route-transition-content"
            initial={{ opacity: 0, scale: 0.78 }}
            animate={isRevealing ? { opacity: 0, scale: 0.72 } : { opacity: 1, scale: 1 }}
            transition={isRevealing
              ? { duration: 0.28, ease: [0.55, 0, 1, 0.45] }
              : { duration: 0.34, delay: 0.22, ease: AVATAR_TO_COVER_EASE }}
          >
            <span className="login-route-transition-orbit" aria-hidden="true"><i /><i /></span>
            <span className="login-route-transition-check logout-route-transition-icon" aria-hidden="true">
              <LogOut />
            </span>
            <span className="login-route-transition-kicker"><Sparkles /> Chuyển phiên bảo mật</span>
            <strong>Đang đăng xuất an toàn</strong>
            <small className="login-route-transition-syncing">
              Đang khóa không gian làm việc của {transition.accountName}
            </small>
            <small className="login-route-transition-synced">Phiên đã được bảo vệ · Hẹn gặp lại</small>
            <span className="login-route-transition-progress" aria-hidden="true"><i /></span>
          </motion.div>

          <motion.span
            className="login-route-transition-avatar-snapshot"
            style={{
              backgroundImage: transition.avatar.backgroundImage,
              backgroundColor: transition.avatar.backgroundColor,
              color: transition.avatar.color,
              fontFamily: transition.avatar.fontFamily,
              fontSize: transition.avatar.fontSize,
              fontWeight: transition.avatar.fontWeight,
            }}
            initial={{ opacity: 1, scale: 1 }}
            animate={transition.phase === "covering"
              ? { opacity: [1, 0.28, 0], scale: [1, 0.92, 0.84] }
              : { opacity: 0, scale: 0.84 }}
            transition={{ duration: 0.38, ease: AVATAR_TO_COVER_EASE, times: [0, 0.58, 1] }}
            aria-hidden="true"
          >
            {transition.avatar.imageSrc
              ? <img src={transition.avatar.imageSrc} alt="" />
              : transition.avatar.label}
          </motion.span>
        </motion.div>

        <span className="login-route-transition-frame login-route-transition-frame--top" aria-hidden="true" />
        <span className="login-route-transition-frame login-route-transition-frame--bottom" aria-hidden="true" />
      </div>
    </MotionConfig>
  );
}
