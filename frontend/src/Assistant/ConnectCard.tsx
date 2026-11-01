import Card from 'Components/Card';
import translate from 'Utilities/String/translate';

import styles from './AssistantIndex.module.css';
import CopyBlock from './CopyBlock';

interface ConnectCardProps {
  endpoint: string;
  headerName: string;
}

/**
 * How to plug an assistant in. The key is never shown: the operator pastes
 * an API key of theirs (Account, API keys) in place of the placeholder.
 */
export default function ConnectCard({ endpoint, headerName }: Readonly<ConnectCardProps>) {
  const key = translate('ApiKeyPlaceholder');
  const claudeCode = `claude mcp add --transport http portarr ${endpoint} --header "${headerName}: ${key}"`;
  const claudeDesktop = JSON.stringify(
    {
      mcpServers: {
        portarr: {
          command: 'npx',
          args: ['-y', 'mcp-remote', endpoint, '--header', `${headerName}:\${PORTARR_API_KEY}`],
          env: { PORTARR_API_KEY: key },
        },
      },
    },
    null,
    2,
  );

  return (
    <Card title={translate('ConnectAnAssistant')}>
      <p className={styles.text}>{translate('ConnectKeyHint')}</p>

      <h3 className={styles.subheading}>{translate('ClaudeCode')}</h3>
      <CopyBlock label={translate('ClaudeCodeCommand')} text={claudeCode} />

      <h3 className={styles.subheading}>{translate('ClaudeDesktop')}</h3>
      <p className={styles.text}>{translate('ClaudeDesktopHint')}</p>
      <CopyBlock label={translate('ClaudeDesktopConfig')} text={claudeDesktop} />

      <h3 className={styles.subheading}>{translate('TryAsking')}</h3>
      <ul className={styles.questions}>
        <li>{translate('AskWhatIsDown')}</li>
        <li>{translate('AskWhichMachine')}</li>
        <li>{translate('AskBackups')}</li>
        <li>{translate('AskVulnerable')}</li>
        <li>{translate('AskWhereApp')}</li>
      </ul>
    </Card>
  );
}
