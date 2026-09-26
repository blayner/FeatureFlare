import type { SdkConfig } from '../src/types.js';

/** Same configuration as the .NET SDK tests (sdks/dotnet/tests/.../EvaluatorTests.cs). */
export function sampleConfig(): SdkConfig {
  return {
    flags: [
      {
        key: 'new-ui',
        type: 'boolean',
        enabled: true,
        salt: 's1',
        version: 3,
        variations: [
          { key: 'on', value: true },
          { key: 'off', value: false },
        ],
        offVariation: 'off',
        fallthrough: {
          variation: null,
          rollout: {
            variations: [
              { variation: 'on', weight: 2500 },
              { variation: 'off', weight: 7500 },
            ],
            bucketBy: null,
          },
        },
        rules: [
          {
            id: 'staff',
            clauses: [{ attribute: 'key', operator: 'inSegment', values: ['staff'], negate: false }],
            serve: { variation: 'on' },
          },
          {
            id: 'old-app',
            clauses: [{ attribute: 'appVersion', operator: 'semVerLessThan', values: ['2.0.0'], negate: false }],
            serve: { variation: 'off' },
          },
        ],
      },
      {
        key: 'max-items',
        type: 'number',
        enabled: false,
        salt: 's2',
        version: 1,
        variations: [
          { key: 'low', value: 10 },
          { key: 'high', value: 50 },
        ],
        offVariation: 'low',
        fallthrough: { variation: 'high' },
        rules: [],
      },
    ],
    segments: [
      {
        key: 'staff',
        included: ['ana'],
        excluded: ['bob'],
        version: 1,
        rules: [{ clauses: [{ attribute: 'email', operator: 'endsWith', values: ['@featureflare.dev'] }] }],
      },
    ],
    experiments: [
      {
        key: 'checkout',
        status: 'running',
        trafficAllocation: 10000,
        salt: 'x',
        controlVariant: 'control',
        primaryMetric: 'purchase',
        version: 2,
        variants: [
          { key: 'control', weight: 5000 },
          { key: 'one-page', weight: 5000, payload: { steps: 1 } },
        ],
      },
    ],
    generatedAt: '2026-09-01T12:00:00Z',
  };
}
