import { useMemo } from 'react';

import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import type Workload from 'typings/Workload';
import translate from 'Utilities/String/translate';
import { useWorkloads } from 'Workloads/useWorkloads';

import EnvironmentSummary from './EnvironmentSummary';
import styles from './InfrastructureIndex.module.css';
import MachineCard from './MachineCard';
import { placeMachines } from './machineUsage';
import useHostStream, { hostKey } from './useHostStream';
import useInfrastructure from './useInfrastructure';

/** One shared empty map, so a machine without workloads does not get a new prop (and a re-render) each time. */
const noWorkloads = new Map<string, Workload>();

/**
 * How the machines run and what consumes them (D-54), modelled on the Unraid dashboard: one compact card per
 * Docker host or Kubernetes node, side by side, a hypervisor followed by its VMs (D-59). Environments are a
 * light header line. Live only; statuses live on the Apps page. Host agents stream a read per machine and
 * second (D-72) on top of the snapshot, which is polled less often while the stream is open.
 */
export default function InfrastructureIndex() {
  const hostStream = useHostStream();
  const infrastructure = useInfrastructure(hostStream.live);
  const { data: workloadList } = useWorkloads();
  const byEnvironment = useMemo(() => {
    const result = new Map<string, Map<string, Workload>>();
    for (const workload of workloadList ?? []) {
      const own = result.get(workload.environmentId) ?? new Map<string, Workload>();
      own.set(workload.id, workload);
      result.set(workload.environmentId, own);
    }
    return result;
  }, [workloadList]);
  const environments = infrastructure.data ?? [];
  const placed = useMemo(() => placeMachines(infrastructure.data ?? []), [infrastructure.data]);

  return (
    <PageContent title={translate('Infrastructure')}>
      <PageContentBody>
        <PageHeading scope={translate('Infrastructure')} title={translate('Machines')} />

        {infrastructure.isError ? <Alert kind="danger">{translate('ErrorLoadingInfrastructure')}</Alert> : null}
        {infrastructure.data?.length === 0 ? (
          <p className={styles.muted}>{translate('InfrastructureEmptyHint')}</p>
        ) : null}

        {environments.length > 0 ? (
          <ul aria-label={translate('Environments')} className={styles.environments}>
            {environments.map((environment) => (
              <EnvironmentSummary
                environment={environment}
                key={environment.id}
                workloadCount={byEnvironment.get(environment.id)?.size ?? 0}
              />
            ))}
          </ul>
        ) : null}

        <div className={styles.grid}>
          {placed.map(({ environment, machine }) => (
            <MachineCard
              environment={environment}
              hostReads={hostStream.reads.get(hostKey(environment.id, machine.id))}
              key={`${environment.id}/${machine.id}`}
              machine={machine}
              workloads={byEnvironment.get(environment.id) ?? noWorkloads}
            />
          ))}
        </div>
      </PageContentBody>
    </PageContent>
  );
}
