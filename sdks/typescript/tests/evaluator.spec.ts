import { describe, expect, it } from 'vitest';
import { ConfigCache } from '../src/cache.js';
import { Evaluator, matchValue } from '../src/evaluator.js';
import { compareSemVer, parseSemVer } from '../src/semver.js';
import type { ClauseOperator } from '../src/types.js';
import { sampleConfig } from './fixtures.js';

function evaluator(): Evaluator {
  const cache = new ConfigCache(60_000);
  cache.replaceAll(sampleConfig());
  return new Evaluator(cache);
}

describe('Evaluator', () => {
  it('matches segment members via inSegment (included, rule, excluded)', () => {
    const e = evaluator();
    expect(e.evaluate('new-ui', { userId: 'ana' })).toMatchObject({ reason: 'ruleMatch', ruleId: 'staff', value: true });
    expect(e.evaluate('new-ui', { userId: 'zoe', attributes: { email: 'zoe@featureflare.dev' } })).toMatchObject({
      ruleId: 'staff',
    });
    expect(e.evaluate('new-ui', { userId: 'bob', attributes: { email: 'bob@featureflare.dev' } })).not.toMatchObject({
      ruleId: 'staff',
    });
  });

  it('applies semver rules and percentage rollouts', () => {
    const e = evaluator();
    expect(e.evaluate('new-ui', { userId: 'u1', attributes: { appVersion: '1.9.0' } })).toMatchObject({
      reason: 'ruleMatch',
      variation: 'off',
    });

    let on = 0;
    for (let i = 0; i < 10_000; i++) {
      if (e.evaluate('new-ui', { userId: `user-${i}` }).variation === 'on') on++;
    }
    expect(on / 10_000).toBeGreaterThan(0.22);
    expect(on / 10_000).toBeLessThan(0.28);
  });

  it('serves the off variation for disabled flags and reports unknown flags', () => {
    const e = evaluator();
    expect(e.evaluate('max-items', { userId: 'u1' })).toMatchObject({ reason: 'off', value: 10 });
    expect(e.evaluate('ghost', { userId: 'u1' })).toMatchObject({ reason: 'flagNotFound', variation: null });
  });

  it('never matches a missing attribute, even when negated', () => {
    const cache = new ConfigCache(60_000);
    const config = sampleConfig();
    config.flags[0]!.rules = [
      {
        id: 'not-br',
        clauses: [{ attribute: 'country', operator: 'in', values: ['BR'], negate: true }],
        serve: { variation: 'on' },
      },
    ];
    config.flags[0]!.fallthrough = { variation: 'off' };
    cache.replaceAll(config);
    const e = new Evaluator(cache);

    expect(e.evaluate('new-ui', { userId: 'u1' }).reason).toBe('fallthrough');
    expect(e.evaluate('new-ui', { userId: 'u1', attributes: { country: 'US' } }).reason).toBe('ruleMatch');
  });

  it('assigns experiment variants deterministically', () => {
    const e = evaluator();
    const first = e.assign('checkout', 'user-42');
    expect(first.reason).toBe('assigned');
    expect(e.assign('checkout', 'user-42')).toEqual(first);
    expect(e.assign('nope', 'user-42').reason).toBe('experimentNotFound');
  });
});

describe('operators', () => {
  it.each<[ClauseOperator, string, string, boolean]>([
    ['in', 'BR', 'BR', true],
    ['in', 'br', 'BR', false],
    ['contains', 'ana@acme.com', '@acme', true],
    ['matches', 'ana@acme.com', '^[a-z]+@acme\\.com$', true],
    ['matches', 'x', '(invalid', false],
    ['lessThan', '9', '10', true],
    ['greaterThanOrEqual', 'abc', '10', false],
    ['semVerEqual', '2', '2.0.0', true],
    ['semVerLessThan', '2.0.0-beta.2', '2.0.0', true],
    ['semVerGreaterThan', '2.0.0-beta.11', '2.0.0-beta.2', true],
  ])('%s(%s, %s) = %s', (op, actual, expected, result) => {
    expect(matchValue(op, actual, expected)).toBe(result);
  });

  it('parses lenient semver', () => {
    expect(parseSemVer('v1.2')).toEqual({ major: 1, minor: 2, patch: 0, preRelease: [] });
    expect(parseSemVer('1.2.3.4')).toBeNull();
    expect(compareSemVer(parseSemVer('1.0.0+build.5')!, parseSemVer('1.0.0')!)).toBe(0);
  });
});
