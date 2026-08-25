export interface SemVer {
  major: number;
  minor: number;
  patch: number;
  preRelease: string[];
}

const NUMERIC = /^\d+$/;

/** Lenient parser matching the server: optional "v", missing minor/patch default to 0, build metadata ignored. */
export function parseSemVer(input: string): SemVer | null {
  let text = input.trim();
  if (text.length === 0) return null;
  if (text[0] === 'v' || text[0] === 'V') text = text.slice(1);

  const plus = text.indexOf('+');
  if (plus >= 0) text = text.slice(0, plus);

  let preRelease: string[] = [];
  const dash = text.indexOf('-');
  if (dash >= 0) {
    preRelease = text.slice(dash + 1).split('.');
    text = text.slice(0, dash);
    if (preRelease.some((p) => p.length === 0)) return null;
  }

  const parts = text.split('.');
  if (parts.length === 0 || parts.length > 3 || parts.some((p) => !NUMERIC.test(p))) return null;

  const [major = 0, minor = 0, patch = 0] = parts.map((p) => Number.parseInt(p, 10));
  return { major, minor, patch, preRelease };
}

export function compareSemVer(a: SemVer, b: SemVer): number {
  if (a.major !== b.major) return Math.sign(a.major - b.major);
  if (a.minor !== b.minor) return Math.sign(a.minor - b.minor);
  if (a.patch !== b.patch) return Math.sign(a.patch - b.patch);

  // A release is greater than any of its pre-releases.
  if (a.preRelease.length === 0 || b.preRelease.length === 0) {
    return Math.sign(b.preRelease.length - a.preRelease.length);
  }

  const n = Math.min(a.preRelease.length, b.preRelease.length);
  for (let i = 0; i < n; i++) {
    const result = compareIdentifier(a.preRelease[i]!, b.preRelease[i]!);
    if (result !== 0) return result;
  }
  return Math.sign(a.preRelease.length - b.preRelease.length);
}

function compareIdentifier(a: string, b: string): number {
  const aNum = NUMERIC.test(a);
  const bNum = NUMERIC.test(b);
  if (aNum && bNum) return Math.sign(Number(a) - Number(b));
  if (aNum) return -1;
  if (bNum) return 1;
  return a < b ? -1 : a > b ? 1 : 0;
}
