import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ReleaseVulnerabilityCell from 'Maintenance/ReleaseVulnerabilityCell';

import renderWithProviders from '__mocks__/renderWithProviders';
import { maintenanceReport } from '__mocks__/server';
import type { ReleaseVulnerabilities, Repository } from 'typings/Maintenance';

const found = maintenanceReport.repositories.find((r) => r.releaseVulnerabilities !== null);
if (!found?.releaseVulnerabilities) {
  throw new Error('The mock report needs a repository with a scanned release.');
}
const shop: Repository = found;
const scanned: ReleaseVulnerabilities = found.releaseVulnerabilities;

function render(release: ReleaseVulnerabilities | null, fullName = shop.fullName) {
  return renderWithProviders(
    <ReleaseVulnerabilityCell repository={{ ...shop, fullName, releaseVulnerabilities: release }} />,
  );
}

describe('vulnerabilities of the latest release (D-132)', () => {
  it('names the scanned build, shows the severities and opens the worst findings', async () => {
    const user = userEvent.setup();
    render(scanned);

    expect(screen.getByText('Latest release v2.1.0')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Vulnerabilities: 1 critical, 2 high' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Details: vulnerabilities of example/shop v2.1.0' }));

    const link = screen.getByRole('link', { name: 'CVE-2026-0001' });
    expect(link).toHaveAttribute('href', 'https://advisories.example/CVE-2026-0001');
    expect(within(screen.getByRole('list')).getAllByRole('listitem')).toHaveLength(1);
  });

  it('says where the release was read from when the catalog maps the repository', () => {
    render({ ...scanned, releaseRepository: 'example/shop-releases' });

    expect(screen.getByText('Latest release v2.1.0, read from example/shop-releases')).toBeInTheDocument();
  });

  it('says that no SBOM is published, never "no vulnerability"', () => {
    render({
      ...scanned,
      state: 'not-scanned',
      reason: 'no-sbom',
      version: null,
      counts: { ...scanned.counts, critical: 0, high: 0, total: 0 },
      findings: [],
    });

    expect(screen.getByText('Latest release: no SBOM published')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it.each([
    ['not-found', 'Latest release: none published, or not readable'],
    ['unauthorized', 'Latest release: access refused'],
  ])('says in release words why a release was not scanned (%s)', (reason, text) => {
    render({ ...scanned, state: 'not-scanned', reason, version: null, findings: [] });

    expect(screen.getByText(text)).toBeInTheDocument();
  });

  it.each([
    ['not-found', 'Latest release: none published, or not readable'],
    ['unauthorized', 'Latest release: access refused'],
  ])('says in release words why a release was not scanned (%s)', (reason, text) => {
    render({ ...scanned, state: 'not-scanned', reason, version: null, findings: [] });

    expect(screen.getByText(text)).toBeInTheDocument();
  });

  it('shows nothing without a release to scan or a declared scanner', () => {
    const { container, rerender } = render(null);
    expect(container).toBeEmptyDOMElement();

    rerender(
      <ReleaseVulnerabilityCell repository={{ ...shop, releaseVulnerabilities: { ...scanned, state: 'off' } }} />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
