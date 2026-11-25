import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import MaintenanceIndex from 'Maintenance/MaintenanceIndex';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { imageVulnerabilitiesReport, maintenanceReport, server } from '__mocks__/server';

function useScanner() {
  server.use(http.get('/api/v1/maintenance', () => HttpResponse.json(imageVulnerabilitiesReport)));
}

describe('image vulnerabilities on the Maintenance page', () => {
  it('shows the worst severities of a scanned image and opens its findings with a toggle', async () => {
    useScanner();
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    const row = await screen.findByRole('listitem', { name: 'ghcr.io/example/shop:1.4' });
    expect(within(row).getByRole('img', { name: 'Vulnerabilities: 1 critical, 14 high' })).toBeInTheDocument();
    const toggle = within(row).getByRole('button', { name: 'Details: vulnerabilities of ghcr.io/example/shop:1.4' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle).not.toHaveAttribute('aria-controls');
    expect(within(row).queryByText('CVE-2026-0001')).not.toBeInTheDocument();

    await user.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const panelId = toggle.getAttribute('aria-controls');
    expect(panelId).toBeTruthy();
    expect(document.getElementById(panelId ?? '')).toContainElement(within(row).getByText('CVE-2026-0001'));
    const findings = within(within(row).getByRole('list', { name: 'Vulnerabilities of ghcr.io/example/shop:1.4' }));
    expect(findings.getAllByRole('listitem')).toHaveLength(4);
    expect(within(row).getByText(/^scanned \d+ h ago$/)).toBeInTheDocument();
    expect(within(row).getByText('11 with a fix')).toBeInTheDocument();
    expect(within(row).getByText('4 of 15 shown')).toBeInTheDocument();

    await user.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle).not.toHaveAttribute('aria-controls');
    expect(within(row).queryByText('CVE-2026-0001')).not.toBeInTheDocument();
  });

  it('shows each finding with its version change, its fix, and its title as plain text', async () => {
    useScanner();
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    const row = await screen.findByRole('listitem', { name: 'ghcr.io/example/shop:1.4' });
    await user.click(within(row).getByRole('button', { name: /^Details/ }));

    const link = within(row).getByRole('link', { name: 'CVE-2026-0001' });
    expect(link).toHaveAttribute('href', 'https://avd.example/cve-2026-0001');
    expect(link).toHaveAttribute('rel', 'noreferrer');
    expect(link).toHaveAttribute('target', '_blank');
    expect(within(row).getByText('Critical')).toBeInTheDocument();
    expect(within(row).getByText('3.0.2')).toBeInTheDocument();
    expect(within(row).getByText('no fix yet')).toBeInTheDocument();
    expect(within(row).getByText('Unrated')).toBeInTheDocument();

    const long = within(row).getByText(/^Heap over-read when inflating/);
    expect(long).toHaveAttribute('title', expect.stringContaining('larger than the buffer'));

    // Third-party text: markup is shown as text, and only an https address becomes a link.
    expect(within(row).getByText('<img src=x onerror=alert(1)> not markup')).toBeInTheDocument();
    expect(row.querySelector('img')).toBeNull();
    expect(within(row).getByText('GHSA-xxxx-0003')).toBeInTheDocument();
    expect(within(row).queryByRole('link', { name: 'GHSA-xxxx-0003' })).not.toBeInTheDocument();
  });

  it('shows medium and low when nothing is critical or high, and says when nothing is shown at the threshold', async () => {
    useScanner();
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    const row = await screen.findByRole('listitem', { name: 'ghcr.io/example/blog:2.0' });
    expect(within(row).getByRole('img', { name: 'Vulnerabilities: 3 medium, 1 low' })).toBeInTheDocument();

    await user.click(within(row).getByRole('button', { name: /^Details/ }));

    expect(within(row).getByText('No vulnerability at or above the threshold.')).toBeInTheDocument();
    expect(within(row).getByText('0 with a fix')).toBeInTheDocument();
  });

  it('has nothing to open for a clean image', async () => {
    useScanner();
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    const row = await screen.findByRole('listitem', { name: 'ghcr.io/example/clean:1.0' });

    expect(within(row).getByRole('img', { name: /^Vulnerabilities:/ })).toBeInTheDocument();
    expect(within(row).queryByRole('button')).not.toBeInTheDocument();
  });

  it.each([
    ['ghcr.io/example/queue:1.0', 'Scanning soon'],
    ['ghcr.io/example/broken:1.0', 'Scan failed: timed out'],
    ['local-build:1', 'Not pullable (local build or private)'],
    ['ghcr.io/example/private:1.0', 'Registry refused'],
    ['by-id', 'Local image id'],
  ])('says why %s has no count: %s', async (name, text) => {
    useScanner();
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    const row = await screen.findByRole('listitem', { name });

    expect(within(row).getByText(text)).toBeInTheDocument();
    expect(within(row).queryByRole('button')).not.toBeInTheDocument();
  });

  it('says an unsupported or unlisted reason without breaking', async () => {
    const images = imageVulnerabilitiesReport.images.map((image) => {
      if (image.reference === 'local-build:1') {
        return { ...image, vulnerabilities: { ...image.vulnerabilities, reason: 'unsupported' } };
      }
      if (image.reference === 'ghcr.io/example/private:1.0') {
        return { ...image, vulnerabilities: { ...image.vulnerabilities, reason: 'something-new' } };
      }
      if (image.reference === 'ghcr.io/example/broken:1.0') {
        return { ...image, vulnerabilities: { ...image.vulnerabilities, reason: 'http-502' } };
      }
      return image;
    });
    server.use(http.get('/api/v1/maintenance', () => HttpResponse.json({ ...imageVulnerabilitiesReport, images })));
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    expect(
      within(await screen.findByRole('listitem', { name: 'local-build:1' })).getByText('Nothing to scan'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('listitem', { name: 'ghcr.io/example/private:1.0' })).getByText('Not scanned'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('listitem', { name: 'ghcr.io/example/broken:1.0' })).getByText('Scan failed: http-502'),
    ).toBeInTheDocument();
  });

  it('shows nothing about vulnerabilities while no scanner is declared', async () => {
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    const row = await screen.findByRole('listitem', { name: 'postgres:16-alpine' });

    expect(within(row).queryByText(/Vulnerabilities/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Image scanner/)).not.toBeInTheDocument();
    expect(screen.queryByRole('listitem', { name: /^Scanned images/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('listitem', { name: /^Critical vulnerabilities/ })).not.toBeInTheDocument();
  });

  it('says in one line when the scanner was read, how old its database is, and why it cannot be read', async () => {
    useScanner();
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    const line = await screen.findByText(/^Image scanner: read \d+ h ago/);

    expect(line.parentElement).toHaveTextContent('vulnerability database updated 3 h ago');
    expect(line.parentElement).toHaveTextContent(
      'cannot be read: connection refused or host down (the last results are kept)',
    );
  });

  it('says a scanner that was never read, and has no error to report when it answers', async () => {
    const scanner = { configured: true, readAt: null, error: null, databaseUpdatedAt: null };
    server.use(
      http.get('/api/v1/maintenance', () => HttpResponse.json({ ...maintenanceReport, imageScanner: scanner })),
    );
    renderWithProviders(<MaintenanceIndex />, '/?tab=images');

    expect(await screen.findByText('Image scanner: not read yet')).toBeInTheDocument();
    expect(screen.queryByText(/cannot be read/)).not.toBeInTheDocument();
    expect(screen.queryByText(/vulnerability database/)).not.toBeInTheDocument();
  });

  it('adds the vulnerability figures to the images group, and filters the images from them', async () => {
    useScanner();
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />);

    expect(await screen.findByRole('listitem', { name: 'Scanned images: 2/8' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Vulnerable images: 1' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Critical vulnerabilities: 1' })).toBeInTheDocument();
    const high = screen.getByRole('listitem', { name: 'High vulnerabilities: 14' });

    await user.click(within(high).getByRole('link'));

    expect(screen.getByText('Showing only: High vulnerabilities')).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'ghcr.io/example/shop:1.4' })).toBeInTheDocument();
    expect(screen.queryByRole('listitem', { name: 'ghcr.io/example/blog:2.0' })).not.toBeInTheDocument();
  });

  it('names the three new kinds of attention and links the first two to the images concerned', async () => {
    useScanner();
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />);

    const list = await screen.findByRole('list', { name: 'Needs attention' });
    const items = within(list).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent(
      'Critical vulnerabilities with a fix in 1 running images (11 critical or high in all)',
    );
    expect(within(items[0] as HTMLElement).getByText('ghcr.io/example/shop:1.4 (11)')).toBeInTheDocument();
    expect(items[1]).toHaveTextContent('2 high vulnerabilities with a fix in 1 running images');
    expect(items[2]).toHaveTextContent('The image scanner could not be read: connection refused or host down.');
    expect(items[2]).not.toHaveTextContent('unreachable');

    await user.click(within(items[1] as HTMLElement).getByRole('link', { name: /high vulnerabilities with a fix/ }));

    expect(screen.getByText('Showing only: Critical or high vulnerabilities with a fix')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Deployed images/ })).toHaveAttribute('aria-selected', 'true');
  });

  it('opens the images tab, without a filter, from the scanner error', async () => {
    useScanner();
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />);

    const list = await screen.findByRole('list', { name: 'Needs attention' });
    await user.click(within(list).getByRole('link', { name: /The image scanner could not be read/ }));

    expect(screen.getByRole('tab', { name: /Deployed images/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByText(/Showing only/)).not.toBeInTheDocument();
  });
});
