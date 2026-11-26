import { type ChangeEvent, type FormEvent, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import form from 'Components/Form/Form.module.css';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import type { CatalogEditOutcome, CatalogItem, CatalogSectionName } from 'typings/Catalog';
import translate from 'Utilities/String/translate';

import styles from './Catalog.module.css';
import GuidedFields from './GuidedFields';
import { formatDraft, idPattern, parseDraft, withRemovals } from './itemDraft';
import { useSaveItem, useValidateItem } from './useCatalog';

interface ItemEditorProps {
  section: CatalogSectionName;
  /** The item being changed, or null for a new one. */
  item: CatalogItem | null;
  /** The catalog version read with the item. */
  version: string;
  onClose: () => void;
  /** The item was saved: the panel shows what happened and closes the editor. */
  onSaved: () => void;
}

/** What the last check or save said: the translation key of the message, and the field errors the server gave. */
interface Outcome {
  kind: 'success' | 'info' | 'danger';
  message: string;
  errors?: string[];
}

function outcomeOf(result: CatalogEditOutcome): Outcome {
  switch (result.status) {
    case 'unchanged':
      return { kind: 'info', message: translate('CatalogNothingChanged') };
    case 'valid':
      return { kind: 'success', message: translate('CatalogItemValid') };
    default:
      return { kind: 'success', message: translate('CatalogItemSaved') };
  }
}

const failureKeys: Record<number, string> = {
  400: 'CatalogItemInvalid',
  401: 'IdentitySessionEnded',
  403: 'CatalogNotAllowed',
  409: 'CatalogStale',
};

/** The field errors the server gave (path: message), or its general message. */
function errorLines(error: ApiError): string[] | undefined {
  const fields = Object.entries(error.errors ?? {}).flatMap(([path, list]) => list.map((m) => `${path}: ${m}`));
  if (fields.length > 0) {
    return fields;
  }
  return error.detail ? [error.detail] : undefined;
}

function failureOf(error: unknown): Outcome {
  if (!(error instanceof ApiError)) {
    return { kind: 'danger', message: translate('CatalogSaveFailed') };
  }
  return {
    kind: 'danger',
    message: translate(failureKeys[error.status] ?? 'CatalogSaveFailed'),
    errors: errorLines(error),
  };
}

function OutcomeNotice({ outcome }: Readonly<{ outcome: Outcome }>) {
  if (outcome.kind === 'success') {
    return <output className={`${form.saved} ${styles.outcome}`}>{outcome.message}</output>;
  }
  return (
    <div className={styles.outcome}>
      <Alert kind={outcome.kind === 'info' ? 'info' : 'danger'}>
        {outcome.message}
        {outcome.errors ? (
          <ul className={styles.errors}>
            {outcome.errors.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        ) : null}
      </Alert>
    </div>
  );
}

/**
 * Adds or changes one item of the catalog: its simple fields as inputs, the whole item as JSON underneath (the same keys
 * as the catalog file, docs/catalog.md), a check that writes nothing, and the save.
 */
export default function ItemEditor({ section, item, version, onClose, onSaved }: Readonly<ItemEditorProps>) {
  const creating = item === null;
  const [text, setText] = useState(formatDraft(item ?? { id: '' }));
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const save = useSaveItem();
  const validate = useValidateItem();
  const parsed = parseDraft(text);
  const draft = 'item' in parsed ? parsed.item : null;
  const idProblem = draft && !idPattern.test(String(draft.id ?? '')) ? translate('CatalogIdHelp') : null;
  const busy = save.isPending || validate.isPending;

  const run = async (saving: boolean) => {
    if (!draft || idProblem) {
      return;
    }
    setOutcome(null);
    try {
      const body = { section, item: withRemovals(item, draft), version };
      const result = await (saving ? save : validate).mutateAsync(body);
      setOutcome(outcomeOf(result));
      if (saving && result.status !== 'unchanged') {
        onSaved();
      }
    } catch (error) {
      setOutcome(failureOf(error));
    }
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    void run(true);
  };

  return (
    <form
      aria-label={translate(creating ? 'CatalogAddItem' : 'CatalogEditItem', { id: item?.id ?? '' })}
      className={styles.editor}
      onSubmit={onSubmit}
    >
      <h3 className={styles.editorTitle}>
        {translate(creating ? 'CatalogAddItem' : 'CatalogEditItem', { id: item?.id ?? '' })}
      </h3>
      <div className={form.form}>
        {draft ? (
          <GuidedFields
            creating={creating}
            draft={draft}
            idProblem={idProblem}
            onChange={(next) => setText(formatDraft(next))}
            section={section}
          />
        ) : null}
        <div className={form.field}>
          <label className={form.label} htmlFor="catalog-json">
            {translate('CatalogItemJson')}
          </label>
          <textarea
            aria-describedby="catalog-json-help"
            className={`${form.input} ${styles.json}`}
            id="catalog-json"
            onChange={(event: ChangeEvent<HTMLTextAreaElement>) => setText(event.target.value)}
            spellCheck={false}
            value={text}
          />
          <p className={form.help} id="catalog-json-help">
            {'error' in parsed ? translate(parsed.error) : translate('CatalogItemJsonHelp')}
          </p>
        </div>
        <div className={form.actions}>
          <button className={form.save} disabled={busy || !draft || idProblem !== null} type="submit">
            {translate('Save')}
          </button>
          <button
            className={form.secondary}
            disabled={busy || !draft || idProblem !== null}
            onClick={() => void run(false)}
            type="button"
          >
            {translate('Check')}
          </button>
          <button className={form.secondary} onClick={onClose} type="button">
            {translate('Cancel')}
          </button>
        </div>
      </div>
      {outcome ? <OutcomeNotice outcome={outcome} /> : null}
    </form>
  );
}
