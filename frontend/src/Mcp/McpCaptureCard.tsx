import { useState } from 'react';

import CopyBlock from 'Assistant/CopyBlock';
import usePermissions from 'Auth/usePermissions';
import { CATALOG_WRITE } from 'Helpers/Props/permissions';
import type { McpAcceptMode, McpAcceptResult, McpCaptureGroup } from 'typings/Mcp';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import McpAcceptForm from './McpAcceptForm';
import styles from './McpCaptureCard.module.css';
import { useAcceptCapture, useDismissCapture } from './useMcp';

interface McpCaptureCardProps {
  group: McpCaptureGroup;
}

interface Decision {
  id?: string;
  name?: string;
  variables?: Record<string, string>;
  addToWorkstations: boolean;
}

function stateLabel(group: McpCaptureGroup): string {
  if (group.state === 'new') {
    return translate('McpCaptureNew');
  }
  const server = group.matchName ?? group.matchId ?? '';
  return translate(group.state === 'same' ? 'McpCaptureSame' : 'McpCaptureDiffers', { server });
}

function whereIs(group: McpCaptureGroup): string {
  const { server } = group;
  return server.url ?? [server.command, ...server.args].join(' ');
}

function Outcome({ result }: Readonly<{ result: McpAcceptResult }>) {
  return (
    <div className={styles.outcome} role="status">
      {result.written && result.serverId ? <p>{translate('McpCaptureWritten', { server: result.serverId })}</p> : null}
      {result.workstationsUpdated.length > 0 ? (
        <p>{translate('McpCaptureWantedUpdated', { workstations: result.workstationsUpdated.join(', ') })}</p>
      ) : null}
      {result.skipped.length > 0 ? (
        <p>{translate('McpCaptureSkipped', { reasons: result.skipped.join(' ') })}</p>
      ) : null}
      {result.errors.length > 0 ? <p>{result.errors.join(' ')}</p> : null}
      {result.readOnly && result.yaml ? (
        <>
          <p>{translate('McpCaptureReadOnly')}</p>
          <CopyBlock label={translate('McpCaptureReadOnly')} text={result.yaml} />
        </>
      ) : null}
    </div>
  );
}

function Sightings({ group }: Readonly<McpCaptureCardProps>) {
  return (
    <ul className={styles.sightings}>
      {group.sightings.map((sighting) => (
        <li key={sighting.rowId}>
          {translate('McpCaptureSeen', {
            workstation: sighting.workstation,
            client: sighting.client,
            scope: sighting.scope,
            age: formatAge(sighting.receivedAt),
          })}
          {sighting.workstationKnown ? '' : ` · ${translate('McpCaptureUnknownWorkstation')}`}
        </li>
      ))}
    </ul>
  );
}

interface ActionsProps {
  group: McpCaptureGroup;
  canWrite: boolean;
  dismissing: boolean;
  onForm: (mode: McpAcceptMode) => void;
  onExisting: () => void;
  onDismiss: () => void;
}

/** What a person may do with the group: only what suits how it compares with the catalog. */
function Actions({ group, canWrite, dismissing, onForm, onExisting, onDismiss }: Readonly<ActionsProps>) {
  const server = group.matchName ?? group.matchId ?? '';
  const same = group.state === 'same';

  return (
    <div className={styles.actions}>
      {canWrite ? null : <span className={styles.hint}>{translate('McpCaptureNeedsWrite')}</span>}
      {canWrite && group.state === 'new' ? (
        <button className={styles.primary} onClick={() => onForm('new')} type="button">
          {translate('McpCaptureAdd')}
        </button>
      ) : null}
      {canWrite && group.state === 'differs' ? (
        <>
          <button className={styles.primary} onClick={() => onForm('replace')} type="button">
            {translate('McpCaptureUpdate', { server })}
          </button>
          <button className={styles.button} onClick={() => onForm('new')} type="button">
            {translate('McpCaptureAddAsNew')}
          </button>
        </>
      ) : null}
      {canWrite && group.state !== 'new' ? (
        <button className={same ? styles.primary : styles.button} onClick={onExisting} type="button">
          {translate(same ? 'McpCaptureUseExisting' : 'McpCaptureKeep')}
        </button>
      ) : null}
      {canWrite ? (
        <button className={styles.button} disabled={dismissing} onClick={onDismiss} type="button">
          {translate('McpCaptureDismiss')}
        </button>
      ) : null}
    </div>
  );
}

/**
 * One captured server: what it is, where it was seen, how it compares with the catalog, and what a person may do with it. Accepting
 * needs `catalog.write` and a browser session (D-112); the server enforces both, the buttons only follow the role.
 */
export default function McpCaptureCard({ group }: Readonly<McpCaptureCardProps>) {
  const { can } = usePermissions();
  const accept = useAcceptCapture();
  const dismiss = useDismissCapture();
  const [form, setForm] = useState<McpAcceptMode | null>(null);
  const [outcome, setOutcome] = useState<McpAcceptResult | null>(null);
  const known = group.sightings.filter((s) => s.workstationKnown).map((s) => s.workstation);

  const decide = (mode: McpAcceptMode, decision: Decision) =>
    accept.mutate(
      { key: group.key, mode, version: group.version, ...decision },
      {
        onSuccess: (result) => {
          setOutcome(result);
          setForm(null);
        },
      },
    );

  return (
    <li aria-label={group.server.name} className={styles.card}>
      <div className={styles.head}>
        <h3 className={styles.name}>{group.server.name}</h3>
        <span className={`${styles.state} ${styles[group.state]}`}>{stateLabel(group)}</span>
        <span className={styles.address} title={whereIs(group)}>
          {whereIs(group)}
        </span>
      </div>

      <Sightings group={group} />

      {group.differences.length > 0 ? (
        <ul
          aria-label={translate('McpCaptureDiffers', { server: group.matchName ?? '' })}
          className={styles.differences}
        >
          {group.differences.map((difference) => (
            <li key={difference}>{difference}</li>
          ))}
        </ul>
      ) : null}

      {accept.isError ? (
        <p className={styles.refused} role="alert">
          {translate('McpCaptureRefused', { reason: accept.error.detail ?? accept.error.message })}
        </p>
      ) : null}
      {outcome ? <Outcome result={outcome} /> : null}

      {form ? (
        <McpAcceptForm
          group={group}
          knownWorkstations={known}
          mode={form}
          onCancel={() => setForm(null)}
          onSubmit={(values) => decide(form, values)}
          pending={accept.isPending}
        />
      ) : (
        <Actions
          canWrite={can(CATALOG_WRITE)}
          dismissing={dismiss.isPending}
          group={group}
          onDismiss={() => dismiss.mutate({ key: group.key })}
          onExisting={() => decide('existing', { addToWorkstations: true })}
          onForm={setForm}
        />
      )}
    </li>
  );
}
