/**
 * A small Markdown subset, enough for the documents Portarr generates itself (the assistant's knowledge base):
 * headings, paragraphs, nested "- " lists and pipe tables. No HTML is ever interpreted: the result is rendered
 * as React elements, so the text cannot inject markup.
 */
export interface ListItem {
  text: string;
  children: ListItem[];
}

export type Block =
  | { type: 'heading'; level: number; text: string }
  | { type: 'paragraph'; text: string }
  | { type: 'list'; items: ListItem[] }
  | { type: 'table'; header: string[]; rows: string[][] };

/** A heading is one to six `#`, a space, then its text. Read by hand: a regex with `\s+(.*)$` can backtrack on a long run of spaces. */
function headingOf(line: string): { level: number; text: string } | null {
  let level = 0;
  while (line[level] === '#') {
    level += 1;
  }
  const next = line[level];
  return level >= 1 && level <= 6 && next !== undefined && next.trim() === ''
    ? { level, text: line.slice(level).trim() }
    : null;
}

const listPattern = /^( *)- (.*)$/;
const separatorCell = /^:?-{3,}:?$/;

function cells(line: string): string[] {
  return line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => cell.trim());
}

/** The line under a table header: dashes (with optional alignment colons) in every cell. Cell by cell, so it cannot backtrack. */
function isSeparator(line: string): boolean {
  return cells(line).every((cell) => separatorCell.test(cell));
}

function parseList(lines: string[]): ListItem[] {
  const root: ListItem[] = [];
  // Each level of nesting is two spaces, as the generator writes them.
  const stack: { depth: number; items: ListItem[] }[] = [{ depth: -1, items: root }];
  for (const line of lines) {
    const match = listPattern.exec(line);
    if (!match) {
      continue;
    }
    const depth = Math.floor((match[1] ?? '').length / 2);
    const item: ListItem = { text: match[2] ?? '', children: [] };
    while (stack.length > 1 && (stack.at(-1)?.depth ?? -1) >= depth) {
      stack.pop();
    }
    const parent = stack.at(-1);
    parent?.items.push(item);
    stack.push({ depth, items: item.children });
  }
  return root;
}

export default function parseMarkdown(markdown: string): Block[] {
  const lines = markdown.split('\n');
  const blocks: Block[] = [];
  let index = 0;

  const take = (accepts: (line: string) => boolean) => {
    const taken: string[] = [];
    while (index < lines.length && accepts(lines[index] ?? '')) {
      taken.push(lines[index] ?? '');
      index += 1;
    }
    return taken;
  };

  while (index < lines.length) {
    const line = lines[index] ?? '';
    const heading = headingOf(line);
    if (line.trim() === '') {
      index += 1;
    } else if (heading) {
      blocks.push({ type: 'heading', level: heading.level, text: heading.text });
      index += 1;
    } else if (listPattern.test(line)) {
      blocks.push({ type: 'list', items: parseList(take((l) => listPattern.test(l))) });
    } else if (line.trimStart().startsWith('|')) {
      const rows = take((l) => l.trimStart().startsWith('|')).filter((l) => !isSeparator(l.trim()));
      blocks.push({ type: 'table', header: cells(rows[0] ?? ''), rows: rows.slice(1).map(cells) });
    } else {
      const text = take(
        (l) => l.trim() !== '' && !headingOf(l) && !listPattern.test(l) && !l.trimStart().startsWith('|'),
      );
      blocks.push({ type: 'paragraph', text: text.join(' ') });
    }
  }
  return blocks;
}
