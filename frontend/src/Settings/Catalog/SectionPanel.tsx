import { useState } from 'react';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import form from 'Components/Form/Form.module.css';
import table from 'Components/Table/Table.module.css';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import * as icons from 'Helpers/Props/icons';
import type { CatalogItem, CatalogSectionName } from 'typings/Catalog';
import translate from 'Utilities/String/translate';

import styles from './Catalog.module.css';
import ItemEditor from './ItemEditor';
import { useCatalogSection, useRemoveItem } from './useCatalog';

interface SectionPanelProps {
  section: CatalogSectionName;
  writable: boolean;
}

type Editing = { item: CatalogItem | null } | null;

function labelOf(item: CatalogItem): string {
  return typeof item.name === 'string' && item.name !== '' ? item.name : item.id;
}

/** One section of the catalog: its items, add, edit and remove. */
export default function SectionPanel({ section, writable }: Readonly<SectionPanelProps>) {
  const { data, isError } = useCatalogSection(section);
  const remove = useRemoveItem();
  const [editing, setEditing] = useState<Editing>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ kind: 'info' | 'danger'; text: string } | null>(null);

  if (isError) {
    return <Alert kind="danger">{translate('CatalogLoadFailed')}</Alert>;
  }
  if (!data) {
    return <Card title={translate(`CatalogSection_${section}`)}>{translate('Loading')}</Card>;
  }

  const onSaved = () => {
    setEditing(null);
    setNotice({ kind: 'info', text: translate('CatalogItemSaved') });
  };

  const onRemove = async (id: string) => {
    setNotice(null);
    try {
      await remove.mutateAsync({ section, id, version: data.version });
      setNotice({ kind: 'info', text: translate('CatalogItemRemoved', { id }) });
    } catch (error) {
      setNotice({
        kind: 'danger',
        text: error instanceof ApiError && error.detail ? error.detail : translate('CatalogSaveFailed'),
      });
    } finally {
      setConfirming(null);
    }
  };

  return (
    <Card title={translate(`CatalogSection_${section}`)}>
      {notice ? <Alert kind={notice.kind}>{notice.text}</Alert> : null}
      {writable ? (
        <div className={styles.toolbar}>
          <button className={form.save} onClick={() => setEditing({ item: null })} type="button">
            {translate('CatalogAddItem')}
          </button>
        </div>
      ) : (
        <Alert kind="info">{translate('CatalogReadOnly')}</Alert>
      )}
      {editing ? (
        <ItemEditor
          item={editing.item}
          key={editing.item?.id ?? 'new'}
          onClose={() => setEditing(null)}
          onSaved={onSaved}
          section={section}
          version={data.version}
        />
      ) : null}
      {data.items.length === 0 ? (
        <p className={form.help}>{translate('CatalogSectionEmpty')}</p>
      ) : (
        <div className={table.wrapper}>
          <table aria-label={translate(`CatalogSection_${section}`)} className={table.table}>
            <thead>
              <tr>
                <th>{translate('Id')}</th>
                <th>{translate('Name')}</th>
                <th>
                  <span className={table.visuallyHidden}>{translate('Actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((item) => (
                <tr key={item.id}>
                  <td className={table.mono}>{item.id}</td>
                  <td>{labelOf(item)}</td>
                  <td>
                    {writable ? (
                      <div className={table.actions}>
                        <button
                          aria-label={translate('CatalogEditItem', { id: item.id })}
                          className={table.iconButton}
                          onClick={() => setEditing({ item })}
                          title={translate('Edit')}
                          type="button"
                        >
                          <icons.EDIT aria-hidden="true" size={16} strokeWidth={2.25} />
                        </button>
                        {confirming === item.id ? (
                          <button className={form.danger} onClick={() => void onRemove(item.id)} type="button">
                            {translate('ConfirmDelete')}
                          </button>
                        ) : (
                          <button
                            aria-label={translate('CatalogRemoveItem', { id: item.id })}
                            className={table.iconButton}
                            onClick={() => setConfirming(item.id)}
                            title={translate('Delete')}
                            type="button"
                          >
                            <icons.DELETE aria-hidden="true" size={16} strokeWidth={2.25} />
                          </button>
                        )}
                      </div>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
