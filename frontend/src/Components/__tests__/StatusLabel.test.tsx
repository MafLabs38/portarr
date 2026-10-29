import { render, screen } from '@testing-library/react';

import StatusLabel from 'Components/Status/StatusLabel';

describe('StatusLabel', () => {
  it('always shows the status as text, with the reason as tooltip', () => {
    render(<StatusLabel status={{ status: 'Down', reason: 'Exited (1)' }} />);

    expect(screen.getByTitle('Exited (1)')).toHaveTextContent('Down');
    expect(screen.getByText('Down')).not.toHaveClass('visuallyHidden');
  });

  it('can show the reason next to the status', () => {
    render(<StatusLabel showReason status={{ status: 'Degraded', reason: 'Recyclarr not healthy' }} />);

    expect(screen.getByText('· Recyclarr not healthy')).toBeInTheDocument();
  });

  it('shows only the icon when compact, the status text staying for assistive technologies', () => {
    render(<StatusLabel compact status={{ status: 'Healthy', reason: 'Running' }} />);

    expect(screen.getByText('Healthy')).toHaveClass('visuallyHidden');
  });
});
