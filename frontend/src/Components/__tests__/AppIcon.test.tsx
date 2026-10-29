import { fireEvent, render, screen } from '@testing-library/react';

import AppIcon from 'Components/AppIcon/AppIcon';

describe('AppIcon', () => {
  it('asks Portarr for the icon, never a third-party site (D-47)', () => {
    const { container } = render(<AppIcon icon="jellyfin" name="Jellyfin" size={32} />);

    expect(container.querySelector('img')).toHaveAttribute('src', '/api/v1/icons/jellyfin');
  });

  it('falls back to the initial when the icon cannot be loaded', () => {
    const { container } = render(<AppIcon icon="unknown-app" name="grafana" size={32} />);

    fireEvent.error(container.querySelector('img') as HTMLImageElement);

    expect(screen.getByText('G')).toBeInTheDocument();
  });
});
