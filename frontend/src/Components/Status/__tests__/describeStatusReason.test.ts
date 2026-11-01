import describeStatusReason from 'Components/Status/describeStatusReason';
import type Status from 'typings/Status';

const down = (reason: string, reasonArgs?: string[]): Status => ({ status: 'Down', reason, reasonArgs });

describe('describeStatusReason', () => {
  it.each([
    ['timeout', undefined, 'Timeout'],
    ['dns-failure', undefined, 'DNS lookup failed'],
    ['keyword-missing', undefined, 'Keyword not found'],
    ['http-status', ['502'], 'HTTP 502'],
    ['certificate-expires-in-days', ['6'], 'Certificate expires in 6 days'],
    ['certificate-expires-today', undefined, 'Certificate expires today'],
    ['check-up', ['200', '140'], 'HTTP 200 in 140 ms'],
  ])('says the code %s in words', (reason, args, text) => {
    expect(describeStatusReason(down(reason, args))).toBe(text);
  });

  it('shows a reason that is not a code as the sentence it is', () => {
    expect(describeStatusReason(down('Exited (1) 2 minutes ago'))).toBe('Exited (1) 2 minutes ago');
  });
});
