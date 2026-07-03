import { Heart, History, LogOut, Maximize2, Minus, Send, X } from "lucide-react";
import "@fontsource/be-vietnam-pro/400.css";
import "@fontsource/be-vietnam-pro/500.css";
import "@fontsource/be-vietnam-pro/600.css";
import "@fontsource/be-vietnam-pro/700.css";
import "./FarewellPage.css";

function WindowControls() {
  return (
    <div className="farewell-window-controls" aria-hidden="true">
      <span><Minus /></span>
      <span><Maximize2 /></span>
      <span><X /></span>
    </div>
  );
}

function MemoryOrb({ mobile = false }: { mobile?: boolean }) {
  return (
    <div className={mobile ? "farewell-memory-orb farewell-memory-orb-mobile" : "farewell-memory-orb"}>
      <div className="farewell-orb-glow" />
      <Send className="farewell-plane" aria-hidden="true" />
      <span className="farewell-orb-sheen" />
    </div>
  );
}

function FarewellActions() {
  return (
    <div className="farewell-actions" aria-label="Hành động">
      <button type="button" className="farewell-action-button">
        <span className="farewell-action-icon">
          <History />
        </span>
        <span>
          <strong>Ôn lại kỷ niệm</strong>
          <small>Nhìn lại hành trình</small>
        </span>
      </button>
      <button type="button" className="farewell-action-button">
        <span className="farewell-action-icon">
          <LogOut />
        </span>
        <span>
          <strong>Thoát ứng dụng</strong>
          <small>Hẹn gặp lại bạn</small>
        </span>
      </button>
    </div>
  );
}

export function FarewellPage() {
  return (
    <main className="farewell-page" aria-label="Màn hình chào tạm biệt ứng dụng">
      <div className="farewell-scene" aria-hidden="true">
        <div className="farewell-stars" />
        <div className="farewell-cloud farewell-cloud-left" />
        <div className="farewell-cloud farewell-cloud-right" />
        <div className="farewell-mountains farewell-mountains-back" />
        <div className="farewell-mountains farewell-mountains-front" />
        <div className="farewell-lake" />
        <div className="farewell-light-trail" />
        <div className="farewell-bubble farewell-bubble-one" />
        <div className="farewell-bubble farewell-bubble-two" />
        <div className="farewell-bubble farewell-bubble-three" />
        <div className="farewell-bubble farewell-bubble-four" />
      </div>

      <section className="farewell-window">
        <div className="farewell-titlebar">
          <div className="farewell-brand">
            <span className="farewell-brand-icon">
              <Heart />
            </span>
            <span>Cảm ơn bạn</span>
          </div>
          <WindowControls />
        </div>

        <button type="button" className="farewell-mobile-close" aria-label="Đóng">
          <X />
        </button>

        <div className="farewell-body">
          <section className="farewell-copy">
            <p className="farewell-kicker">Cảm ơn bạn đã đồng hành cùng chúng tôi</p>
            <h1>Tạm biệt!</h1>

            <MemoryOrb mobile />

            <p className="farewell-intro">
              Mỗi hành trình đều để lại những dấu ấn riêng.
              <br />
              Có những điều khép lại không phải để mất đi,
              <br />
              mà để trở thành những ký ức đẹp nhất trong tim.
            </p>

            <div className="farewell-quote-card">
              <span className="farewell-quote-mark" aria-hidden="true">“</span>
              <p className="farewell-main-message">
                Có những hành trình không thực sự kết thúc,
                <br />
                chúng chỉ được chúng ta cất giữ
                <br />
                <em>trong những miền ký ức đẹp nhất.</em>
              </p>
              <span className="farewell-divider">
                <i />
                <Heart />
                <i />
              </span>
              <p className="farewell-support-message">
                <span className="farewell-mobile-text">
                  Cảm ơn bạn vì đã đồng hành cùng chúng tôi.
                  <br />
                  Hy vọng những khoảnh khắc đã qua sẽ luôn là một phần ký ức đẹp trong lòng bạn.
                </span>
                <span className="farewell-desktop-text">
                  Cảm ơn bạn vì đã trở thành một phần trong hành trình của chúng tôi.
                  <br />
                  Chúc bạn luôn bình an và vững bước trên những chặng đường phía trước.
                </span>
              </p>
            </div>
          </section>

          <section className="farewell-visual" aria-hidden="true">
            <MemoryOrb />
          </section>
        </div>

        <FarewellActions />

        <p className="farewell-signoff">
          <Heart />
          <span className="farewell-mobile-text">Hẹn gặp lại bạn trong một hành trình mới.</span>
          <span className="farewell-desktop-text">Hẹn gặp lại bạn!</span>
        </p>
      </section>
    </main>
  );
}
