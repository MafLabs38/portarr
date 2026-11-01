import formatBytes from 'Utilities/Number/formatBytes';
import formatCores from 'Utilities/Number/formatCores';

describe('formatBytes', () => {
  it.each([
    [-1, '0 B'],
    [512, '512 B'],
    [138_522_624, '132 MB'],
    [1_610_612_736, '1.5 GB'],
  ])('formats %d as %s', (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected);
  });
});

describe('formatCores', () => {
  it('keeps three decimals for an almost idle container', () => {
    expect(formatCores(0.002)).toBe('0.002 vCPU');
  });

  it('uses two decimals otherwise', () => {
    expect(formatCores(1.5)).toBe('1.50 vCPU');
  });
});
