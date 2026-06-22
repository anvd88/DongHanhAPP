import { useEffect, useState } from "react";
import { Droplet, Power, ShieldCheck } from "lucide-react";
import { GlassCard } from "../components/Glass";
import { PageHeader } from "../components/Layout";
import { Badge } from "../components/ui";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "../shadcn/tooltip";
import { useAuth } from "../lib/auth";
import {
  isWaterReminderEnabled,
  setWaterReminderEnabled,
  subscribeWaterReminderEnabled,
} from "../lib/waterReminderClock";
import "./system-settings.css";

export function SystemSettings() {
  const { user } = useAuth();
  const [waterEnabled, setWaterEnabled] = useState(() => (user ? isWaterReminderEnabled(user.id) : true));

  useEffect(() => {
    if (!user) return;

    setWaterEnabled(isWaterReminderEnabled(user.id));
    return subscribeWaterReminderEnabled(user.id, () => {
      setWaterEnabled(isWaterReminderEnabled(user.id));
    });
  }, [user]);

  const toggleWaterReminder = () => {
    if (!user) return;

    const next = !waterEnabled;
    setWaterReminderEnabled(user.id, next);
    setWaterEnabled(next);
  };

  return (
    <div className="system-settings-page">
      <PageHeader
        title="Hệ thống"
        subtitle="Cài đặt thông báo và tuỳ chọn trải nghiệm web"
      />

      <section className="grid gap-4">
        <GlassCard className="system-settings-card p-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex min-w-0 gap-4">
              <div className="system-settings-icon">
                <Droplet className="h-7 w-7" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-lg font-black text-[var(--text)]">Nhắc nhở uống nước</h2>
                  <Badge color={waterEnabled ? "success" : "muted"}>
                    {waterEnabled ? "Đang bật" : "Đang tắt"}
                  </Badge>
                  <TooltipProvider delayDuration={120}>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <button className="system-rules-hint" type="button" aria-label="Quy tắc hoạt động">
                          <ShieldCheck className="h-4 w-4" />
                          <span>Quy tắc hoạt động</span>
                        </button>
                      </TooltipTrigger>
                      <TooltipContent side="bottom" align="start" className="system-rules-tooltip">
                        Cài đặt này chỉ điều khiển popup nhắc uống nước trên web. Khi tắt, các lần nhắc tới hạn sẽ không hiện.
                        Khi bật lại, hệ thống tiếp tục dựa trên mốc đăng nhập đầu tiên của ngày hiện tại.
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </div>
              </div>
            </div>

            <button
              className={`water-toggle ${waterEnabled ? "is-on" : ""}`}
              type="button"
              role="switch"
              aria-checked={waterEnabled}
              onClick={toggleWaterReminder}
            >
              <span className="water-toggle-icon">
                <Power className="h-4 w-4" />
              </span>
              <span className="water-toggle-track">
                <span className="water-toggle-thumb" />
              </span>
            </button>
          </div>

        </GlassCard>
      </section>
    </div>
  );
}
