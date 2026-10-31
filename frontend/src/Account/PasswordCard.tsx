import { type FormEvent, useState } from 'react';

import { minimumPasswordLength } from 'Auth/SetupPage';
import Card from 'Components/Card';
import styles from 'Components/Form/Form.module.css';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';

import { useChangePassword } from './useAccount';

/** The message key for a refused change: 403 the current password is wrong, 400 the new one is not acceptable. */
function failureOf(status: number): string {
  if (status === 403) {
    return 'CurrentPasswordWrong';
  }
  return status === 400 ? 'SetupInvalid' : 'PasswordChangeFailed';
}

/** Changing the password needs the current one; other sessions end, this one goes on (V7). */
export default function PasswordCard() {
  const change = useChangePassword();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [outcome, setOutcome] = useState<string | null>(null);

  const ready = current.length > 0 && next.length >= minimumPasswordLength && next === confirmation;

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setOutcome(null);
    try {
      await change.mutateAsync({ currentPassword: current, newPassword: next });
      setCurrent('');
      setNext('');
      setConfirmation('');
      setOutcome('saved');
    } catch (error) {
      setOutcome(failureOf(error instanceof ApiError ? error.status : 0));
    }
  };

  return (
    <Card title={translate('Password')}>
      <form className={styles.form} onSubmit={onSubmit}>
        <div className={styles.field}>
          <label className={styles.label} htmlFor="password-current">
            {translate('CurrentPassword')}
          </label>
          <input
            autoComplete="current-password"
            className={styles.input}
            id="password-current"
            maxLength={128}
            onChange={(event) => setCurrent(event.target.value)}
            type="password"
            value={current}
          />
        </div>
        <div className={styles.field}>
          <label className={styles.label} htmlFor="password-new">
            {translate('NewPassword')}
          </label>
          <input
            aria-describedby="password-new-help"
            autoComplete="new-password"
            className={styles.input}
            id="password-new"
            maxLength={128}
            onChange={(event) => setNext(event.target.value)}
            type="password"
            value={next}
          />
          <span className={styles.help} id="password-new-help">
            {translate('PasswordHelpText', { count: minimumPasswordLength })}
          </span>
        </div>
        <div className={styles.field}>
          <label className={styles.label} htmlFor="password-confirmation">
            {translate('ConfirmPassword')}
          </label>
          <input
            autoComplete="new-password"
            className={styles.input}
            id="password-confirmation"
            maxLength={128}
            onChange={(event) => setConfirmation(event.target.value)}
            type="password"
            value={confirmation}
          />
        </div>
        <div className={styles.actions}>
          <button className={styles.save} disabled={!ready || change.isPending} type="submit">
            {translate('ChangePassword')}
          </button>
          {outcome === 'saved' ? (
            <span className={styles.saved} role="status">
              {translate('PasswordChanged')}
            </span>
          ) : null}
        </div>
        {outcome && outcome !== 'saved' ? (
          <p className={styles.error} role="alert">
            {translate(outcome)}
          </p>
        ) : null}
      </form>
    </Card>
  );
}
