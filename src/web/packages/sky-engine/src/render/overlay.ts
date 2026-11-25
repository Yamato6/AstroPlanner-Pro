// The sky view's 2D canvas overlay: labels, the Sun, Moon, planets, comets and satellites, the
// all-sky and finder frames, cardinal points and the camera field-of-view rectangle.
import type { Layers, MovingBody } from '../bridge/schema';
import { Star } from '../data/buffers';
import type { SkyData } from '../data/loader';
import type { Vec3 } from '../math/frame';
import { bodyPosition, dsoVector, inTrack, isSunlit, starVector, type BodyTrack } from './bodies';
import { palette } from './colors';
import {
  fovOutline,
  fovUpMarker,
  raDec,
  type ChartGeometry,
  type ScreenPoint,
  type ViewMode,
} from './projection';

/** Above this field (degrees) only the brightest star names, no Messier and no minor-body labels are drawn. */
const WIDE_LABEL_FOV = 110;
const FINDER_LABELS = 60;
const CARDINALS = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'] as const;
const FOV_COLOR = '#ff6f6f';
/** Satellite trail: the last minute of the path, in 5 s pieces. */
const SATELLITE_TRAIL_STEPS = 12;
const SATELLITE_TRAIL_STEP_MS = 5000;

export interface Selection {
  readonly kind: 'star' | 'dso' | 'body';
  readonly id: string;
}

/** A projected point with its apparent horizontal position (90°/0 in the finder). */
export interface Placed extends ScreenPoint {
  readonly altitude: number;
  readonly azimuth: number;
}

/** The camera field shown on the sky. */
export interface FovState {
  readonly widthDegrees: number;
  readonly heightDegrees: number;
  rotationDegrees: number;
  center: Vec3 | null;
  readonly label: string;
}

/** What the overlay reads of the view: its state this frame and its projection. */
export interface OverlayView {
  readonly data: SkyData;
  readonly layers: Layers;
  readonly mode: ViewMode;
  readonly width: number;
  readonly height: number;
  readonly effectiveFov: number;
  readonly starLimit: number;
  readonly pixelsPerDegree: number;
  readonly chart: ChartGeometry;
  readonly finderCenter: Vec3;
  readonly finderFov: number;
  readonly minimumAltitudeDegrees: number;
  readonly plan: ReadonlySet<string>;
  readonly selection: Selection | null;
  readonly bodies: readonly BodyTrack[];
  readonly stats: string | null;
  /** Screen position of a J2000 direction (null when hidden by the ground when `cut`, or off screen). */
  place(j2000: Vec3, margin?: number, cut?: boolean): Placed | null;
  /** Screen position of an apparent horizontal direction (null in the finder or off screen). */
  placeHorizontal(altitudeDegrees: number, azimuthDegrees: number, margin?: number): ScreenPoint | null;
  selectionPosition(selection: Selection, utcMs: number): Vec3 | null;
  dsoVisible(i: number): boolean;
}

type Label = (text: string, x: number, y: number, style: string, force?: boolean) => void;

interface FinderLabel {
  readonly vector: Vec3;
  readonly text: string;
}

export class SkyOverlay {
  private finderLabels: { key: string; items: FinderLabel[] } = { key: '', items: [] };
  /** The view of the frame being drawn. */
  private view!: OverlayView;

  constructor(private readonly g: CanvasRenderingContext2D) {}

  /** Draws the frame's overlay; returns the field-of-view outline on screen (empty when hidden). */
  draw(view: OverlayView, utcMs: number, fov: FovState | null): (ScreenPoint | null)[] {
    this.view = view;
    const g = this.g;
    const v = view;
    g.clearRect(0, 0, v.width, v.height);
    g.font = '12px "Segoe UI", sans-serif';
    g.textBaseline = 'middle';
    const occupied = new Set<string>();
    const label: Label = (text, x, y, style, force = false) => {
      const key = `${Math.round(x / 70)}:${Math.round(y / 16)}`;
      if (!force && occupied.has(key)) {
        return;
      }
      occupied.add(key);
      g.fillStyle = style;
      g.fillText(text, x, y);
    };
    const labels = v.layers.labels;

    if (v.mode === 'allSky') {
      this.drawAllSkyFrame();
    }

    if (v.layers.constellationNames) {
      g.textAlign = 'center';
      for (const constellation of v.data.constellations) {
        const p = v.place(constellation.label);
        if (p) {
          label(constellation.name.toUpperCase(), p.x, p.y, palette.constellationLabel);
        }
      }
    }

    g.textAlign = 'left';
    if (labels && v.layers.stars) {
      if (v.mode === 'finder') {
        for (const item of this.finderStarLabels()) {
          const p = v.place(item.vector);
          if (p) {
            label(item.text, p.x + 5, p.y - 6, palette.label);
          }
        }
      } else {
        // Label density follows the field: only the brightest names in a very wide view.
        const limit = Math.min(v.effectiveFov > WIDE_LABEL_FOV ? 1.5 : 2.5, v.starLimit - 3.5);
        const { stars, starNames } = v.data;
        for (let i = 0; i < stars.count; i++) {
          const magnitude = stars.float32[i * Star.stride + Star.magnitude] ?? 99;
          if (magnitude > limit) {
            break;
          }
          const name = starNames.get(stars.uint32[i * Star.stride + Star.hygId] ?? 0);
          if (!name) {
            continue;
          }
          const p = v.place(starVector(v.data, i));
          if (p) {
            label(name, p.x + 6, p.y - 6, palette.label);
          }
        }
      }
    }

    if (labels && v.layers.deepSky) {
      const showAll = v.effectiveFov < 15;
      const showMessier = v.effectiveFov <= WIDE_LABEL_FOV;
      v.data.dsoIndex.forEach(([id], i) => {
        const highlighted = v.plan.has(id) || (v.selection?.kind === 'dso' && v.selection.id === id);
        if (!highlighted && !((showMessier && /^M\d/.test(id)) || (showAll && v.dsoVisible(i)))) {
          return;
        }
        const p = v.place(dsoVector(v.data, i));
        if (p) {
          label(id, p.x + 8, p.y + 8, highlighted ? '#ffb45e' : palette.label, highlighted);
        }
      });
    }

    for (const track of v.bodies) {
      if (track.body.kind === 'satellite') {
        if (v.layers.satellites) {
          this.drawSatellite(track, utcMs, labels ? label : null);
        }
      } else if (v.layers.bodies) {
        // In a very wide view only the Sun, the Moon, the planets and what is selected or
        // planned keep their names (minor bodies would bury the sky in labels).
        const minor = track.body.kind === 'comet' || track.body.kind === 'asteroid';
        const named =
          !minor ||
          v.effectiveFov <= WIDE_LABEL_FOV ||
          v.plan.has(track.body.id) ||
          (v.selection?.kind === 'body' && v.selection.id === track.body.id);
        this.drawBody(track, utcMs, labels && named ? label : null);
      }
    }

    if (v.selection && v.selection.kind !== 'dso') {
      const position = v.selectionPosition(v.selection, utcMs);
      const p = position ? v.place(position) : null;
      if (p) {
        g.strokeStyle = '#ffffff';
        g.lineWidth = 1.5;
        g.beginPath();
        g.arc(p.x, p.y, 12, 0, Math.PI * 2);
        g.stroke();
      }
    }

    const fovScreen = this.drawFov(fov);

    if (v.mode === 'dome' && v.layers.ground) {
      g.textAlign = 'center';
      g.font = 'bold 14px "Segoe UI", sans-serif';
      CARDINALS.forEach((name, i) => {
        const p = v.placeHorizontal(0, i * 45);
        if (p) {
          label(name, p.x, p.y - 12, palette.cardinal, true);
        }
      });
    } else if (v.mode === 'finder') {
      this.drawFinderFrame();
    }

    if (v.stats) {
      g.textAlign = 'left';
      g.font = '12px Consolas, monospace';
      g.fillStyle = '#7fe08a';
      g.fillText(v.stats, 8, 14);
    }
    return fovScreen;
  }

  /** All-sky chart: horizon circle, minimum altitude and cardinal points around it. */
  private drawAllSkyFrame(): void {
    const g = this.g;
    const { cx, cy, scale } = this.view.chart;
    g.strokeStyle = 'rgba(255, 180, 94, 0.55)';
    g.lineWidth = 1.5;
    g.beginPath();
    g.arc(cx, cy, scale, 0, Math.PI * 2);
    g.stroke();
    const minimum = this.view.minimumAltitudeDegrees;
    if (minimum > 0) {
      g.save();
      g.setLineDash([5, 5]);
      g.strokeStyle = 'rgba(255, 180, 94, 0.35)';
      g.lineWidth = 1;
      g.beginPath();
      g.arc(cx, cy, ((90 - minimum) / 90) * scale, 0, Math.PI * 2);
      g.stroke();
      g.restore();
    }
    g.textAlign = 'center';
    g.font = 'bold 14px "Segoe UI", sans-serif';
    g.fillStyle = palette.cardinal;
    CARDINALS.forEach((name, i) => {
      const az = i * 45 * (Math.PI / 180);
      const r = scale + (i % 2 === 0 ? 16 : 14);
      g.font = i % 2 === 0 ? 'bold 14px "Segoe UI", sans-serif' : '11px "Segoe UI", sans-serif';
      g.fillText(name, cx - r * Math.sin(az), cy - r * Math.cos(az));
    });
  }

  /** Finder chart: center coordinates, field, compass and a scale bar. */
  private drawFinderFrame(): void {
    const g = this.g;
    const { width, height, finderFov } = this.view;
    const { raHours, decDegrees } = raDec(this.view.finderCenter);
    const raH = Math.floor(raHours);
    const raM = (raHours - raH) * 60;
    const sign = decDegrees < 0 ? '−' : '+';
    const decAbs = Math.abs(decDegrees);
    const decD = Math.floor(decAbs);
    const decM = Math.round((decAbs - decD) * 60);
    const text =
      `α ${raH.toString().padStart(2, '0')}h ${raM.toFixed(1).padStart(4, '0')}m   ` +
      `δ ${sign}${decD.toString().padStart(2, '0')}° ${decM.toString().padStart(2, '0')}′   ` +
      (finderFov < 2 ? `${(finderFov * 60).toFixed(0)}′` : `${finderFov.toFixed(1)}°`);
    g.font = '12px "Segoe UI", sans-serif';
    g.textAlign = 'left';
    g.fillStyle = palette.label;
    g.fillText(text, 10, height - 14);

    // Compass: north up, east left (a mirror image of a map, as on the sky).
    const x = width - 40;
    const y = 44;
    g.strokeStyle = palette.cardinal;
    g.fillStyle = palette.cardinal;
    g.lineWidth = 1.5;
    g.beginPath();
    g.moveTo(x, y);
    g.lineTo(x, y - 24);
    g.moveTo(x, y);
    g.lineTo(x - 24, y);
    g.stroke();
    g.textAlign = 'center';
    g.font = 'bold 12px "Segoe UI", sans-serif';
    g.fillText('N', x, y - 32);
    g.fillText('E', x - 32, y);

    // Scale bar of a round angle about a fifth of the width.
    const pixelsPerDegree = this.view.pixelsPerDegree;
    const target = width / 5 / pixelsPerDegree;
    const steps = [1 / 60, 5 / 60, 10 / 60, 15 / 60, 0.5, 1, 2, 5, 10, 20];
    const angle = steps.reduce((best, s) => (Math.abs(s - target) < Math.abs(best - target) ? s : best), 1);
    const length = angle * pixelsPerDegree;
    const bx = width - 20 - length;
    const by = height - 14;
    g.beginPath();
    g.moveTo(bx, by);
    g.lineTo(bx + length, by);
    g.moveTo(bx, by - 4);
    g.lineTo(bx, by + 4);
    g.moveTo(bx + length, by - 4);
    g.lineTo(bx + length, by + 4);
    g.stroke();
    g.font = '11px "Segoe UI", sans-serif';
    g.fillText(angle < 1 ? `${Math.round(angle * 60)}′` : `${angle}°`, bx + length / 2, by - 10);
  }

  /** The camera field: the sensor rectangle, a tick on its "up" side and its label. */
  private drawFov(fov: FovState | null): (ScreenPoint | null)[] {
    if (!fov?.center) {
      return [];
    }
    const g = this.g;
    const outline = fovOutline(fov.center, fov.widthDegrees, fov.heightDegrees, fov.rotationDegrees);
    const screen = outline.map((v) => this.view.place(v, 1e5, false));
    g.strokeStyle = FOV_COLOR;
    g.lineWidth = 1.6;
    g.beginPath();
    let drawing = false;
    for (const p of screen) {
      if (!p) {
        drawing = false;
        continue;
      }
      if (drawing) {
        g.lineTo(p.x, p.y);
      } else {
        g.moveTo(p.x, p.y);
        drawing = true;
      }
    }
    g.stroke();

    const steps = (outline.length - 1) / 4;
    const topMiddle = screen[Math.round(steps / 2)];
    const tick = this.view.place(fovUpMarker(fov.center, fov.heightDegrees, fov.rotationDegrees), 1e5, false);
    if (topMiddle && tick) {
      g.beginPath();
      g.moveTo(topMiddle.x, topMiddle.y);
      g.lineTo(tick.x, tick.y);
      g.stroke();
    }
    const corner = screen[0];
    if (corner && fov.label) {
      g.font = '12px "Segoe UI", sans-serif';
      g.textAlign = 'left';
      g.fillStyle = FOV_COLOR;
      g.fillText(fov.label, corner.x + 4, corner.y - 9);
    }
    return screen;
  }

  /** Named stars and magnitudes for the finder (recomputed only when the chart changes). */
  private finderStarLabels(): FinderLabel[] {
    const v = this.view;
    const limit = Math.min(v.starLimit - 1.5, 11);
    const c = v.finderCenter;
    const key = `${c.map((x) => x.toFixed(5)).join(',')}|${v.finderFov.toFixed(3)}|${v.width}x${v.height}|${limit.toFixed(1)}`;
    if (key === this.finderLabels.key) {
      return this.finderLabels.items;
    }
    const halfDiagonal = Math.atan(Math.hypot(v.width, v.height) / 2 / v.chart.scale) * (180 / Math.PI);
    const cosLimit = Math.cos(Math.min(89, halfDiagonal + 0.5) * (Math.PI / 180));
    const items: FinderLabel[] = [];
    const { stars, starNames } = v.data;
    for (let i = 0; i < stars.count && items.length < FINDER_LABELS; i++) {
      const magnitude = stars.float32[i * Star.stride + Star.magnitude] ?? 99;
      if (magnitude > limit) {
        break;
      }
      const s = starVector(v.data, i);
      if (s[0] * c[0] + s[1] * c[1] + s[2] * c[2] < cosLimit) {
        continue;
      }
      const name = starNames.get(stars.uint32[i * Star.stride + Star.hygId] ?? 0);
      items.push({ vector: s, text: name ?? magnitude.toFixed(1) });
    }
    this.finderLabels = { key, items };
    return items;
  }

  /** Screen directions of celestial north and east at a J2000 point (unit vectors in canvas px). */
  private screenAxes(j2000: Vec3, at: ScreenPoint): { north: ScreenPoint; east: ScreenPoint } | null {
    const offset = (direction: Vec3): ScreenPoint | null => {
      const p: Vec3 = [
        j2000[0] + direction[0] * 1e-3,
        j2000[1] + direction[1] * 1e-3,
        j2000[2] + direction[2] * 1e-3,
      ];
      const s = this.view.place(p, 1e6, false);
      if (!s) {
        return null;
      }
      const length = Math.hypot(s.x - at.x, s.y - at.y) || 1;
      return { x: (s.x - at.x) / length, y: (s.y - at.y) / length };
    };
    const north = offset([-j2000[0] * j2000[2], -j2000[1] * j2000[2], 1 - j2000[2] * j2000[2]]);
    const east = offset([-j2000[1], j2000[0], 0]);
    return north && east ? { north, east } : null;
  }

  private drawBody(track: BodyTrack, utcMs: number, label: Label | null): void {
    const j2000 = bodyPosition(track, utcMs);
    const p = this.view.place(j2000);
    if (!p) {
      return;
    }
    const g = this.g;
    const body = track.body;
    const minimumRadius = { planet: 2.5, comet: 3, asteroid: 1.8, satellite: 2, sun: 6, moon: 6 }[body.kind];
    const radius = Math.max(minimumRadius, ((body.diameterArcsec / 3600) * this.view.pixelsPerDegree) / 2);

    if (body.kind === 'sun') {
      const glow = g.createRadialGradient(p.x, p.y, radius * 0.5, p.x, p.y, radius * 4);
      glow.addColorStop(0, 'rgba(255, 236, 170, 0.8)');
      glow.addColorStop(1, 'rgba(255, 236, 170, 0)');
      g.fillStyle = glow;
      g.beginPath();
      g.arc(p.x, p.y, radius * 4, 0, Math.PI * 2);
      g.fill();
      g.fillStyle = '#fff3c4';
      g.beginPath();
      g.arc(p.x, p.y, radius, 0, Math.PI * 2);
      g.fill();
    } else if (body.kind === 'moon') {
      this.drawMoon(p, radius, j2000, body);
    } else if (body.kind === 'comet') {
      this.drawComet(p, radius, j2000, body);
    } else if (body.kind === 'asteroid') {
      g.fillStyle = '#cfc8bc';
      g.beginPath();
      g.arc(p.x, p.y, radius, 0, Math.PI * 2);
      g.fill();
    } else {
      g.fillStyle = '#f5c26b';
      g.beginPath();
      g.arc(p.x, p.y, radius, 0, Math.PI * 2);
      g.fill();
    }

    if (label) {
      g.textAlign = 'left';
      label(body.name, p.x + radius + 4, p.y - radius - 2, palette.bodyLabel, true);
    }
  }

  /**
   * A satellite: a dot, bright while sunlit and dim in the Earth's shadow, with a fading trail of
   * the last minute of its path.
   */
  private drawSatellite(track: BodyTrack, utcMs: number, label: Label | null): void {
    if (!inTrack(track, utcMs)) {
      return;
    }
    const p = this.view.place(bodyPosition(track, utcMs));
    if (!p) {
      return;
    }
    const g = this.g;
    const lit = isSunlit(track, utcMs);
    const rgb = lit ? '226, 238, 255' : '110, 118, 134';
    let previous: ScreenPoint = p;
    g.lineWidth = 1.5;
    g.lineCap = 'round';
    for (let k = 1; k <= SATELLITE_TRAIL_STEPS; k++) {
      const t = utcMs - k * SATELLITE_TRAIL_STEP_MS;
      if (t < track.startMs) {
        break;
      }
      const q = this.view.place(bodyPosition(track, t));
      if (!q) {
        break;
      }
      g.strokeStyle = `rgba(${rgb}, ${(0.6 * (1 - k / (SATELLITE_TRAIL_STEPS + 1))).toFixed(3)})`;
      g.beginPath();
      g.moveTo(previous.x, previous.y);
      g.lineTo(q.x, q.y);
      g.stroke();
      previous = q;
    }
    g.fillStyle = `rgb(${rgb})`;
    g.beginPath();
    g.arc(p.x, p.y, lit ? 2.4 : 1.6, 0, Math.PI * 2);
    g.fill();
    if (label && lit) {
      g.textAlign = 'left';
      label(track.body.name, p.x + 6, p.y - 4, palette.bodyLabel);
    }
  }

  /**
   * A comet: a fuzzy head and a short tail pointing away from the Sun (brightLimbAngleDegrees is
   * the position angle of the Sun's direction, as for the Moon's bright limb).
   */
  private drawComet(p: ScreenPoint, radius: number, j2000: Vec3, body: MovingBody): void {
    const g = this.g;
    const axes = this.screenAxes(j2000, p);
    const chi = (body.brightLimbAngleDegrees * Math.PI) / 180 + Math.PI;
    const tail = axes
      ? {
          x: Math.cos(chi) * axes.north.x + Math.sin(chi) * axes.east.x,
          y: Math.cos(chi) * axes.north.y + Math.sin(chi) * axes.east.y,
        }
      : { x: 0, y: -1 };
    const length = Math.hypot(tail.x, tail.y) || 1;
    const end = { x: p.x + (tail.x / length) * radius * 9, y: p.y + (tail.y / length) * radius * 9 };

    const gradient = g.createLinearGradient(p.x, p.y, end.x, end.y);
    gradient.addColorStop(0, 'rgba(160, 232, 216, 0.75)');
    gradient.addColorStop(1, 'rgba(160, 232, 216, 0)');
    g.strokeStyle = gradient;
    g.lineWidth = radius * 1.2;
    g.lineCap = 'round';
    g.beginPath();
    g.moveTo(p.x, p.y);
    g.lineTo(end.x, end.y);
    g.stroke();

    const head = g.createRadialGradient(p.x, p.y, 0, p.x, p.y, radius * 2);
    head.addColorStop(0, 'rgba(214, 250, 240, 1)');
    head.addColorStop(1, 'rgba(160, 232, 216, 0)');
    g.fillStyle = head;
    g.beginPath();
    g.arc(p.x, p.y, radius * 2, 0, Math.PI * 2);
    g.fill();
  }

  /** The Moon with its phase: the bright limb points at position angle χ, the terminator is an ellipse. */
  private drawMoon(p: ScreenPoint, radius: number, j2000: Vec3, body: MovingBody): void {
    const g = this.g;
    g.fillStyle = '#20242c';
    g.beginPath();
    g.arc(p.x, p.y, radius, 0, Math.PI * 2);
    g.fill();

    const axes = this.screenAxes(j2000, p);
    const chi = (body.brightLimbAngleDegrees * Math.PI) / 180;
    const direction = axes
      ? {
          x: Math.cos(chi) * axes.north.x + Math.sin(chi) * axes.east.x,
          y: Math.cos(chi) * axes.north.y + Math.sin(chi) * axes.east.y,
        }
      : { x: 1, y: 0 };
    const cosI = Math.cos((body.phaseAngleDegrees * Math.PI) / 180);

    g.save();
    g.translate(p.x, p.y);
    g.rotate(Math.atan2(direction.y, direction.x));
    g.fillStyle = '#e8e3c8';
    g.beginPath();
    g.moveTo(0, -radius);
    g.arc(0, 0, radius, -Math.PI / 2, Math.PI / 2, false);
    g.ellipse(0, 0, Math.max(0.01, radius * Math.abs(cosI)), radius, 0, Math.PI / 2, -Math.PI / 2, cosI < 0);
    g.closePath();
    g.fill();
    g.restore();
  }
}
