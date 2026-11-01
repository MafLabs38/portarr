import { render, screen } from '@testing-library/react';

import MarkdownView from 'Components/Markdown/MarkdownView';

const text = 'See https://sonarr.homelab.example/ and **bold** and `code`.';

describe('MarkdownView', () => {
  it("links the URLs of Portarr's own documents", () => {
    render(<MarkdownView markdown={text} />);

    expect(screen.getByRole('link', { name: 'https://sonarr.homelab.example/' })).toHaveAttribute('rel', 'noreferrer');
  });

  it('shows the URLs of what a model wrote as text, in a paragraph, a list and a table', () => {
    render(
      <MarkdownView
        markdown={[
          'A https://a.example/x?d=secret',
          '',
          '- B https://b.example/',
          '',
          '| h |',
          '| --- |',
          '| https://c.example/ |',
        ].join('\n')}
        plainUrls
      />,
    );

    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.getByText(/https:\/\/a\.example\/x\?d=secret/)).toBeInTheDocument();
    expect(screen.getByText(/https:\/\/b\.example\//)).toBeInTheDocument();
    expect(screen.getByText('https://c.example/')).toBeInTheDocument();
  });

  it('still renders bold and code with plain URLs', () => {
    const { container } = render(<MarkdownView markdown={text} plainUrls />);

    expect(container.querySelector('strong')).toHaveTextContent('bold');
    expect(container.querySelector('code')).toHaveTextContent('code');
  });
});
