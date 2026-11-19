// Frame statistics: rolling FPS and 95th percentile frame time (spec §4.2 performance budget).

export class FrameStats {
  private readonly times: number[] = [];
  private last: number | null = null;
  private total = 0;

  constructor(private readonly capacity = 600) {}

  /** Records a frame at `nowMs` (performance.now()). */
  tick(nowMs: number): void {
    if (this.last !== null) {
      this.times.push(nowMs - this.last);
      if (this.times.length > this.capacity) {
        this.times.shift();
      }
    }
    this.last = nowMs;
    this.total++;
  }

  get frames(): number {
    return this.total;
  }

  get fps(): number {
    if (this.times.length === 0) {
      return 0;
    }
    const mean = this.times.reduce((sum, t) => sum + t, 0) / this.times.length;
    return mean > 0 ? 1000 / mean : 0;
  }

  get p95FrameMs(): number {
    if (this.times.length === 0) {
      return 0;
    }
    const sorted = [...this.times].sort((a, b) => a - b);
    return sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.95))] ?? 0;
  }

  reset(): void {
    this.times.length = 0;
    this.last = null;
    this.total = 0;
  }
}
