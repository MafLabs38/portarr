import { useState } from 'react';
import { Link, useParams } from 'react-router';

import Alert from 'Components/Alert/Alert';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import type { McpRendering, McpWorkstation } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import McpRenderingBlock from './McpRenderingBlock';
import styles from './McpServerDetail.module.css';
import { useMcpWorkstation } from './useMcp';

const systems = ['linux', 'macos', 'windows'] as const;

function WantedList({ workstation }: Readonly<{ workstation: McpWorkstation }>) {
  if (workstation.wanted.length === 0) {
    return <span>{translate('McpNothingWanted')}</span>;
  }

  return (
    <ul className={styles.wantedList}>
      {workstation.wanted.map((wanted) => (
        <li key={wanted.server}>
          <Link to={`/mcp-servers/${encodeURIComponent(wanted.server)}`}>{wanted.server}</Link>
          <span className={styles.hint}> · {wanted.clients.join(', ')}</span>
        </li>
      ))}
    </ul>
  );
}

function PlanSection({
  title,
  hint,
  empty,
  renderings,
}: Readonly<{ title: string; hint?: string; empty: string; renderings: McpRendering[] }>) {
  return (
    <>
      <h3 className={styles.subTitle}>{title}</h3>
      {hint ? <p className={styles.hint}>{hint}</p> : null}
      {renderings.length === 0 ? <p className={styles.hint}>{empty}</p> : null}
      {renderings.map((rendering) => (
        <McpRenderingBlock
          key={`${rendering.server}-${rendering.client}-${rendering.scope}`}
          rendering={rendering}
          title={`${rendering.server} · ${rendering.clientName}`}
        />
      ))}
    </>
  );
}

/**
 * One workstation: what the catalog says about it and its plan (D-110): what an assistant on that machine installs, what it removes
 * if present, and the variables the machine must provide. The system defaults to the machine's own; the operator may look at another.
 */
export default function McpWorkstationDetail() {
  const { id = '' } = useParams();
  const [os, setOs] = useState<string | undefined>(undefined);
  const { data: plan, isError } = useMcpWorkstation(id, os);
  const workstation = plan?.workstation;

  return (
    <PageContent title={workstation?.name ?? id}>
      <PageContentBody>
        <Link className={styles.back} to="/mcp-servers?tab=workstations">
          ← {translate('McpPlanBack')}
        </Link>
        <PageHeading scope={translate('McpWorkstation')} title={workstation?.name ?? id} />

        {isError ? <Alert kind="warning">{translate('McpWorkstationNotFound')}</Alert> : null}

        {plan && workstation ? (
          <div className={styles.layout}>
            <section aria-label={translate('McpDefinition')} className={styles.card}>
              <h2 className={styles.cardTitle}>{translate('McpDefinition')}</h2>
              <DescriptionList>
                <DescriptionListItem
                  data={translate(`McpOs_${workstation.os}`)}
                  title={translate('McpWorkstationOs')}
                />
                <DescriptionListItem data={workstation.clients.join(', ')} monospace title={translate('McpClients')} />
                {workstation.environment ? (
                  <DescriptionListItem data={workstation.environment} title={translate('McpEnvironmentLink')} />
                ) : null}
                <DescriptionListItem
                  data={<WantedList workstation={workstation} />}
                  title={translate('McpWantedServers')}
                />
                {workstation.notes ? (
                  <DescriptionListItem data={workstation.notes} title={translate('McpNotes')} />
                ) : null}
              </DescriptionList>
              <p className={styles.hint}>{translate('McpEditInCatalog')}</p>
            </section>

            <section aria-label={translate('McpPlan')} className={styles.card}>
              <h2 className={styles.cardTitle}>{translate('McpPlan')}</h2>
              <label className={styles.chooser}>
                {translate('McpOperatingSystem')}
                <select className={styles.select} onChange={(event) => setOs(event.target.value)} value={plan.os}>
                  {systems.map((system) => (
                    <option key={system} value={system}>
                      {translate(`McpOs_${system}`)}
                    </option>
                  ))}
                </select>
              </label>
              <p className={styles.hint}>{translate('McpPlanHint')}</p>
              {plan.requiresSecrets.length > 0 ? (
                <p className={styles.hint}>
                  {translate('McpPlanRequiresSecrets', { keys: plan.requiresSecrets.join(', ') })}
                </p>
              ) : null}
              {plan.requiresEnv.length > 0 ? (
                <p className={styles.hint}>
                  {translate('McpPlanRequires', { variables: plan.requiresEnv.join(', ') })}
                </p>
              ) : null}

              <PlanSection
                empty={translate('McpPlanNothingToInstall')}
                renderings={plan.install}
                title={translate('McpPlanInstall')}
              />
              <PlanSection
                empty={translate('McpPlanNothingToRemove')}
                hint={translate('McpPlanRemoveHint')}
                renderings={plan.remove}
                title={translate('McpPlanRemove')}
              />
            </section>
          </div>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
