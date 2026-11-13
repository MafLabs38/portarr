import { hostMemoryParts } from 'Infrastructure/machineUsage';

import type { HostMetrics } from 'typings/Infrastructure';

const GB = 1_000_000_000;

function host(used: number, reclaimable: number, free: number): HostMetrics {
  return {
    memoryTotalBytes: used + reclaimable + free,
    memoryUsedBytes: used,
    memoryReclaimableBytes: reclaimable,
    memoryFreeBytes: free,
  } as HostMetrics;
}

describe('hostMemoryParts', () => {
  it('counts the reclaimable cache as free, because the kernel gives it back on demand', () => {
    const parts = hostMemoryParts(host(49 * GB, 12 * GB, 1 * GB), null, 9 * GB);

    expect(parts.map((part) => part.key)).toEqual(['system', 'consumers', 'free']);
    const free = parts.find((part) => part.key === 'free');
    expect(free).toEqual({ key: 'free', bytes: 13 * GB, cacheBytes: 12 * GB });
  });

  it('keeps the parts summing to the total of the machine', () => {
    const parts = hostMemoryParts(host(40 * GB, 10 * GB, 2 * GB), 20 * GB, 5 * GB);

    expect(parts.reduce((sum, part) => sum + part.bytes, 0)).toBe(52 * GB);
    expect(parts.map((part) => part.key)).toEqual(['system', 'guests', 'consumers', 'free']);
  });

  it('splits the VMs from the system only when the used memory can hold them', () => {
    const parts = hostMemoryParts(host(10 * GB, 0, 5 * GB), 30 * GB, 2 * GB);

    expect(parts.map((part) => part.key)).toEqual(['guestsSystem', 'consumers', 'free']);
    expect(parts.find((part) => part.key === 'free')).toEqual({ key: 'free', bytes: 5 * GB, cacheBytes: 0 });
  });
});
