import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const read = (path: string) => readFileSync(resolve(process.cwd(), path), 'utf8');

describe('favicon', () => {
  it('is declared by the page', () => {
    expect(read('frontend/index.html')).toContain('<link rel="icon" href="/favicon.svg" type="image/svg+xml" />');
  });

  it('draws the porthole of the header logo', () => {
    const icon = read('frontend/public/favicon.svg');
    const logo = read('frontend/src/Components/Page/PortholeLogo.tsx');

    // The disc, the sunken circle, the ring and the rivets: the same geometry as the React logo.
    for (const shape of ['r="15"', 'r="10"', 'r="7"', 'cy="3.5"', 'r="1.5"']) {
      expect(icon).toContain(shape);
      expect(logo).toContain(shape);
    }
    expect(icon.match(/cy="3\.5"/g)).toHaveLength(4);
  });
});
