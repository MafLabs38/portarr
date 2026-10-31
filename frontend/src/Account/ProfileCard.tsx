import { type ChangeEvent, type FormEvent, useRef, useState } from 'react';

import Avatar from 'Auth/Avatar';
import usePermissions from 'Auth/usePermissions';
import Card from 'Components/Card';
import styles from 'Components/Form/Form.module.css';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';

import layout from './Account.module.css';
import { useRemoveAvatar, useUpdateProfile, useUploadAvatar } from './useAccount';

const ACCEPTED_AVATARS = '.png,.webp,.jpg,.jpeg,image/png,image/webp,image/jpeg';

/** The picture and the display name (V7, D-95): the picture is a png, webp or jpeg of at most 1 MiB (no svg). */
export default function ProfileCard() {
  const { me } = usePermissions();
  const update = useUpdateProfile();
  const upload = useUploadAvatar();
  const remove = useRemoveAvatar();
  const fileInput = useRef<HTMLInputElement>(null);
  const [displayName, setDisplayName] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<string | null>(null);

  if (!me) {
    return null;
  }

  const current = displayName ?? me.displayName;
  const changed = current.trim() !== me.displayName;

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setOutcome(null);
    try {
      await update.mutateAsync({ displayName: current.trim() });
      setDisplayName(null);
      setOutcome('saved');
    } catch {
      setOutcome('ProfileSaveFailed');
    }
  };

  const onFile = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) {
      return;
    }
    setOutcome(null);
    try {
      await upload.mutateAsync({ file });
    } catch (error) {
      setOutcome(error instanceof ApiError && error.status === 400 ? 'AvatarInvalid' : 'AvatarUploadFailed');
    }
  };

  return (
    <Card title={translate('Profile')}>
      <div className={layout.identity}>
        <Avatar avatarUrl={me.avatarUrl} displayName={me.displayName} size={72} username={me.username} />
        <div className={layout.names}>
          <span className={layout.displayName}>{me.displayName}</span>
          <span className={layout.meta}>{me.username}</span>
          <span className={layout.meta}>{translate('RoleName', { role: me.role })}</span>
        </div>
      </div>
      <div className={styles.actions}>
        <input
          accept={ACCEPTED_AVATARS}
          aria-label={translate('AvatarFile')}
          hidden
          onChange={onFile}
          ref={fileInput}
          type="file"
        />
        <button
          className={styles.secondary}
          disabled={upload.isPending}
          onClick={() => fileInput.current?.click()}
          type="button"
        >
          {translate('UploadAvatar')}
        </button>
        {me.avatarUrl ? (
          <button
            className={styles.secondary}
            disabled={remove.isPending}
            onClick={() => remove.mutate(undefined)}
            type="button"
          >
            {translate('RemoveAvatar')}
          </button>
        ) : null}
      </div>
      <p className={styles.help}>{translate('AvatarHelpText')}</p>
      <form className={styles.form} onSubmit={onSubmit}>
        <div className={styles.field}>
          <label className={styles.label} htmlFor="profile-display-name">
            {translate('DisplayName')}
          </label>
          <input
            className={styles.input}
            id="profile-display-name"
            maxLength={64}
            onChange={(event) => setDisplayName(event.target.value)}
            required
            type="text"
            value={current}
          />
        </div>
        <div className={styles.actions}>
          <button className={styles.save} disabled={!changed || !current.trim() || update.isPending} type="submit">
            {translate('Save')}
          </button>
          {outcome === 'saved' ? (
            <span className={styles.saved} role="status">
              {translate('IdentitySaved')}
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
