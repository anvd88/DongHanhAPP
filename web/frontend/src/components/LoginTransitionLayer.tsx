import { useLayoutEffect, useState, type CSSProperties } from "react";
import { Check, Sparkles } from "lucide-react";
import { MotionConfig, motion } from "motion/react";
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

const COVER_EASE = [0.16, 1, 0.3, 1] as const;
const REVEAL_EASE = [0.76, 0, 0.24, 1] as const;
const COVER_DURATION_SECONDS = 0.9;
const REVEAL_DURATION_SECONDS = 0.82;

type AvatarTarget = {
  left: number;
  top: number;
  width: number;
  height: number;
  imageSrc: string | null;
  label: string;
  backgroundImage: string;
  backgroundColor: string;
  color: string;
  fontFamily: string;
  fontSize: string;
  fontWeight: string;
};

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

export function LoginTransitionLayer({
  transition,
  onCoverComplete,
  onRevealComplete,
}: {
  transition: LoginTransitionState | null;
  onCoverComplete: () => void;
  onRevealComplete: () => void;
}) {
  const [avatarTarget, setAvatarTarget] = useState<AvatarTarget | null>(null);

  useLayoutEffect(() => {
    if (!transition) {
      setAvatarTarget(null);
      return;
    }
    if (transition.phase === "covering") return;

    let animationFrame = 0;
    const measureAvatar = () => {
      const avatar = document.querySelector<HTMLElement>("[data-login-avatar-target='true']");
      if (!avatar) return;
      const rect = avatar.getBoundingClientRect();
      if (rect.width < 12 || rect.height < 12) return;
      const avatarImage = avatar.querySelector<HTMLImageElement>("img");
      const avatarStyle = window.getComputedStyle(avatar);
      const measured: AvatarTarget = {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
        imageSrc: avatarImage?.currentSrc || avatarImage?.src || null,
        label: avatarImage ? "" : avatar.textContent?.trim() || "?",
        backgroundImage: avatarStyle.backgroundImage,
        backgroundColor: avatarStyle.backgroundColor,
        color: avatarStyle.color,
        fontFamily: avatarStyle.fontFamily,
        fontSize: avatarStyle.fontSize,
        fontWeight: avatarStyle.fontWeight,
      };
      setAvatarTarget((current) => (
        current
        && Math.abs(current.left - measured.left) < 0.5
        && Math.abs(current.top - measured.top) < 0.5
        && Math.abs(current.width - measured.width) < 0.5
        && Math.abs(current.height - measured.height) < 0.5
        && current.imageSrc === measured.imageSrc
        && current.label === measured.label
          ? current
          : measured
      ));
    };
    const scheduleMeasure = () => {
      window.cancelAnimationFrame(animationFrame);
      animationFrame = window.requestAnimationFrame(measureAvatar);
    };
    const observer = new MutationObserver(scheduleMeasure);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener("resize", scheduleMeasure, { passive: true });
    scheduleMeasure();

    return () => {
      observer.disconnect();
      window.removeEventListener("resize", scheduleMeasure);
      window.cancelAnimationFrame(animationFrame);
    };
  }, [transition?.id, transition?.phase]);

  if (!transition) return null;

  const viewportWidth = typeof window === "undefined" ? 1440 : window.innerWidth;
  const viewportHeight = typeof window === "undefined" ? 900 : window.innerHeight;
  const hasVisibleOrigin = transition.origin.width >= 24 && transition.origin.height >= 20;
  const originX = hasVisibleOrigin
    ? clamp(transition.origin.left + transition.origin.width / 2, 0, viewportWidth)
    : viewportWidth / 2;
  const originY = hasVisibleOrigin
    ? clamp(transition.origin.top + transition.origin.height / 2, 0, viewportHeight)
    : viewportHeight / 2;
  const startRadius = hasVisibleOrigin
    ? Math.max(26, Math.hypot(transition.origin.width, transition.origin.height) / 2)
    : 34;
  const coverRadius = Math.ceil(Math.max(
    Math.hypot(originX, originY),
    Math.hypot(viewportWidth - originX, originY),
    Math.hypot(originX, viewportHeight - originY),
    Math.hypot(viewportWidth - originX, viewportHeight - originY),
  )) + 72;

  // Đích thật là vòng tròn avatar vừa được dựng trong Header. Fallback vẫn nằm trên vùng điều
  // hướng để các route đặc biệt không có Header không làm đứt chuyển cảnh.
  const fallbackRevealX = viewportWidth >= 1024 ? Math.round(viewportWidth * 0.56) : Math.round(viewportWidth * 0.5);
  const fallbackRevealY = Math.round(clamp(viewportHeight * 0.14, 72, 128));
  const revealX = avatarTarget
    ? avatarTarget.left + avatarTarget.width / 2
    : fallbackRevealX;
  const revealY = avatarTarget
    ? avatarTarget.top + avatarTarget.height / 2
    : fallbackRevealY;
  const avatarFrameWidth = avatarTarget?.width ?? 48;
  const avatarFrameHeight = avatarTarget?.height ?? 48;
  const centerX = viewportWidth / 2;
  const centerY = viewportHeight / 2;
  const statusWidth = Math.min(390, viewportWidth - 40);
  const statusHeight = viewportWidth <= 600 ? 268 : 286;
  const routeX = centerX - originX;
  const routeY = centerY - originY;
  const routeLength = Math.max(1, Math.hypot(routeX, routeY));
  const routeAngle = Math.atan2(routeY, routeX) * (180 / Math.PI);
  const beaconSize = clamp(Math.min(
    transition.origin.width || 52,
    transition.origin.height || 52,
  ), 52, 96);

  const isRevealing = transition.phase === "revealing";
  const surfaceTarget = isRevealing
    ? {
        clipPath: `circle(0px at ${revealX}px ${revealY}px)`,
        opacity: 0.98,
      }
    : {
        clipPath: `circle(${coverRadius}px at ${originX}px ${originY}px)`,
        opacity: 1,
      };
  const surfaceTransition = transition.phase === "covering"
    ? { duration: COVER_DURATION_SECONDS, ease: COVER_EASE }
    : transition.phase === "revealing"
      ? { duration: REVEAL_DURATION_SECONDS, ease: REVEAL_EASE }
      : { duration: 0 };

  const statusTarget = transition.phase === "revealing"
    ? {
        x: revealX - centerX,
        y: revealY - centerY,
        width: avatarFrameWidth,
        height: avatarFrameHeight,
        borderRadius: Math.max(avatarFrameWidth, avatarFrameHeight) / 2,
        opacity: 1,
        scale: 1,
        filter: "blur(0px)",
      }
    : {
        x: 0,
        y: 0,
        width: statusWidth,
        height: statusHeight,
        borderRadius: viewportWidth <= 600 ? 26 : 30,
        opacity: 1,
        scale: 1,
        filter: "blur(0px)",
      };
  const statusTransition = transition.phase === "covering"
    ? { duration: 0.58, delay: 0.24, ease: COVER_EASE }
    : transition.phase === "revealing"
      ? { duration: 0.76, ease: REVEAL_EASE }
      : { duration: 0.18, ease: COVER_EASE };

  const layerStyle = {
    "--login-route-origin-x": `${originX}px`,
    "--login-route-origin-y": `${originY}px`,
    "--login-route-reveal-x": `${revealX}px`,
    "--login-route-reveal-y": `${revealY}px`,
  } as CSSProperties;

  return (
    <MotionConfig reducedMotion="never">
      <div
        key={transition.id}
        className="login-route-transition"
        data-phase={transition.phase}
        style={layerStyle}
        role="status"
        aria-live="polite"
        aria-atomic="true"
        aria-label={isRevealing
          ? "Đăng nhập hoàn tất, đang mở ứng dụng"
          : "Đăng nhập thành công, đang chuẩn bị không gian làm việc"}
      >
        <motion.div
          className="login-route-transition-curtain"
          initial={{
            clipPath: `circle(${startRadius}px at ${originX}px ${originY}px)`,
            opacity: 1,
          }}
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
          style={{
            left: originX,
            top: originY,
            width: routeLength,
            rotate: routeAngle,
          }}
          initial={{ opacity: 0, scaleX: 0 }}
          animate={transition.phase === "covering"
            ? { opacity: [0, 0.72, 0], scaleX: [0, 1, 1] }
            : { opacity: 0, scaleX: 1 }}
          transition={{ duration: 0.7, delay: 0.08, ease: COVER_EASE, times: [0, 0.54, 1] }}
          aria-hidden="true"
        />

        <motion.span
          className="login-route-transition-beacon"
          style={{ left: originX, top: originY, width: beaconSize, height: beaconSize }}
          initial={{ opacity: 0, scale: 0.72 }}
          animate={transition.phase === "covering"
            ? { opacity: [0, 1, 0.62, 0], scale: [0.72, 1, 1.72, 2.36] }
            : { opacity: 0, scale: 2.36 }}
          transition={{ duration: 0.76, ease: COVER_EASE, times: [0, 0.18, 0.58, 1] }}
          aria-hidden="true"
        />

        <motion.div
          className="login-route-transition-status"
          initial={{
            x: originX - centerX,
            y: originY - centerY,
            opacity: 0,
            scale: 0.64,
            filter: "blur(12px)",
            width: statusWidth,
            height: statusHeight,
            borderRadius: viewportWidth <= 600 ? 26 : 30,
          }}
          animate={statusTarget}
          transition={statusTransition}
        >
          <motion.div
            className="login-route-transition-status-content"
            animate={isRevealing
              ? { opacity: 0, scale: 0.68, filter: "blur(8px)" }
              : { opacity: 1, scale: 1, filter: "blur(0px)" }}
            transition={isRevealing
              ? { duration: 0.3, ease: [0.55, 0, 1, 0.45] }
              : { duration: 0.18, ease: COVER_EASE }}
          >
            <span className="login-route-transition-orbit" aria-hidden="true"><i /><i /></span>
            <span className="login-route-transition-check" aria-hidden="true">
              <Check />
            </span>
            <span className="login-route-transition-kicker"><Sparkles /> Phiên làm việc an toàn</span>
            <strong>Đăng nhập thành công</strong>
            <small className="login-route-transition-syncing">Đang mở không gian làm việc của bạn</small>
            <small className="login-route-transition-synced">Mọi thứ đã sẵn sàng</small>
            <span className="login-route-transition-progress" aria-hidden="true"><i /></span>
          </motion.div>
          <motion.span
            className="login-route-transition-avatar-snapshot"
            style={{
              backgroundImage: avatarTarget?.backgroundImage,
              backgroundColor: avatarTarget?.backgroundColor,
              color: avatarTarget?.color,
              fontFamily: avatarTarget?.fontFamily,
              fontSize: avatarTarget?.fontSize,
              fontWeight: avatarTarget?.fontWeight,
            }}
            animate={isRevealing && avatarTarget
              ? { opacity: [0, 0, 1], scale: [0.72, 0.86, 1] }
              : { opacity: 0, scale: 0.72 }}
            transition={isRevealing
              ? { duration: 0.76, ease: REVEAL_EASE, times: [0, 0.34, 1] }
              : { duration: 0 }}
            aria-hidden="true"
          >
            {avatarTarget?.imageSrc
              ? <img src={avatarTarget.imageSrc} alt="" />
              : avatarTarget?.label}
          </motion.span>
        </motion.div>

        <span className="login-route-transition-frame login-route-transition-frame--top" aria-hidden="true" />
        <span className="login-route-transition-frame login-route-transition-frame--bottom" aria-hidden="true" />
      </div>
    </MotionConfig>
  );
}
