import { describeLastSuccess } from 'Data/describeCopy';

import type { DataCopy } from 'typings/Data';

const now = Date.parse('2026-01-31T12:00:00Z');

function copy(overrides: Partial<DataCopy>): DataCopy {
  return {
    state: 'protected',
    reason: null,
    lastSuccessAt: null,
    lastRunAt: null,
    lastResult: null,
    sizeBytes: null,
    manual: false,
    manualLastSuccess: null,
    sources: [],
    nextRunAt: null,
    ...overrides,
  };
}

describe('describeLastSuccess', () => {
  it('says when the last success was', () => {
    expect(describeLastSuccess(copy({ lastSuccessAt: '2026-01-31T09:00:00Z' }), now)).toBe('last success 3 h ago');
  });

  it('says a job that never ran is waiting, with its next run when the schedule gives one', () => {
    expect(describeLastSuccess(copy({ state: 'waiting', nextRunAt: '2026-01-31T15:00:00Z' }), now)).toBe(
      'has not run yet · next run in 3 h',
    );
    expect(describeLastSuccess(copy({ state: 'waiting' }), now)).toBe('has not run yet');
  });

  it('asks to set the tool up rather than claiming a copy never succeeded', () => {
    expect(describeLastSuccess(copy({ state: 'not-set-up' }), now)).toBe('set up the backup tool on this environment');
  });

  it('says a copy that is declared but never succeeded', () => {
    expect(describeLastSuccess(copy({ state: 'missing' }), now)).toBe('never succeeded');
  });
});
