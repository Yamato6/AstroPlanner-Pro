// Screen-space picking: the renderer projects candidates, this picks the best one near the pointer.

export interface PickCandidate {
  readonly kind: 'star' | 'dso' | 'body';
  readonly id: string;
  readonly label: string | null;
  readonly x: number;
  readonly y: number;
  /** Larger wins among candidates at a similar distance (bodies > selected/plan DSOs > bright stars). */
  readonly weight: number;
  /** Extra pick radius in pixels (large discs and DSO markers). */
  readonly radius: number;
}

/**
 * Picks the candidate closest to (x, y), measured from its edge, within `tolerance` pixels; the
 * weight breaks near ties so that a planet wins over a faint star next to it.
 */
export function pickNearest(
  candidates: Iterable<PickCandidate>,
  x: number,
  y: number,
  tolerance = 8,
): PickCandidate | null {
  let best: PickCandidate | null = null;
  let bestScore = Number.POSITIVE_INFINITY;
  for (const candidate of candidates) {
    const distance = Math.max(0, Math.hypot(candidate.x - x, candidate.y - y) - candidate.radius);
    if (distance > tolerance) {
      continue;
    }
    const score = distance - candidate.weight;
    if (score < bestScore) {
      best = candidate;
      bestScore = score;
    }
  }
  return best;
}
