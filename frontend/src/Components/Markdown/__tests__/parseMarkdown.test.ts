import parseMarkdown from 'Components/Markdown/parseMarkdown';

describe('parseMarkdown', () => {
  it('nests list items by two-space indentation and skips table separators', () => {
    const blocks = parseMarkdown(
      ['- a', '  - b', '    - c', '- d', '', '| x | y |', '|---|---|', '| 1 | 2 |'].join('\n'),
    );

    expect(blocks[0]).toEqual({
      type: 'list',
      items: [
        { text: 'a', children: [{ text: 'b', children: [{ text: 'c', children: [] }] }] },
        { text: 'd', children: [] },
      ],
    });
    expect(blocks[1]).toEqual({ type: 'table', header: ['x', 'y'], rows: [['1', '2']] });
  });

  it('skips aligned separators and answers at once to a long line that is almost one', () => {
    const aligned = parseMarkdown(['| x | y |', '| :--- | ---: |', '| 1 | 2 |'].join('\n'));
    expect(aligned[0]).toEqual({ type: 'table', header: ['x', 'y'], rows: [['1', '2']] });

    // The answer is what matters, not a clock: a backtracking pattern would hang here and the test timeout would catch it.
    const blocks = parseMarkdown(['| x |', `|${'---|'.repeat(2_000)}x`, '| 1 |'].join('\n'));
    expect(blocks[0]).toEqual(expect.objectContaining({ type: 'table', header: ['x'] }));
  });

  it('keeps markup as text: nothing is interpreted as HTML', () => {
    expect(parseMarkdown('<img src=x onerror=alert(1)>')).toEqual([
      { type: 'paragraph', text: '<img src=x onerror=alert(1)>' },
    ]);
  });
});
