// Presentation colors (not astronomy): star tint from the B−V color index.

/** Approximate effective temperature from B−V (Ballesteros 2012). */
export function temperatureFromColorIndex(bv: number): number {
  const b = Math.min(Math.max(bv, -0.4), 2.0);
  return 4600 * (1 / (0.92 * b + 1.7) + 1 / (0.92 * b + 0.62));
}

/** RGB (0–1) of a black body, after T. Helland's fit, softened toward white for display. */
export function starColor(bv: number): [number, number, number] {
  const t = temperatureFromColorIndex(bv) / 100;
  const clamp = (v: number): number => Math.min(Math.max(v, 0), 255) / 255;
  const r = t <= 66 ? 255 : 329.698727446 * Math.pow(t - 60, -0.1332047592);
  const g =
    t <= 66 ? 99.4708025861 * Math.log(t) - 161.1195681661 : 288.1221695283 * Math.pow(t - 60, -0.0755148492);
  const b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.log(t - 10) - 305.0447927307;
  const soften = (v: number): number => 0.45 + 0.55 * clamp(v);
  return [soften(r), soften(g), soften(b)];
}

/** Marker shape of a deep-sky type: 0 ellipse, 1 circle, 2 square, 3 ring. */
export function dsoShape(type: string): number {
  switch (type) {
    case 'Galaxy':
    case 'GalaxyGroup':
      return 0;
    case 'PlanetaryNebula':
      return 3;
    case 'DiffuseNebula':
    case 'EmissionNebula':
    case 'ReflectionNebula':
    case 'HiiRegion':
    case 'DarkNebula':
    case 'SupernovaRemnant':
    case 'ClusterWithNebula':
      return 2;
    default:
      return 1;
  }
}

export const palette = {
  background: 0x050b16,
  constellationLines: [0.33, 0.55, 0.78] as const,
  constellationBounds: [0.42, 0.33, 0.55] as const,
  milkyWay: [0.55, 0.62, 0.8] as const,
  altAzGrid: [0.25, 0.45, 0.35] as const,
  equatorialGrid: [0.25, 0.38, 0.6] as const,
  ecliptic: [0.85, 0.65, 0.3] as const,
  meridian: [0.6, 0.35, 0.35] as const,
  dsoShapes: [
    [0.85, 0.45, 0.45],
    [0.9, 0.82, 0.45],
    [0.45, 0.8, 0.6],
    [0.45, 0.75, 0.9],
  ] as const,
  plan: [1.0, 0.71, 0.37] as const,
  selected: [1.0, 1.0, 1.0] as const,
  ground: [0.035, 0.05, 0.04] as const,
  horizon: [0.09, 0.13, 0.12] as const,
  label: '#8fa8c8',
  constellationLabel: 'rgba(110, 150, 200, 0.75)',
  bodyLabel: '#f5c26b',
  cardinal: '#ffb45e',
};
