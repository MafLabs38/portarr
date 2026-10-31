# 0007 — Text-level catalog editing

- **Status**: accepted
- **Date**: 2026-10-31
- **Spec**: D-83 (refines D-44)

## Context

The catalog file is the source of truth (D-44) and is written by hand: it carries comments that explain the fleet, and a style (one-line flow items for apps, block items for data). The maintainer wants the API, and an assistant through MCP, to change it. Re-serializing the document would drop every comment and reformat the file: YamlDotNet's serializer and representation model do not keep comments, and no maintained .NET YAML library round-trips them.

## Decision

Edit at the text level. The editor loads the file with YamlDotNet's representation model only to find where each node starts, compares the edited item with the intended one and splices the difference into the original text:

- a changed scalar or flow collection is re-emitted in place; a block mapping or block list is patched key by key or item by item (kept items keep their text and comments);
- a new key, or a new list item, is inserted after the last one, at the file's indentation, in the style most of the list uses;
- a removed key or item loses its lines;
- a flow item (`- { id: …, name: … }`) is one line and is re-emitted whole; it switches to block style only when it gains multi-line text, and a trailing comment moves to its own line.

The result is read back and compared with the intended document; on a mismatch the whole item is re-emitted, and if that also fails nothing is written. The parser then validates the whole file before the atomic write.

## Consequences

- Comments and formatting outside the edited nodes are kept byte for byte.
- Limits: comments inside a re-emitted node (a flow item spread over lines, a list whose order changed) are lost; a comment line above a removed item stays.
- No database and no second representation: the file stays the only state. Addressing items (section and id) rather than files leaves room to split the catalog into several files later.
