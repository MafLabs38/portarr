import { useState } from 'react';
import { Link, useParams } from 'react-router';

import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import type { McpRendering } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import McpDefinitionCard from './McpDefinitionCard';
import McpRenderingBlock from './McpRenderingBlock';
import styles from './McpServerDetail.module.css';
import { useMcpServer } from './useMcp';

const systems = ['linux', 'macos', 'windows'] as const;

/** The system of the browser, as a first guess of the machine the operator is installing for. */
function guessSystem(): string {
  const agent = typeof navigator === 'undefined' ? '' : navigator.userAgent;
  if (/Windows/i.test(agent)) {
    return 'windows';
  }
  return /Mac/i.test(agent) ? 'macos' : 'linux';
}

interface InstallCardProps {
  os: string;
  onOsChange: (os: string) => void;
  renderings: McpRendering[];
}

function InstallCard({ os, onOsChange, renderings }: Readonly<InstallCardProps>) {
  return (
    <section aria-label={translate('McpInstall')} className={styles.card}>
      <h2 className={styles.cardTitle}>{translate('McpInstall')}</h2>
      <label className={styles.chooser}>
        {translate('McpOperatingSystem')}
        <select className={styles.select} onChange={(event) => onOsChange(event.target.value)} value={os}>
          {systems.map((system) => (
            <option key={system} value={system}>
              {translate(`McpOs_${system}`)}
            </option>
          ))}
        </select>
      </label>
      <p className={styles.hint}>{translate('McpInstallHint')}</p>
      {renderings.map((rendering) => (
        <McpRenderingBlock key={rendering.client} rendering={rendering} />
      ))}
    </section>
  );
}

/**
 * One MCP server: what the catalog says about it, how it is doing, and how to install it in every client for the operating
 * system of the machine (V9).
 */
export default function McpServerDetail() {
  const { id = '' } = useParams();
  const [os, setOs] = useState(guessSystem);
  const { data, isError } = useMcpServer(id, os);
  const server = data?.server;

  return (
    <PageContent title={server?.name ?? id}>
      <PageContentBody>
        <Link className={styles.back} to="/mcp-servers">
          ← {translate('McpServers')}
        </Link>
        <PageHeading scope={translate('MCP')} title={server?.name ?? id} />

        {isError ? <Alert kind="warning">{translate('McpServerNotFound')}</Alert> : null}

        {data && server ? (
          <div className={styles.layout}>
            {server.unpinnedPackage ? <Alert kind="warning">{translate('McpUnpinned')}</Alert> : null}
            <McpDefinitionCard server={server} />
            <InstallCard onOsChange={setOs} os={os} renderings={data.renderings} />
          </div>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
