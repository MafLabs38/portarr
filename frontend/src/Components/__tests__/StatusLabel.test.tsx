import { render, screen } from '@testing-library/react';

import AppIcon from 'Components/AppIcon/AppIcon';
import StatusLabel from 'Components/Status/StatusLabel';

describe('StatusLabel', () => {
  it('always shows the status as text, with the reason as tooltip', () => {
    render(<StatusLabel status={{ status: 'Down', reason: 'Exited (1)' }} />);

    expect(screen.getByText('Down').parentElement).toHaveAttribute('title', 'Exited (1)');
  });

  it('can show the reason next to the status', () => {
    render(<StatusLabel showReason status={{ status: 'Degraded', reason: 'Recyclarr not healthy' }} />);

    expect(screen.getByText('· Recyclarr not healthy')).toBeInTheDocument();
  });
});

describe('AppIcon', () => {
  it('asks Portarr for the icon, never a third-party site', () => {
    const { container } = render(<AppIcon icon="jellyfin" name="Jellyfin" size={32} />);

    expect(container.querySelector('img')).toHaveAttribute('src', '/api/v1/icons/jellyfin');
  });

  it('accepts a full URL', () => {
    const { container } = render(<AppIcon icon="https://icons.homelab.example/x.png" name="X" size={32} />);

    expect(container.querySelector('img')).toHaveAttribute('src', 'https://icons.homelab.example/x.png');
  });

  it('falls back to the initial without an icon', () => {
    render(<AppIcon icon={null} name="grafana" size={32} />);

    expect(screen.getByText('G')).toBeInTheDocument();
  });
});
