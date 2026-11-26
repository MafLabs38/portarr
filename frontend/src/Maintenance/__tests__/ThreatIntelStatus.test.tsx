import { render, screen } from '@testing-library/react';
import ThreatIntelStatus from 'Maintenance/ThreatIntelStatus';

const configured = {
  configured: true,
  readAt: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
  error: null,
  kevUpdatedAt: null,
  epssScoreDate: null,
};

describe('the status line of the exploit feeds (D-133)', () => {
  it('says nothing while the feeds are off or absent', () => {
    const { container, rerender } = render(<ThreatIntelStatus threatIntel={{ ...configured, configured: false }} />);
    expect(container).toBeEmptyDOMElement();

    rerender(<ThreatIntelStatus threatIntel={undefined} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('says when the feeds were read, or that they were not yet', () => {
    const { rerender } = render(<ThreatIntelStatus threatIntel={configured} />);
    expect(screen.getByText(/^Exploit feeds: read 2 h ago$/)).toBeInTheDocument();

    rerender(<ThreatIntelStatus threatIntel={{ ...configured, readAt: null }} />);
    expect(screen.getByText('Exploit feeds: not read yet')).toBeInTheDocument();
  });

  it('says why the last read failed, the previous data being kept', () => {
    render(<ThreatIntelStatus threatIntel={{ ...configured, error: 'timeout' }} />);

    expect(screen.getByText('cannot be read: timed out (the last data is kept)')).toBeInTheDocument();
  });
});
