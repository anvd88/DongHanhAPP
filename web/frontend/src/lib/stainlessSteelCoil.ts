export interface StainlessSteelBarem {
  thicknessMm: number;
  weightKgPerMeter: {
    width1000: number;
    width1200: number;
    width1500: number;
  };
}

const knownBaremRows: StainlessSteelBarem[] = [
  { thicknessMm: 0.3, weightKgPerMeter: { width1000: 2.4, width1200: 2.9, width1500: 3.6 } },
  { thicknessMm: 0.4, weightKgPerMeter: { width1000: 3.2, width1200: 3.8, width1500: 4.8 } },
  { thicknessMm: 0.5, weightKgPerMeter: { width1000: 4.0, width1200: 4.8, width1500: 5.9 } },
  { thicknessMm: 0.6, weightKgPerMeter: { width1000: 4.8, width1200: 5.7, width1500: 7.1 } },
  { thicknessMm: 0.7, weightKgPerMeter: { width1000: 5.6, width1200: 6.7, width1500: 8.3 } },
  { thicknessMm: 0.8, weightKgPerMeter: { width1000: 6.3, width1200: 7.6, width1500: 9.5 } },
  { thicknessMm: 0.9, weightKgPerMeter: { width1000: 7.1, width1200: 8.6, width1500: 10.7 } },
  { thicknessMm: 1.0, weightKgPerMeter: { width1000: 7.9, width1200: 9.5, width1500: 11.9 } },
  { thicknessMm: 1.1, weightKgPerMeter: { width1000: 8.7, width1200: 10.5, width1500: 13.1 } },
  { thicknessMm: 1.2, weightKgPerMeter: { width1000: 9.5, width1200: 11.4, width1500: 14.3 } },
  { thicknessMm: 1.5, weightKgPerMeter: { width1000: 11.9, width1200: 14.3, width1500: 17.8 } },
  { thicknessMm: 1.8, weightKgPerMeter: { width1000: 14.3, width1200: 17.1, width1500: 21.4 } },
  { thicknessMm: 2.0, weightKgPerMeter: { width1000: 15.9, width1200: 19.0, width1500: 23.8 } },
  { thicknessMm: 2.5, weightKgPerMeter: { width1000: 19.8, width1200: 23.8, width1500: 29.7 } },
  { thicknessMm: 3.0, weightKgPerMeter: { width1000: 23.8, width1200: 28.5, width1500: 35.7 } },
  { thicknessMm: 3.5, weightKgPerMeter: { width1000: 27.8, width1200: 33.3, width1500: 41.6 } },
  { thicknessMm: 4.0, weightKgPerMeter: { width1000: 31.7, width1200: 38.1, width1500: 47.6 } },
  { thicknessMm: 5.0, weightKgPerMeter: { width1000: 39.7, width1200: 47.6, width1500: 59.5 } },
];

const DEFAULT_BAREM_DENSITY_KG_M3 = 7930;
const MIN_THICKNESS_TENTHS = 3;
const MAX_THICKNESS_TENTHS = 50;
const knownBaremByThickness = new Map(knownBaremRows.map((row) => [row.thicknessMm, row]));

const roundToOneDecimal = (value: number) => Math.round(value * 10) / 10;

function calculateStandardWidthBarem(thicknessMm: number, widthMm: number): number {
  return roundToOneDecimal(DEFAULT_BAREM_DENSITY_KG_M3 * (thicknessMm / 1000) * (widthMm / 1000));
}

function createGeneratedBaremRow(thicknessMm: number): StainlessSteelBarem {
  return {
    thicknessMm,
    weightKgPerMeter: {
      width1000: calculateStandardWidthBarem(thicknessMm, 1000),
      width1200: calculateStandardWidthBarem(thicknessMm, 1200),
      width1500: calculateStandardWidthBarem(thicknessMm, 1500),
    },
  };
}

export const thicknessOptions = Array.from(
  { length: MAX_THICKNESS_TENTHS - MIN_THICKNESS_TENTHS + 1 },
  (_, index) => (MIN_THICKNESS_TENTHS + index) / 10,
);

export const stainlessSteelBaremTable: StainlessSteelBarem[] = thicknessOptions.map(
  (thicknessMm) => knownBaremByThickness.get(thicknessMm) ?? createGeneratedBaremRow(thicknessMm),
);

export const standardWidthOptions = [
  { label: "Khổ 1.000 mm", value: 1000 },
  { label: "Khổ 1.200 mm", value: 1200 },
  { label: "Khổ 1.500 mm", value: 1500 },
] as const;

export const stainlessSteelDensity = {
  inox201: { label: "Inox 201", densityKgM3: 7930 },
  inox304: { label: "Inox 304", densityKgM3: 7930 },
  inox316: { label: "Inox 316", densityKgM3: 7980 },
  inox430: { label: "Inox 430", densityKgM3: 7700 },
} as const;

export type StainlessSteelType = keyof typeof stainlessSteelDensity | "custom";
export type BaremMode = "table" | "density";
export type BaremSource = "table" | "density" | "densityFallback";

export interface CoilCalculationInput {
  thicknessMm: number;
  widthMm: number;
  steelType: StainlessSteelType;
  customDensityKgM3: number;
  massTon: number;
  innerDiameterMm: number;
  packingFactor: number;
  baremMode: BaremMode;
}

export interface CoilCalculationResult {
  densityKgM3: number;
  steelLabel: string;
  massKg: number;
  kgPerMeter: number;
  lengthM: number;
  outerDiameterMm: number;
  radialBuildMm: number;
  estimatedTurns: number;
  baremSource: BaremSource;
  notice?: string;
}

export interface CoilDiameterInput {
  weightKg: number;
  widthMm: number;
  innerDiameterMm: number;
  densityKgM3: number;
  packingFactor: number;
}

export interface CoilDiameterResult {
  outerDiameterMm: number;
  radialBuildMm: number;
}

const EPSILON = 0.000001;

export function getBaremFromTable(thicknessMm: number, widthMm: number): number | null {
  const row = stainlessSteelBaremTable.find((item) => Math.abs(item.thicknessMm - thicknessMm) < EPSILON);

  if (!row) return null;
  if (widthMm === 1000) return row.weightKgPerMeter.width1000;
  if (widthMm === 1200) return row.weightKgPerMeter.width1200;
  if (widthMm === 1500) return row.weightKgPerMeter.width1500;

  return null;
}

export function calculateCustomWidthBarem(thicknessMm: number, widthMm: number): number | null {
  const row = stainlessSteelBaremTable.find((item) => Math.abs(item.thicknessMm - thicknessMm) < EPSILON);

  if (!row || widthMm <= 0) return null;

  return row.weightKgPerMeter.width1000 * (widthMm / 1000);
}

export function calculateWeightKg(baremKgPerMeter: number, lengthM: number): number {
  if (baremKgPerMeter <= 0 || lengthM <= 0) return 0;
  return baremKgPerMeter * lengthM;
}

export function calculateLengthM(weightKg: number, baremKgPerMeter: number): number {
  if (weightKg <= 0 || baremKgPerMeter <= 0) return 0;
  return weightKg / baremKgPerMeter;
}

export function calculateDensityBarem(thicknessMm: number, widthMm: number, densityKgM3: number): number {
  if (thicknessMm <= 0 || widthMm <= 0 || densityKgM3 <= 0) return 0;

  const thicknessM = thicknessMm / 1000;
  const widthM = widthMm / 1000;

  return densityKgM3 * thicknessM * widthM;
}

export function calculateCoilDiameter(input: CoilDiameterInput): CoilDiameterResult {
  const { weightKg, widthMm, innerDiameterMm, densityKgM3, packingFactor } = input;

  if (weightKg <= 0 || widthMm <= 0 || innerDiameterMm <= 0 || densityKgM3 <= 0 || packingFactor <= 0) {
    return { outerDiameterMm: 0, radialBuildMm: 0 };
  }

  const widthM = widthMm / 1000;
  const innerDiameterM = innerDiameterMm / 1000;

  // Formula based on the volume of a cylindrical annulus.
  const outerDiameterM = Math.sqrt(
    innerDiameterM ** 2 + (4 * weightKg) / (Math.PI * densityKgM3 * widthM * packingFactor),
  );
  const outerDiameterMm = outerDiameterM * 1000;
  const radialBuildMm = (outerDiameterMm - innerDiameterMm) / 2;

  return { outerDiameterMm, radialBuildMm };
}

export function resolveSteelDensity(type: StainlessSteelType, customDensityKgM3: number) {
  if (type === "custom") {
    return { label: "Tùy chỉnh", densityKgM3: customDensityKgM3 };
  }

  return stainlessSteelDensity[type];
}

export function calculateStainlessSteelCoil(input: CoilCalculationInput): CoilCalculationResult | null {
  const density = resolveSteelDensity(input.steelType, input.customDensityKgM3);
  const massKg = input.massTon * 1000;

  if (
    input.thicknessMm <= 0 ||
    input.widthMm <= 0 ||
    density.densityKgM3 <= 0 ||
    input.massTon <= 0 ||
    input.innerDiameterMm <= 0 ||
    input.packingFactor < 0.9 ||
    input.packingFactor > 1
  ) {
    return null;
  }

  const tableBarem = getBaremFromTable(input.thicknessMm, input.widthMm)
    ?? calculateCustomWidthBarem(input.thicknessMm, input.widthMm);
  const canUseTable = input.baremMode === "table" && tableBarem !== null;
  const kgPerMeter = canUseTable
    ? tableBarem
    : calculateDensityBarem(input.thicknessMm, input.widthMm, density.densityKgM3);
  const baremSource: BaremSource = canUseTable
    ? "table"
    : input.baremMode === "table"
      ? "densityFallback"
      : "density";
  const lengthM = calculateLengthM(massKg, kgPerMeter);
  const diameter = calculateCoilDiameter({
    weightKg: massKg,
    widthMm: input.widthMm,
    innerDiameterMm: input.innerDiameterMm,
    densityKgM3: density.densityKgM3,
    packingFactor: input.packingFactor,
  });
  const estimatedTurns = diameter.radialBuildMm / input.thicknessMm;

  if (
    kgPerMeter <= 0 ||
    lengthM <= 0 ||
    diameter.outerDiameterMm <= 0 ||
    diameter.radialBuildMm < 0 ||
    !Number.isFinite(kgPerMeter) ||
    !Number.isFinite(lengthM) ||
    !Number.isFinite(diameter.outerDiameterMm) ||
    !Number.isFinite(diameter.radialBuildMm) ||
    !Number.isFinite(estimatedTurns)
  ) {
    return null;
  }

  return {
    densityKgM3: density.densityKgM3,
    steelLabel: density.label,
    massKg,
    kgPerMeter,
    lengthM,
    outerDiameterMm: diameter.outerDiameterMm,
    radialBuildMm: diameter.radialBuildMm,
    estimatedTurns,
    baremSource,
    notice:
      baremSource === "densityFallback"
        ? "Độ dày này chưa có trong bảng barem, kết quả đang được tính theo khối lượng riêng."
        : undefined,
  };
}
