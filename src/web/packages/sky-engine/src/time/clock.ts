// Sky time inside the renderer. C# owns the clock (setTime / setTimeRate); between messages the
// renderer advances it locally so that animation is smooth, and C# re-syncs it periodically.

export type Now = () => number;

export class SkyClock {
  private baseUtcMs: number;
  private basePerfMs: number;
  private rateValue: number;

  constructor(
    private readonly perfNow: Now = () => performance.now(),
    utcMs = Date.now(),
    rate = 1,
  ) {
    this.baseUtcMs = utcMs;
    this.basePerfMs = perfNow();
    this.rateValue = rate;
  }

  get rate(): number {
    return this.rateValue;
  }

  /** Current sky time, milliseconds since the Unix epoch (UTC). */
  now(): number {
    return this.baseUtcMs + this.rateValue * (this.perfNow() - this.basePerfMs);
  }

  set(utcMs: number, rate: number): void {
    this.baseUtcMs = utcMs;
    this.basePerfMs = this.perfNow();
    this.rateValue = rate;
  }

  setRate(rate: number): void {
    this.set(this.now(), rate);
  }
}
