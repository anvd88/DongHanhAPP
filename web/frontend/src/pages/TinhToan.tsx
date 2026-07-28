import { useMemo, useRef, useState, type ChangeEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import {
  Calculator,
  Check,
  Copy,
  Gauge,
  LogIn,
  RefreshCcw,
  Ruler,
  Scale,
} from "lucide-react";
import { APP_BRAND_NAME } from "../lib/branding";
import {
  calculateStainlessSteelCoil,
  resolveSteelDensity,
  standardWidthOptions,
  stainlessSteelDensity,
  thicknessOptions,
  type BaremMode,
  type CalcDirection,
  type CoilCalculationInput,
  type CoilCalculationResult,
  type StainlessSteelType,
} from "../lib/stainlessSteelCoil";
import { useTheme } from "../lib/theme";
import "./tinh-toan.css";

interface FormState {
  thicknessMm: string;
  widthMm: string;
  steelType: StainlessSteelType;
  customDensityKgM3: string;
  massTon: string;
  lengthM: string;
  innerDiameterMm: string;
  packingFactor: string;
  baremMode: BaremMode;
  calcDirection: CalcDirection;
}

type FieldKey = keyof Pick<
  FormState,
  "thicknessMm" | "widthMm" | "customDensityKgM3" | "massTon" | "lengthM" | "innerDiameterMm" | "packingFactor"
>;

type FieldErrors = Partial<Record<FieldKey, string>>;

const defaultForm: FormState = {
  thicknessMm: "1.2",
  widthMm: "1200",
  steelType: "inox304",
  customDensityKgM3: "7930",
  massTon: "1000",
  lengthM: "100",
  innerDiameterMm: "500",
  packingFactor: "1",
  baremMode: "table",
  calcDirection: "mass",
};

const decimalFormatter = new Intl.NumberFormat("vi-VN", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const oneDecimalFormatter = new Intl.NumberFormat("vi-VN", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const integerFormatter = new Intl.NumberFormat("vi-VN", {
  maximumFractionDigits: 0,
});

const densityFormatter = new Intl.NumberFormat("vi-VN", {
  maximumFractionDigits: 0,
});

function parseNumber(value: string): number {
  return Number(value.replace(",", "."));
}

function formatCmFromMm(valueMm: number): string {
  return `${decimalFormatter.format(valueMm / 10)} cm`;
}

function formatMm(valueMm: number): string {
  return `${oneDecimalFormatter.format(valueMm)} mm`;
}

function formatInputCmFromMm(value: string): string {
  if (!value.trim()) return "";

  const valueMm = parseNumber(value);
  return Number.isFinite(valueMm) ? String(valueMm / 10) : "";
}

function buildInput(form: FormState): CoilCalculationInput {
  return {
    thicknessMm: parseNumber(form.thicknessMm),
    widthMm: parseNumber(form.widthMm),
    steelType: form.steelType,
    customDensityKgM3: parseNumber(form.customDensityKgM3),
    massTon: parseNumber(form.massTon) / 1000,
    lengthInputM: parseNumber(form.lengthM),
    innerDiameterMm: parseNumber(form.innerDiameterMm),
    packingFactor: parseNumber(form.packingFactor),
    baremMode: form.baremMode,
    calcDirection: form.calcDirection,
  };
}

function validateForm(form: FormState): FieldErrors {
  const input = buildInput(form);
  const errors: FieldErrors = {};

  if (!Number.isFinite(input.thicknessMm) || input.thicknessMm <= 0) {
    errors.thicknessMm = "Độ dày inox phải lớn hơn 0 mm.";
  }

  if (!Number.isFinite(input.widthMm) || input.widthMm <= 0) {
    errors.widthMm = "Khổ rộng cuộn phải lớn hơn 0 mm.";
  }

  if (form.calcDirection === "length") {
    if (!Number.isFinite(input.lengthInputM) || input.lengthInputM <= 0) {
      errors.lengthM = "Chiều dài cuộn phải lớn hơn 0 m.";
    }
  } else if (!Number.isFinite(input.massTon) || input.massTon <= 0) {
    errors.massTon = "Khối lượng cần san phải lớn hơn 0 kg.";
  }

  if (!Number.isFinite(input.innerDiameterMm) || input.innerDiameterMm <= 0) {
    errors.innerDiameterMm = "Đường kính lõi trong phải lớn hơn 0 cm.";
  }

  if (!Number.isFinite(input.packingFactor) || input.packingFactor < 0.9 || input.packingFactor > 1) {
    errors.packingFactor = "Hệ số độ chặt phải nằm trong khoảng 0,90 đến 1,00.";
  }

  if (
    input.steelType === "custom" &&
    (!Number.isFinite(input.customDensityKgM3) || input.customDensityKgM3 <= 0)
  ) {
    errors.customDensityKgM3 = "Khối lượng riêng tùy chỉnh phải lớn hơn 0 kg/m³.";
  }

  return errors;
}

function getBaremSourceLabel(result: CoilCalculationResult): string {
  if (result.baremSource === "table") return "Barem theo bảng thực tế";
  if (result.baremSource === "densityFallback") return "Barem theo khối lượng riêng";
  return "Barem tính theo khối lượng riêng";
}

function formatOptional(value: number, formatter: Intl.NumberFormat): string {
  return Number.isFinite(value) ? formatter.format(value) : "-";
}

function createCopyText(form: FormState, result: CoilCalculationResult): string {
  return [
    "CHỈ SỐ CUỘN INOX",
    "",
    `Chủng loại: ${result.steelLabel}`,
    `Độ dày tấm: ${formatOptional(parseNumber(form.thicknessMm), oneDecimalFormatter)} mm`,
    `Khổ rộng: ${formatOptional(parseNumber(form.widthMm), integerFormatter)} mm`,
    `Khối lượng: ${formatOptional(result.massKg, decimalFormatter)} kg`,
    `Đường kính lõi: ${formatCmFromMm(parseNumber(form.innerDiameterMm))} (${formatMm(parseNumber(form.innerDiameterMm))})`,
    `Hệ số độ chặt: ${formatOptional(parseNumber(form.packingFactor), decimalFormatter)}`,
    "",
    `Barem: ${formatOptional(result.kgPerMeter, decimalFormatter)} kg/m`,
    `Chiều dài: ${formatOptional(result.lengthM, decimalFormatter)} m`,
    `Đường kính ngoài: ${formatCmFromMm(result.outerDiameterMm)} (${formatMm(result.outerDiameterMm)})`,
    `Bề dày phần cuộn: ${formatCmFromMm(result.radialBuildMm)} (${formatMm(result.radialBuildMm)})`,
    `Số vòng ước tính: ${formatOptional(Math.round(result.estimatedTurns), integerFormatter)} vòng`,
  ].join("\n");
}

async function copyText(text: string): Promise<void> {
  if (navigator.clipboard) {
    await navigator.clipboard.writeText(text);
    return;
  }

  const textarea = document.createElement("textarea");
  textarea.value = text;
  textarea.setAttribute("readonly", "true");
  textarea.style.position = "fixed";
  textarea.style.left = "-9999px";
  document.body.appendChild(textarea);
  textarea.select();
  document.execCommand("copy");
  document.body.removeChild(textarea);
}

function Field({
  label,
  unit,
  error,
  hint,
  children,
}: {
  label: string;
  unit?: string;
  error?: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <label className="calc-field">
      <span className="calc-label-row">
        <span>{label}</span>
        {unit && <span>{unit}</span>}
      </span>
      {children}
      {hint && !error && <span className="calc-field-hint">{hint}</span>}
      {error && <span className="calc-field-error">{error}</span>}
    </label>
  );
}

function ResultCard({
  icon,
  label,
  value,
  sub,
  featured = false,
}: {
  icon: ReactNode;
  label: string;
  value: string;
  sub?: string;
  featured?: boolean;
}) {
  return (
    <div className={`calc-result-card ${featured ? "is-featured" : ""}`}>
      <span className="calc-result-icon">{icon}</span>
      <span className="calc-result-label">{label}</span>
      <strong>{value}</strong>
      {sub && <span className="calc-result-sub">{sub}</span>}
    </div>
  );
}

export function TinhToan({ embedded = false }: { embedded?: boolean }) {
  const { theme, toggle } = useTheme();
  const [form, setForm] = useState<FormState>(defaultForm);
  const [copied, setCopied] = useState(false);
  const formRef = useRef<HTMLFormElement | null>(null);
  const resultsRef = useRef<HTMLDivElement | null>(null);
  const errors = useMemo(() => validateForm(form), [form]);
  const hasErrors = Object.keys(errors).length > 0;
  const input = useMemo(() => buildInput(form), [form]);
  const result = useMemo(() => (hasErrors ? null : calculateStainlessSteelCoil(input)), [hasErrors, input]);
  const density = resolveSteelDensity(form.steelType, parseNumber(form.customDensityKgM3));

  const updateField = (key: keyof FormState) => (event: ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
    setForm((current) => ({ ...current, [key]: event.target.value }));
    setCopied(false);
  };

  const updateCmField = (key: "innerDiameterMm") => (event: ChangeEvent<HTMLInputElement>) => {
    const valueCm = event.target.value;
    const valueMm = valueCm.trim() ? parseNumber(valueCm) * 10 : Number.NaN;

    setForm((current) => ({
      ...current,
      [key]: Number.isFinite(valueMm) ? String(valueMm) : "",
    }));
    setCopied(false);
  };

  const setWidth = (value: number) => {
    setForm((current) => ({ ...current, widthMm: String(value) }));
    setCopied(false);
  };

  const setPackingFactor = (value: string) => {
    setForm((current) => ({ ...current, packingFactor: value }));
    setCopied(false);
  };

  const setDirection = (value: CalcDirection) => {
    setForm((current) => ({ ...current, calcDirection: value }));
    setCopied(false);
  };

  const setBaremMode = (value: BaremMode) => {
    setForm((current) => ({ ...current, baremMode: value }));
    setCopied(false);
  };

  const handleCalculate = () => {
    resultsRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  };

  const handleReset = () => {
    setForm(defaultForm);
    setCopied(false);
    window.setTimeout(() => {
      formRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
    }, 0);
  };

  const handleCopy = async () => {
    if (!result) return;

    await copyText(createCopyText(form, result));
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2200);
  };

  if (embedded) {
    return (
      <main className="calc-mini">
        <form className="calc-panel calc-mini-form" onSubmit={(event) => event.preventDefault()}>
          <div className="calc-mode-tabs" role="group" aria-label="Chiều tính">
            <button
              type="button"
              className={form.calcDirection === "mass" ? "is-active" : ""}
              onClick={() => setDirection("mass")}
            >
              Từ khối lượng
            </button>
            <button
              type="button"
              className={form.calcDirection === "length" ? "is-active" : ""}
              onClick={() => setDirection("length")}
            >
              Từ chiều dài
            </button>
          </div>

          <div className="calc-mini-fields">
            <Field label="Độ dày inox" unit="mm" error={errors.thicknessMm}>
              <input
                list="thickness-options-mini"
                type="number"
                inputMode="decimal"
                min="0"
                step="0.1"
                value={form.thicknessMm}
                onChange={updateField("thicknessMm")}
                className="calc-input"
              />
              <datalist id="thickness-options-mini">
                {thicknessOptions.map((value) => (
                  <option key={value} value={value} />
                ))}
              </datalist>
            </Field>

            <Field label="Khổ rộng cuộn" unit="mm" error={errors.widthMm}>
              <input
                type="number"
                inputMode="numeric"
                min="0"
                step="1"
                value={form.widthMm}
                onChange={updateField("widthMm")}
                className="calc-input"
              />
              <div className="calc-quick-row">
                {standardWidthOptions.map((option) => (
                  <button key={option.value} type="button" onClick={() => setWidth(option.value)}>
                    {option.value}
                  </button>
                ))}
              </div>
            </Field>

            <Field label="Chủng loại inox">
              <select value={form.steelType} onChange={updateField("steelType")} className="calc-input">
                {Object.entries(stainlessSteelDensity).map(([key, item]) => (
                  <option key={key} value={key}>
                    {item.label}
                  </option>
                ))}
              </select>
            </Field>

            {form.calcDirection === "length" ? (
              <Field label="Chiều dài cuộn" unit="m" error={errors.lengthM}>
                <input
                  type="number"
                  inputMode="decimal"
                  min="0"
                  step="1"
                  value={form.lengthM}
                  onChange={updateField("lengthM")}
                  className="calc-input"
                />
              </Field>
            ) : (
              <Field label="Khối lượng cần san" unit="kg" error={errors.massTon}>
                <input
                  type="number"
                  inputMode="numeric"
                  min="0"
                  step="1"
                  value={form.massTon}
                  onChange={updateField("massTon")}
                  className="calc-input"
                />
              </Field>
            )}

            <Field label="Đường kính lõi trong" unit="cm" error={errors.innerDiameterMm}>
              <input
                type="number"
                inputMode="decimal"
                min="0"
                step="0.1"
                value={formatInputCmFromMm(form.innerDiameterMm)}
                onChange={updateCmField("innerDiameterMm")}
                className="calc-input"
              />
            </Field>
          </div>

          <div className="calc-mini-actions">
            <button type="button" className="calc-secondary-button" onClick={handleReset}>
              <RefreshCcw className="h-4 w-4" />
              Đặt lại
            </button>
            <button type="button" className="calc-secondary-button" onClick={handleCopy} disabled={!result}>
              {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              {copied ? "Đã sao chép" : "Sao chép"}
            </button>
          </div>
        </form>

        {result ? (
          <div className="calc-mini-results">
            <ResultCard
              featured
              icon={<Ruler className="h-5 w-5" />}
              label="Bề dày phần cuộn"
              value={formatCmFromMm(result.radialBuildMm)}
              sub={formatMm(result.radialBuildMm)}
            />
            <ResultCard
              featured
              icon={<Gauge className="h-5 w-5" />}
              label="Đường kính ngoài"
              value={formatCmFromMm(result.outerDiameterMm)}
              sub={formatMm(result.outerDiameterMm)}
            />
            {form.calcDirection === "length" ? (
              <ResultCard
                icon={<Scale className="h-5 w-5" />}
                label="Khối lượng"
                value={`${decimalFormatter.format(result.massKg)} kg`}
                sub={`${decimalFormatter.format(result.massKg / 1000)} tấn`}
              />
            ) : (
              <ResultCard
                icon={<Ruler className="h-5 w-5" />}
                label="Chiều dài inox"
                value={`${decimalFormatter.format(result.lengthM)} m`}
              />
            )}
          </div>
        ) : (
          <div className="calc-empty-state">
            Nhập đầy đủ thông số hợp lệ để xem kết quả.
          </div>
        )}
      </main>
    );
  }

  return (
    <main className={embedded ? "calc-embedded-page" : "calc-public-page scroll-thin"}>
      {!embedded && <header className="calc-public-header">
        <Link to="/dashboard" className="calc-brand" aria-label="Về hệ thống">
          <span>ĐH</span>
          <span>
            <strong>{APP_BRAND_NAME}</strong>
            <small>Tiện ích tính toán</small>
          </span>
        </Link>
        <div className="calc-header-actions">
          <button type="button" className="calc-theme-button" onClick={toggle}>
            {theme === "light" ? "Giao diện tối" : "Giao diện sáng"}
          </button>
          <Link to="/login" className="calc-login-link">
            <LogIn className="h-4 w-4" />
            Đăng nhập
          </Link>
        </div>
      </header>}

      <section className="calc-hero">
        <div>
          <span className="calc-eyebrow">
            <Calculator className="h-4 w-4" />
            Tính toán
          </span>
          <h1>Tính kích thước cuộn inox</h1>
          <p>
            Nhập độ dày, khổ rộng và lõi cuộn, rồi chọn tính theo khối lượng để ra chiều dài — hoặc đảo lại,
            nhập chiều dài để tính ra cân. Xem ngay bề dày phần cuộn theo cm và số vòng ước tính.
          </p>
        </div>
        <div className="calc-hero-stats">
          <span>{result ? getBaremSourceLabel(result) : "Sẵn sàng tính toán"}</span>
          <strong>{result ? formatCmFromMm(result.radialBuildMm) : "6,04 cm"}</strong>
          <small>Bề dày phần cuộn</small>
        </div>
      </section>

      <section className="calc-grid">
        <form ref={formRef} className="calc-panel calc-form-panel" onSubmit={(event) => event.preventDefault()}>
          <div className="calc-form-head">
            <div className="calc-section-title">
              <Scale className="h-5 w-5" />
              Thông số đầu vào
            </div>
            <div className="calc-barem-switch" role="group" aria-label="Chế độ tính barem">
              <button
                type="button"
                title="Barem theo bảng thực tế"
                className={form.baremMode === "table" ? "is-active" : ""}
                onClick={() => setBaremMode("table")}
              >
                Thực tế
              </button>
              <button
                type="button"
                title="Tính theo khối lượng riêng"
                className={form.baremMode === "density" ? "is-active" : ""}
                onClick={() => setBaremMode("density")}
              >
                KL riêng
              </button>
            </div>
          </div>

          <div className="calc-mode-tabs" role="group" aria-label="Chiều tính">
            <button
              type="button"
              className={form.calcDirection === "mass" ? "is-active" : ""}
              onClick={() => setDirection("mass")}
            >
              Từ khối lượng → chiều dài
            </button>
            <button
              type="button"
              className={form.calcDirection === "length" ? "is-active" : ""}
              onClick={() => setDirection("length")}
            >
              Từ chiều dài → cân
            </button>
          </div>

          <div className="calc-fields-grid">
            <Field label="Độ dày inox" unit="mm" error={errors.thicknessMm}>
              <input
                list="thickness-options"
                type="number"
                min="0"
                step="0.1"
                value={form.thicknessMm}
                onChange={updateField("thicknessMm")}
                className="calc-input"
              />
              <datalist id="thickness-options">
                {thicknessOptions.map((value) => (
                  <option key={value} value={value} />
                ))}
              </datalist>
            </Field>

            <Field label="Khổ rộng cuộn" unit="mm" error={errors.widthMm}>
              <input
                type="number"
                min="0"
                step="1"
                value={form.widthMm}
                onChange={updateField("widthMm")}
                className="calc-input"
              />
              <div className="calc-quick-row">
                {standardWidthOptions.map((option) => (
                  <button key={option.value} type="button" onClick={() => setWidth(option.value)}>
                    {option.value} mm
                  </button>
                ))}
              </div>
            </Field>

            <Field label="Chủng loại inox">
              <select value={form.steelType} onChange={updateField("steelType")} className="calc-input">
                {Object.entries(stainlessSteelDensity).map(([key, item]) => (
                  <option key={key} value={key}>
                    {item.label}
                  </option>
                ))}
                <option value="custom">Tùy chỉnh</option>
              </select>
              <span className="calc-field-hint">
                Đang dùng: {densityFormatter.format(density.densityKgM3 || 0)} kg/m³
              </span>
            </Field>

            {form.steelType === "custom" && (
              <Field label="Khối lượng riêng tùy chỉnh" unit="kg/m³" error={errors.customDensityKgM3}>
                <input
                  type="number"
                  min="0"
                  step="1"
                  value={form.customDensityKgM3}
                  onChange={updateField("customDensityKgM3")}
                  className="calc-input"
                />
              </Field>
            )}

            {form.calcDirection === "length" ? (
              <Field label="Chiều dài cuộn" unit="m" error={errors.lengthM}>
                <input
                  type="number"
                  inputMode="decimal"
                  min="0"
                  step="1"
                  value={form.lengthM}
                  onChange={updateField("lengthM")}
                  className="calc-input"
                />
              </Field>
            ) : (
              <Field label="Khối lượng cần san" unit="kg" error={errors.massTon}>
                <input
                  type="number"
                  inputMode="numeric"
                  min="0"
                  step="1"
                  value={form.massTon}
                  onChange={updateField("massTon")}
                  className="calc-input"
                />
              </Field>
            )}

            <Field label="Đường kính lõi trong" unit="cm" error={errors.innerDiameterMm}>
              <input
                type="number"
                min="0"
                step="0.1"
                value={formatInputCmFromMm(form.innerDiameterMm)}
                onChange={updateCmField("innerDiameterMm")}
                className="calc-input"
              />
            </Field>

            <Field
              label="Hệ số độ chặt"
              error={errors.packingFactor}
              hint="1,00 là cuộn lý thuyết hoàn toàn chặt."
            >
              <input
                type="number"
                min="0.9"
                max="1"
                step="0.01"
                value={form.packingFactor}
                onChange={updateField("packingFactor")}
                className="calc-input"
              />
              <div className="calc-quick-row">
                <button type="button" onClick={() => setPackingFactor("1")}>
                  Lý thuyết
                </button>
                <button type="button" onClick={() => setPackingFactor("0.98")}>
                  Cuộn chặt
                </button>
                <button type="button" onClick={() => setPackingFactor("0.95")}>
                  Cuộn thường
                </button>
              </div>
            </Field>
          </div>

          <div className="calc-actions">
            <button type="button" className="calc-primary-button" onClick={handleCalculate}>
              <Calculator className="h-4 w-4" />
              Tính toán
            </button>
            <button type="button" className="calc-secondary-button" onClick={handleReset}>
              <RefreshCcw className="h-4 w-4" />
              Đặt lại
            </button>
            <button type="button" className="calc-secondary-button" onClick={handleCopy} disabled={!result}>
              {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              {copied ? "Đã sao chép" : "Sao chép kết quả"}
            </button>
          </div>
        </form>

        <div ref={resultsRef} className="calc-results-stack">
          <div className="calc-panel">
            <div className="calc-section-title">
              <Gauge className="h-5 w-5" />
              Kết quả
            </div>

            {result ? (
              <>
                {result.notice && <div className="calc-notice">{result.notice}</div>}
                <div className="calc-result-grid">
                  <ResultCard
                    featured
                    icon={<Ruler className="h-5 w-5" />}
                    label="Bề dày phần cuộn"
                    value={formatCmFromMm(result.radialBuildMm)}
                    sub={formatMm(result.radialBuildMm)}
                  />
                  {form.calcDirection === "length" ? (
                    <ResultCard
                      icon={<Scale className="h-5 w-5" />}
                      label="Khối lượng"
                      value={`${decimalFormatter.format(result.massKg)} kg`}
                      sub={`${decimalFormatter.format(result.massKg / 1000)} tấn`}
                    />
                  ) : (
                    <ResultCard
                      icon={<Ruler className="h-5 w-5" />}
                      label="Chiều dài inox"
                      value={`${decimalFormatter.format(result.lengthM)} m`}
                    />
                  )}
                  <ResultCard
                    icon={<RefreshCcw className="h-5 w-5" />}
                    label="Số vòng ước tính"
                    value={`${integerFormatter.format(Math.round(result.estimatedTurns))} vòng`}
                  />
                </div>
              </>
            ) : (
              <div className="calc-empty-state">
                Nhập đầy đủ thông số hợp lệ để xem kết quả. Hệ thống sẽ không hiển thị NaN hoặc kết quả âm.
              </div>
            )}
          </div>

        </div>
      </section>

      <div className="calc-mobile-actions" aria-label="Thao tác tính toán">
        <button type="button" className="calc-primary-button" onClick={handleCalculate}>
          <Calculator className="h-4 w-4" />
          Tính toán
        </button>
        <button type="button" className="calc-secondary-button" onClick={handleReset} aria-label="Đặt lại">
          <RefreshCcw className="h-4 w-4" />
          Đặt lại
        </button>
        <button type="button" className="calc-secondary-button" onClick={handleCopy} disabled={!result} aria-label="Sao chép kết quả">
          {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
          {copied ? "Đã sao chép" : "Sao chép kết quả"}
        </button>
      </div>
    </main>
  );
}
