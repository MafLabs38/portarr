import { type FormEvent, useState } from 'react';

import type { McpAcceptMode, McpCaptureGroup } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import styles from './McpCaptureCard.module.css';

interface McpAcceptFormProps {
  group: McpCaptureGroup;
  mode: McpAcceptMode;
  /** The workstations of the catalog this server was seen on: their wanted lists can be updated. */
  knownWorkstations: string[];
  pending: boolean;
  onSubmit: (values: {
    id: string;
    name: string;
    variables: Record<string, string>;
    addToWorkstations: boolean;
  }) => void;
  onCancel: () => void;
}

/**
 * What the person decides before a captured server is written to the catalog: its id and name, the variable that holds each value the
 * assistant did not send (it never sends a secret), and whether the machines it was seen on want it. The values proposed are the ones the
 * definition would get.
 */
export default function McpAcceptForm({
  group,
  mode,
  knownWorkstations,
  pending,
  onSubmit,
  onCancel,
}: Readonly<McpAcceptFormProps>) {
  const replacing = mode === 'replace';
  const [id, setId] = useState(replacing ? (group.matchId ?? group.proposal.id) : group.proposal.id);
  const [name, setName] = useState(group.proposal.name);
  const [variables, setVariables] = useState<Record<string, string>>(
    Object.fromEntries(group.proposal.variables.map((v) => [`${v.kind}:${v.name}`, v.variable])),
  );
  const [addToWorkstations, setAddToWorkstations] = useState(knownWorkstations.length > 0);
  const workstations = [...new Set(knownWorkstations)];

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({ id, name, variables, addToWorkstations });
  };

  return (
    <form
      aria-label={translate(replacing ? 'McpCaptureFormSubmitReplace' : 'McpCaptureFormSubmitNew')}
      className={styles.form}
      onSubmit={submit}
    >
      {replacing ? null : (
        <label className={styles.field}>
          {translate('McpCaptureFormId')}
          <input className={styles.input} onChange={(event) => setId(event.target.value)} required value={id} />
        </label>
      )}
      <label className={styles.field}>
        {translate('McpCaptureFormName')}
        <input className={styles.input} onChange={(event) => setName(event.target.value)} required value={name} />
      </label>
      {group.proposal.variables.map((variable) => {
        const key = `${variable.kind}:${variable.name}`;
        return (
          <label className={styles.field} key={key}>
            {translate('McpCaptureFormVariable', {
              kind: translate(variable.kind === 'header' ? 'McpCaptureHeaderKind' : 'McpCaptureEnvKind'),
              name: variable.name,
            })}
            <input
              className={styles.input}
              onChange={(event) => setVariables({ ...variables, [key]: event.target.value })}
              required
              value={variables[key] ?? ''}
            />
          </label>
        );
      })}
      {workstations.length > 0 ? (
        <label className={styles.check}>
          <input
            checked={addToWorkstations}
            onChange={(event) => setAddToWorkstations(event.target.checked)}
            type="checkbox"
          />
          {translate('McpCaptureFormWanted', { workstations: workstations.join(', ') })}
        </label>
      ) : null}
      <div className={styles.actions}>
        <button className={styles.primary} disabled={pending} type="submit">
          {translate(replacing ? 'McpCaptureFormSubmitReplace' : 'McpCaptureFormSubmitNew')}
        </button>
        <button className={styles.button} onClick={onCancel} type="button">
          {translate('McpCaptureFormCancel')}
        </button>
      </div>
    </form>
  );
}
