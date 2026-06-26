import { useMemo, useState, type ChangeEvent, type ReactNode } from "react";
import { Check, Copy, Gauge, RefreshCcw, Ruler, Scale, Wrench, X } from "lucide-react";
import {
  calculateStainlessSteelCoil,
  standardWidthOptions,
  stainlessSteelDensity,
  thicknessOptions,
  type CoilCalculationInput,
  type StainlessSteelType,
} from "../lib/stainlessSteelCoil";
import "./quick-tools-drawer.css";

interface ToolForm {
  thicknessMm: string;
  widthMm: string;
  steelType: StainlessSteelType;
  massKg: string;
  innerDiameterCm: string;
  packingFactor: string;
}

const defaultToolForm: ToolForm = {
  thicknessMm: "1.2",
  widthMm: "1200",
  steelType: "inox304",
  massKg: "1000",
  innerDiameterCm: "50",
  packingFactor: "1",
};

const number2 = new Intl.NumberFormat("vi-VN", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const number1 = new Intl.NumberFormat("vi-VN", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const integer = new Intl.NumberFormat("vi-VN", {
  maximumFractionDigits: 0,
});

function parseNumber(value: string): number {
  return Number(value.replace(",", "."));
}

function cmFromMm(valueMm: number): string {
  return `${number2.format(valueMm / 10)} cm`;
}

function mm(valueMm: number): string {
  return `${number1.format(valueMm)} mm`;
}

function buildInput(form: ToolForm): CoilCalculationInput {
  return {
    thicknessMm: parseNumber(form.thicknessMm),
    widthMm: parseNumber(form.widthMm),
    steelType: form.steelType,
    customDensityKgM3: 7930,
    massTon: parseNumber(form.massKg) / 1000,
    innerDiameterMm: parseNumber(form.innerDiameterCm) * 10,
    packingFactor: parseNumber(form.packingFactor),
    baremMode: "table",
  };
}

function isInputValid(input: CoilCalculationInput): boolean {
  return (
    Number.isFinite(input.thicknessMm) &&
    Number.isFinite(input.widthMm) &&
    Number.isFinite(input.massTon) &&
    Number.isFinite(input.innerDiameterMm) &&
    Number.isFinite(input.packingFactor) &&
    input.thicknessMm > 0 &&
    input.widthMm > 0 &&
    input.massTon > 0 &&
    input.innerDiameterMm > 0 &&
    input.packingFactor >= 0.9 &&
    input.packingFactor <= 1
  );
}

export function QuickToolsDrawer() {
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<ToolForm>(defaultToolForm);
  const [copied, setCopied] = useState(false);
  const input = useMemo(() => buildInput(form), [form]);
  const result = useMemo(
    () => (isInputValid(input) ? calculateStainlessSteelCoil(input) : null),
    [input],
  );

  const updateField = (key: keyof ToolForm) => (event: ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
    setForm((current) => ({ ...current, [key]: event.target.value }));
    setCopied(false);
  };

  const reset = () => {
    setForm(defaultToolForm);
    setCopied(false);
  };

  const copyResult = async () => {
    if (!result) return;

    const text = [
      "SAN CUỘN INOX",
      `Độ dày: ${number1.format(parseNumber(form.thicknessMm))} mm`,
      `Khổ rộng: ${integer.format(parseNumber(form.widthMm))} mm`,
      `Khối lượng: ${number2.format(parseNumber(form.massKg))} kg`,
      `Lõi trong: ${number2.format(parseNumber(form.innerDiameterCm))} cm`,
      `Barem: ${number2.format(result.kgPerMeter)} kg/m`,
      `Chiều dài: ${number2.format(result.lengthM)} m`,
      `Đường kính ngoài: ${cmFromMm(result.outerDiameterMm)} (${mm(result.outerDiameterMm)})`,
      `Bề dày từ lõi ra ngoài: ${cmFromMm(result.radialBuildMm)} (${mm(result.radialBuildMm)})`,
    ].join("\n");

    await navigator.clipboard?.writeText(text);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  };

  return (
    <div className="quick-tools" data-open={open}>
      {open && (
        <button
          type="button"
          className="quick-tools-click-away"
          onClick={() => setOpen(false)}
          aria-label="Đóng công cụ"
        />
      )}

      <button
        type="button"
        className="quick-tools-handle"
        onClick={() => setOpen(true)}
        aria-label="Mở công cụ San cuộn"
      >
        <Wrench className="h-4 w-4" />
        <span>Công cụ</span>
      </button>

      <aside className="quick-tools-panel" aria-hidden={!open}>
        <div className="quick-tools-header">
          <div>
            <span className="quick-tools-eyebrow">Công cụ</span>
            <h2>San cuộn</h2>
          </div>
          <button type="button" className="quick-tools-icon-btn" onClick={() => setOpen(false)} aria-label="Đóng công cụ">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="quick-tools-content scroll-thin">
          <div className="quick-tools-result">
            <div>
              <span>Bề dày từ lõi ra ngoài</span>
              <strong>{result ? cmFromMm(result.radialBuildMm) : "--"}</strong>
              {result && <small>{mm(result.radialBuildMm)}</small>}
            </div>
            <Ruler className="h-7 w-7" />
          </div>

          <div className="quick-tools-fields">
            <ToolField label="Độ dày" unit="mm">
              <input
                list="quick-thickness-options"
                type="number"
                min="0.3"
                max="5"
                step="0.1"
                value={form.thicknessMm}
                onChange={updateField("thicknessMm")}
              />
              <datalist id="quick-thickness-options">
                {thicknessOptions.map((value) => (
                  <option key={value} value={value} />
                ))}
              </datalist>
            </ToolField>

            <ToolField label="Khổ rộng" unit="mm">
              <input type="number" min="0" step="1" value={form.widthMm} onChange={updateField("widthMm")} />
              <div className="quick-tools-chips">
                {standardWidthOptions.map((option) => (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => setForm((current) => ({ ...current, widthMm: String(option.value) }))}
                  >
                    {option.value}
                  </button>
                ))}
              </div>
            </ToolField>

            <ToolField label="Inox">
              <select value={form.steelType} onChange={updateField("steelType")}>
                {Object.entries(stainlessSteelDensity).map(([key, item]) => (
                  <option key={key} value={key}>
                    {item.label}
                  </option>
                ))}
              </select>
            </ToolField>

            <ToolField label="Khối lượng" unit="kg">
              <input type="number" min="0" step="1" value={form.massKg} onChange={updateField("massKg")} />
            </ToolField>

            <ToolField label="Đường kính lõi" unit="cm">
              <input type="number" min="0" step="0.1" value={form.innerDiameterCm} onChange={updateField("innerDiameterCm")} />
            </ToolField>

            <ToolField label="Độ chặt">
              <input
                type="number"
                min="0.9"
                max="1"
                step="0.01"
                value={form.packingFactor}
                onChange={updateField("packingFactor")}
              />
              <div className="quick-tools-chips">
                <button type="button" onClick={() => setForm((current) => ({ ...current, packingFactor: "1" }))}>
                  1.00
                </button>
                <button type="button" onClick={() => setForm((current) => ({ ...current, packingFactor: "0.98" }))}>
                  0.98
                </button>
                <button type="button" onClick={() => setForm((current) => ({ ...current, packingFactor: "0.95" }))}>
                  0.95
                </button>
              </div>
            </ToolField>
          </div>

          <div className="quick-tools-stats">
            <Stat icon={<Gauge className="h-4 w-4" />} label="Đường kính ngoài" value={result ? cmFromMm(result.outerDiameterMm) : "--"} />
            <Stat icon={<Scale className="h-4 w-4" />} label="Barem" value={result ? `${number2.format(result.kgPerMeter)} kg/m` : "--"} />
            <Stat icon={<Ruler className="h-4 w-4" />} label="Chiều dài" value={result ? `${number2.format(result.lengthM)} m` : "--"} />
            <Stat icon={<RefreshCcw className="h-4 w-4" />} label="Số vòng" value={result ? `${integer.format(Math.round(result.estimatedTurns))} vòng` : "--"} />
          </div>
        </div>

        <div className="quick-tools-footer">
          <button type="button" className="quick-tools-secondary" onClick={reset}>
            <RefreshCcw className="h-4 w-4" />
            Đặt lại
          </button>
          <button type="button" className="quick-tools-primary" onClick={copyResult} disabled={!result}>
            {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
            {copied ? "Đã sao chép" : "Sao chép"}
          </button>
        </div>
      </aside>
    </div>
  );
}

function ToolField({ label, unit, children }: { label: string; unit?: string; children: ReactNode }) {
  return (
    <label className="quick-tools-field">
      <span>
        {label}
        {unit && <small>{unit}</small>}
      </span>
      {children}
    </label>
  );
}

function Stat({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return (
    <div className="quick-tools-stat">
      <span>{icon}</span>
      <div>
        <small>{label}</small>
        <strong>{value}</strong>
      </div>
    </div>
  );
}
