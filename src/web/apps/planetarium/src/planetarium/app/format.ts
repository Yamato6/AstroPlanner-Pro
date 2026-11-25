// Formatting of numbers the page computes itself (times on the page's clock, the point under the
// cursor). Everything the host computes arrives formatted from C#.

/** The site's local time of a UTC instant, "21:42". */
export function siteTime(utcMs: number, timeZone: string): string {
  try {
    return new Intl.DateTimeFormat('en-GB', {
      timeZone,
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).format(utcMs);
  } catch {
    return new Date(utcMs).toISOString().slice(11, 16); // unknown zone: UTC rather than nothing
  }
}

/** The site's local date of a UTC instant, "Thu 15 Jan". */
export function siteDate(utcMs: number, timeZone: string): string {
  try {
    return new Intl.DateTimeFormat('en-GB', {
      timeZone,
      weekday: 'short',
      day: 'numeric',
      month: 'short',
    }).format(utcMs);
  } catch {
    return new Date(utcMs).toISOString().slice(0, 10);
  }
}

const pad = (value: number): string => String(value).padStart(2, '0');

/** Right ascension in hours as "05h 35m 17s". */
export function formatRa(hours: number): string {
  const total = Math.round((((hours % 24) + 24) % 24) * 3600) % 86400;
  return `${pad(Math.floor(total / 3600))}h ${pad(Math.floor(total / 60) % 60)}m ${pad(total % 60)}s`;
}

/** Declination in degrees as "+05° 23′ 28″" (a true minus sign below the equator). */
export function formatDec(degrees: number): string {
  const total = Math.round(Math.abs(degrees) * 3600);
  const sign = degrees < 0 && total > 0 ? '−' : '+';
  return `${sign}${pad(Math.floor(total / 3600))}° ${pad(Math.floor(total / 60) % 60)}′ ${pad(total % 60)}″`;
}

/** An angle with one decimal and a degree sign, "48.2°" (a true minus sign). */
export function formatDegrees(degrees: number, decimals = 1): string {
  const text = degrees.toFixed(decimals);
  return `${text.startsWith('-') ? `−${text.slice(1)}` : text}°`;
}

/** "RA 05h 35m 17s, Dec −05° 23′ 28″", the text "Copy coordinates" puts on the clipboard. */
export function formatRaDec(raHours: number, decDegrees: number): string {
  return `RA ${formatRa(raHours)}, Dec ${formatDec(decDegrees)}`;
}
