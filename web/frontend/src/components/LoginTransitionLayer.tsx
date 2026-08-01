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

type CssVarStyle = CSSProperties & Record<`--${string}`, string>;

// Chuyển cảnh sau đăng nhập: dựng sẵn CÁC KHỐI CỦA CHÍNH MÀN HÌNH SẮP HIỆN RA (thanh menu, thanh
// trên, các thẻ nội dung). Chúng bung ra từ điểm khởi phát (nút Đăng nhập hoặc khung mã QR) rồi hạ
// đúng chỗ chúng sẽ nằm trong ứng dụng; lúc mở màn thì chỉ tan tại chỗ, nên giao diện thật hiện lên
// như thể chính những khối đó vừa đông cứng lại. Mốc hết pha lấy từ animationend của khối kết thúc
// muộn nhất (đánh dấu data-cover-clock / data-reveal-clock).
const BLOCK_PLACE_MS = 460;
const BLOCK_FADE_MS = 360;
const COVER_BASE_DELAY_MS = 80;
const COVER_SPREAD_MS = 300;
const REVEAL_SPREAD_MS = 220;
// Khối ở xa không bay quá dài, nếu không lúc bắt đầu nó đã nằm ngoài màn hình và người dùng chỉ
// thấy nó "hiện ra" chứ không thấy nó được xếp vào.
const MAX_BLOCK_TRAVEL_PX = 420;

type BlockKind = "rail" | "bar" | "card" | "dock";

type BlockRect = {
  key: string;
  kind: BlockKind;
  left: number;
  top: number;
  width: number;
  height: number;
  radius: number;
  /** Số vạch dựng sẵn bên trong khối (icon menu, ô tìm kiếm, dòng chữ giả…). */
  parts: number;
};

/*
 * Số đo lấy đúng theo vỏ ứng dụng thật trong index.css: .km-app-shell (padding 6px 8px, gap 8px),
 * .km-sidebar-rail (70px), .km-header (min-height 60px, 58px ở ≤760px; bán kính 14px, 18px ở ≤760px),
 * .km-page (padding 0 10px 12px) và .km-mobile-bottom-nav (62px, 58px ở ≤380px).
 * Sửa layout thật thì phải sửa cả bảng này, không thì khối dựng sẵn sẽ lệch so với giao diện hiện ra.
 */
function shellMetrics(width: number) {
  if (width >= 1024) {
    return {
      railWidth: 70,
      railRadius: 10,
      shellPadX: 8,
      shellPadY: 6,
      gap: 8,
      barHeight: 60,
      barRadius: 14,
      pageInsetX: 10,
      pageBottom: 18,
      dockHeight: 0,
      dockInset: 0,
      dockRadius: 0,
      cardRadius: 16,
      columns: 4,
    };
  }
  if (width > 760) {
    return {
      railWidth: 0,
      railRadius: 0,
      shellPadX: 12,
      shellPadY: 12,
      gap: 12,
      barHeight: 60,
      barRadius: 14,
      pageInsetX: 0,
      pageBottom: 0,
      dockHeight: 62,
      dockInset: 10,
      dockRadius: 22,
      cardRadius: 16,
      columns: 2,
    };
  }
  const narrow = width <= 380;
  return {
    railWidth: 0,
    railRadius: 0,
    shellPadX: 10,
    shellPadY: 8,
    gap: 12,
    barHeight: 58,
    barRadius: 18,
    pageInsetX: 0,
    pageBottom: 0,
    dockHeight: narrow ? 58 : 62,
    dockInset: narrow ? 6 : 10,
    dockRadius: narrow ? 18 : 22,
    cardRadius: 14,
    columns: 2,
  };
}

function travel(distance: number) {
  return Math.max(-MAX_BLOCK_TRAVEL_PX, Math.min(MAX_BLOCK_TRAVEL_PX, Math.round(distance)));
}

function buildScreenBlocks(viewportWidth: number, viewportHeight: number): BlockRect[] {
  const metrics = shellMetrics(viewportWidth);
  const blocks: BlockRect[] = [];

  if (metrics.railWidth > 0) {
    blocks.push({
      key: "rail",
      kind: "rail",
      left: metrics.shellPadX,
      top: metrics.shellPadY,
      width: metrics.railWidth,
      height: Math.max(120, viewportHeight - metrics.shellPadY * 2),
      radius: metrics.railRadius,
      parts: 6,
    });
  }

  const barLeft = metrics.railWidth > 0
    ? metrics.shellPadX + metrics.railWidth + metrics.gap
    : metrics.shellPadX;
  blocks.push({
    key: "bar",
    kind: "bar",
    left: barLeft,
    top: metrics.shellPadY,
    width: Math.max(160, viewportWidth - barLeft - metrics.shellPadX),
    height: metrics.barHeight,
    radius: metrics.barRadius,
    parts: 3,
  });

  if (metrics.dockHeight > 0) {
    blocks.push({
      key: "dock",
      kind: "dock",
      left: metrics.dockInset,
      top: viewportHeight - metrics.dockInset - metrics.dockHeight,
      width: Math.max(160, viewportWidth - metrics.dockInset * 2),
      height: metrics.dockHeight,
      radius: metrics.dockRadius,
      parts: 5,
    });
  }

  const contentLeft = barLeft + metrics.pageInsetX;
  const contentRight = metrics.shellPadX + metrics.pageInsetX;
  const contentTop = metrics.shellPadY + metrics.barHeight + metrics.gap;
  const contentBottom = metrics.dockHeight > 0
    ? metrics.dockInset + metrics.dockHeight + 10
    : metrics.pageBottom;
  const contentWidth = Math.max(160, viewportWidth - contentLeft - contentRight);
  const contentHeight = Math.max(150, viewportHeight - contentTop - contentBottom);
  const cardGap = 10;

  // Hàng thẻ số ở trên + khối nội dung chính ở dưới: đó là dáng chung của các trang trong app.
  // Không cần trùng từng trang — đây là bộ khung, đúng tinh thần skeleton lúc trang đang tải.
  const statHeight = Math.max(62, Math.min(104, Math.round(contentHeight * 0.18)));
  const statWidth = (contentWidth - cardGap * (metrics.columns - 1)) / metrics.columns;
  for (let index = 0; index < metrics.columns; index += 1) {
    blocks.push({
      key: `stat-${index}`,
      kind: "card",
      left: Math.round(contentLeft + index * (statWidth + cardGap)),
      top: contentTop,
      width: Math.round(statWidth),
      height: statHeight,
      radius: metrics.cardRadius,
      parts: 2,
    });
  }

  const bodyTop = contentTop + statHeight + cardGap;
  const bodyHeight = contentHeight - statHeight - cardGap;
  if (bodyHeight >= 110) {
    if (metrics.columns >= 4) {
      const mainWidth = Math.round((contentWidth - cardGap) * 0.64);
      blocks.push({
        key: "main",
        kind: "card",
        left: contentLeft,
        top: bodyTop,
        width: mainWidth,
        height: bodyHeight,
        radius: metrics.cardRadius,
        parts: 3,
      });
      blocks.push({
        key: "side",
        kind: "card",
        left: contentLeft + mainWidth + cardGap,
        top: bodyTop,
        width: contentWidth - mainWidth - cardGap,
        height: bodyHeight,
        radius: metrics.cardRadius,
        parts: 4,
      });
    } else {
      const mainHeight = bodyHeight >= 260 ? Math.round((bodyHeight - cardGap) * 0.56) : bodyHeight;
      blocks.push({
        key: "main",
        kind: "card",
        left: contentLeft,
        top: bodyTop,
        width: contentWidth,
        height: mainHeight,
        radius: metrics.cardRadius,
        parts: 3,
      });
      if (mainHeight < bodyHeight) {
        blocks.push({
          key: "list",
          kind: "card",
          left: contentLeft,
          top: bodyTop + mainHeight + cardGap,
          width: contentWidth,
          height: bodyHeight - mainHeight - cardGap,
          radius: metrics.cardRadius,
          parts: 4,
        });
      }
    }
  }

  return blocks;
}

function layoutBlocks(
  viewportWidth: number,
  viewportHeight: number,
  originCenterX: number,
  originCenterY: number,
) {
  const screenCenterX = viewportWidth / 2;
  const screenCenterY = viewportHeight / 2;
  const measured = buildScreenBlocks(viewportWidth, viewportHeight).map((rect) => {
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2;
    return {
      rect,
      centerX,
      centerY,
      fromOrigin: Math.hypot(centerX - originCenterX, centerY - originCenterY),
      fromScreenCenter: Math.hypot(centerX - screenCenterX, centerY - screenCenterY),
    };
  });

  const farthestFromOrigin = Math.max(1, ...measured.map((item) => item.fromOrigin));
  const farthestFromScreenCenter = Math.max(1, ...measured.map((item) => item.fromScreenCenter));

  let lastCoverDelay = -1;
  let lastRevealDelay = -1;
  let coverClockKey = "";
  let revealClockKey = "";

  const blocks = measured.map((item, index) => {
    // Xếp vào: khối gần điểm khởi phát hạ trước. Mở màn: khối giữa màn hình tan trước rồi lan ra,
    // để mắt bắt được nội dung thật ở trung tâm sớm nhất.
    const coverDelay = Math.round(
      COVER_BASE_DELAY_MS + (item.fromOrigin / farthestFromOrigin) * COVER_SPREAD_MS,
    );
    const revealDelay = Math.round(
      (item.fromScreenCenter / farthestFromScreenCenter) * REVEAL_SPREAD_MS,
    );
    // Mọi khối chạy cùng thời lượng, nên khối vào muộn nhất cũng là khối xong muộn nhất.
    if (coverDelay > lastCoverDelay) {
      lastCoverDelay = coverDelay;
      coverClockKey = item.rect.key;
    }
    if (revealDelay > lastRevealDelay) {
      lastRevealDelay = revealDelay;
      revealClockKey = item.rect.key;
    }
    const spin = (index % 2 === 0 ? 1 : -1) * (1.4 + (index % 3) * 0.7);
    const style: CssVarStyle = {
      "--block-x": `${Math.round(item.rect.left)}px`,
      "--block-y": `${Math.round(item.rect.top)}px`,
      "--block-w": `${Math.round(item.rect.width)}px`,
      "--block-h": `${Math.round(item.rect.height)}px`,
      "--block-radius": `${item.rect.radius}px`,
      "--block-cover-delay": `${coverDelay}ms`,
      "--block-reveal-delay": `${revealDelay}ms`,
      "--block-enter-x": `${travel((originCenterX - item.centerX) * 0.5)}px`,
      "--block-enter-y": `${travel((originCenterY - item.centerY) * 0.5)}px`,
      "--block-spin": `${spin.toFixed(2)}deg`,
    };
    return { ...item.rect, style };
  });

  return blocks.map((block) => ({
    ...block,
    coverClock: block.key === coverClockKey,
    revealClock: block.key === revealClockKey,
  }));
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
  if (!transition) return null;

  const viewportWidth = typeof window === "undefined" ? 1440 : window.innerWidth;
  const viewportHeight = typeof window === "undefined" ? 900 : window.innerHeight;
  // Không có phần tử khởi phát thật (đăng nhập từ ứng dụng Android) thì bung ra từ giữa màn hình.
  const hasOrigin = transition.origin.width >= 40 && transition.origin.height >= 24;
  const originCenterX = hasOrigin
    ? Math.min(viewportWidth, Math.max(0, transition.origin.left + transition.origin.width / 2))
    : viewportWidth / 2;
  const originCenterY = hasOrigin
    ? Math.min(viewportHeight, Math.max(0, transition.origin.top + transition.origin.height / 2))
    : viewportHeight / 2;
  const blocks = layoutBlocks(viewportWidth, viewportHeight, originCenterX, originCenterY);
  const style: CssVarStyle = {
    "--login-transition-block-place": `${BLOCK_PLACE_MS}ms`,
    "--login-transition-block-fade": `${BLOCK_FADE_MS}ms`,
  };

  // animationend của các khối nổi bọt lên lớp bọc; chỉ khối được đánh dấu là khối cuối mới báo hết
  // pha. Bỏ qua pseudo-element vì vệt loading có nhịp riêng.
  const handleStageAnimationEnd = (event: AnimationEvent<HTMLDivElement>) => {
    if (event.pseudoElement) return;
    const block = event.target as HTMLElement | null;
    if (
      transition.phase === "covering"
      && event.animationName === "login-route-block-place"
      && block?.dataset.coverClock === "true"
    ) {
      onCoverComplete();
    }
    if (
      transition.phase === "revealing"
      && event.animationName === "login-route-block-dissolve"
      && block?.dataset.revealClock === "true"
    ) {
      onRevealComplete();
    }
  };

  return (
    <div
      key={transition.id}
      className="login-route-transition"
      data-phase={transition.phase}
      style={style}
      role="status"
      aria-live="polite"
      aria-atomic="true"
      aria-label={
        transition.phase === "revealing"
          ? "Đồng bộ hoàn tất, đang mở ứng dụng"
          : "Đăng nhập thành công, đang dựng không gian làm việc"
      }
    >
      <div
        className="login-route-transition-stage"
        onAnimationEnd={handleStageAnimationEnd}
        aria-hidden="true"
      >
        {blocks.map((block) => (
          <span
            key={block.key}
            className="login-route-transition-block"
            data-kind={block.kind}
            data-cover-clock={block.coverClock ? "true" : undefined}
            data-reveal-clock={block.revealClock ? "true" : undefined}
            style={block.style}
          >
            {Array.from({ length: block.parts }, (_, part) => <i key={part} />)}
          </span>
        ))}
      </div>

      <span className="login-route-transition-status">
        <span className="login-route-transition-check" aria-hidden="true">
          <CheckCircle2 />
        </span>
        <strong>Đăng nhập thành công</strong>
        <small className="login-route-transition-syncing">
          Đang dựng không gian làm việc của bạn
        </small>
        <small className="login-route-transition-synced">
          Đã sẵn sàng · Đang mở ứng dụng
        </small>
        <span className="login-route-transition-progress" aria-hidden="true"><i /></span>
      </span>
    </div>
  );
}
