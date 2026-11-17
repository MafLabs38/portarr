import type { ChangeEvent } from 'react';

import form from 'Components/Form/Form.module.css';
import type { CatalogItem, CatalogSectionName } from 'typings/Catalog';
import translate from 'Utilities/String/translate';

import styles from './Catalog.module.css';
import { guidedFields, withField } from './itemDraft';

interface GuidedFieldsProps {
  section: CatalogSectionName;
  draft: CatalogItem;
  creating: boolean;
  idProblem: string | null;
  onChange: (item: CatalogItem) => void;
}

function textOf(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** The simple fields of an item as inputs; they read and write the same item the JSON below shows. */
export default function GuidedFields({ section, draft, creating, idProblem, onChange }: Readonly<GuidedFieldsProps>) {
  return (
    <div className={styles.grid}>
      <div className={form.field}>
        <label className={form.label} htmlFor="catalog-id">
          {translate('Id')}
        </label>
        <input
          aria-describedby="catalog-id-help"
          className={form.input}
          id="catalog-id"
          onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(withField(draft, 'id', event.target.value))}
          readOnly={!creating}
          spellCheck={false}
          type="text"
          value={textOf(draft.id)}
        />
        <p className={form.help} id="catalog-id-help">
          {idProblem ?? translate('CatalogIdHelp')}
        </p>
      </div>
      {guidedFields[section].map((field) => (
        <div className={form.field} key={field.key}>
          <label className={form.label} htmlFor={`catalog-${field.key}`}>
            {translate(field.label)}
          </label>
          {field.options ? (
            <select
              className={form.input}
              id={`catalog-${field.key}`}
              onChange={(event) => onChange(withField(draft, field.key, event.target.value))}
              value={textOf(draft[field.key])}
            >
              {field.options.map((option) => (
                <option key={option} value={option}>
                  {option === '' ? translate('Default') : option}
                </option>
              ))}
            </select>
          ) : (
            <input
              className={form.input}
              id={`catalog-${field.key}`}
              onChange={(event: ChangeEvent<HTMLInputElement>) =>
                onChange(withField(draft, field.key, event.target.value))
              }
              type="text"
              value={textOf(draft[field.key])}
            />
          )}
        </div>
      ))}
    </div>
  );
}
