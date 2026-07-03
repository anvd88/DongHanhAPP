import { Wrench } from "lucide-react";
import { GlassCard } from "../components/Glass";

export function StubPage({ title }: { title: string }) {
  return (
    <div>
      <h1 className="mb-5 text-2xl font-bold tracking-tight text-[var(--text)]">{title}</h1>
      <GlassCard className="flex flex-col items-center justify-center gap-3 py-20 text-center">
        <div
          className="flex h-16 w-16 items-center justify-center rounded-3xl"
          style={{ background: "var(--accent-soft)", color: "var(--accent)" }}
        >
          <Wrench className="h-7 w-7" />
        </div>
        <p className="text-lg font-bold text-[var(--text)]">Module đang phát triển</p>
        <p className="max-w-sm text-sm text-[var(--text-secondary)]">
          Chức năng "{title}" sẽ sớm có mặt trên bản web. Trên bản desktop, module này cũng đang được hoàn thiện.
        </p>
      </GlassCard>
    </div>
  );
}
