import { type FormEvent, useState } from 'react';

import Avatar from 'Auth/Avatar';
import usePermissions from 'Auth/usePermissions';
import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import form from 'Components/Form/Form.module.css';
import table from 'Components/Table/Table.module.css';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import * as icons from 'Helpers/Props/icons';
import type { Role, UserAccount } from 'typings/Auth';
import { formatDateTime } from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';

import layout from './UsersAuth.module.css';
import { useCreateUser, useDeleteUser, useResetPassword, useUpdateUser } from './useUsersAuth';

interface UsersTableProps {
  users: UserAccount[];
  roles: Role[];
}

type Editing =
  { kind: 'create' } | { kind: 'edit'; user: UserAccount } | { kind: 'password'; user: UserAccount } | null;

/** Why a call was refused, in words: the server's own message when it gave one, else a general one. */
function reason(error: unknown): string {
  return error instanceof ApiError && error.detail ? error.detail : translate('UserActionFailed');
}

/** The users (V7, D-93): create, edit the display name and the role, reset a password, disable, delete. */
/** The state of an account as a message key: disabled and locked ones are flagged, the others are just enabled. */
function stateKey(user: UserAccount): 'Disabled' | 'Locked' | 'Enabled' {
  if (user.disabled) {
    return 'Disabled';
  }
  return user.lockedUntil ? 'Locked' : 'Enabled';
}

/** The title of the user form: a new user, an edit, or a password reset. */
function formTitle(kind: 'create' | 'edit' | 'password', username: string): string {
  if (kind === 'create') {
    return translate('AddUser');
  }
  return translate(kind === 'edit' ? 'EditUser' : 'ResetPasswordOf', { name: username });
}

export default function UsersTable({ users, roles }: Readonly<UsersTableProps>) {
  const { me } = usePermissions();
  const create = useCreateUser();
  const update = useUpdateUser();
  const reset = useResetPassword();
  const remove = useDeleteUser();
  const [editing, setEditing] = useState<Editing>(null);
  const [confirming, setConfirming] = useState<number | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const run = async (action: () => Promise<unknown>) => {
    setProblem(null);
    try {
      await action();
      setEditing(null);
      setConfirming(null);
    } catch (error) {
      setProblem(reason(error));
    }
  };

  return (
    <Card title={translate('Users')}>
      {problem ? <Alert kind="danger">{problem}</Alert> : null}
      <div className={form.actions}>
        <button className={form.save} onClick={() => setEditing({ kind: 'create' })} type="button">
          {translate('AddUser')}
        </button>
      </div>

      {editing ? (
        <UserForm
          editing={editing}
          key={editing.kind === 'create' ? 'create' : `${editing.kind}-${editing.user.id}`}
          onCancel={() => setEditing(null)}
          onSubmit={(values) =>
            run(() => {
              if (editing.kind === 'create') {
                return create.mutateAsync({
                  username: values.username,
                  displayName: values.displayName || undefined,
                  password: values.password,
                  roleId: values.roleId,
                });
              }
              if (editing.kind === 'password') {
                return reset.mutateAsync({ id: editing.user.id, password: values.password });
              }
              return update.mutateAsync({
                id: editing.user.id,
                displayName: values.displayName,
                roleId: values.roleId,
              });
            })
          }
          roles={roles}
          self={editing.kind !== 'create' && editing.user.id === me?.id}
        />
      ) : null}

      <div className={table.wrapper}>
        <table aria-label={translate('Users')} className={table.table}>
          <thead>
            <tr>
              <th>{translate('User')}</th>
              <th>{translate('Role')}</th>
              <th>{translate('State')}</th>
              <th>{translate('LastSignIn')}</th>
              <th>
                <span className={table.visuallyHidden}>{translate('Actions')}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {users.map((user) => {
              const self = user.id === me?.id;
              return (
                <tr key={user.id}>
                  <td>
                    <div className={table.person}>
                      <Avatar
                        avatarUrl={user.avatarUrl}
                        displayName={user.displayName}
                        size={32}
                        username={user.username}
                      />
                      <div className={table.names}>
                        <span>{user.displayName}</span>
                        <span className={table.muted}>{user.username}</span>
                      </div>
                    </div>
                  </td>
                  <td>{user.role}</td>
                  <td>
                    {stateKey(user) === 'Enabled' ? (
                      translate('Enabled')
                    ) : (
                      <span className={`${table.badge} ${table.warning}`}>{translate(stateKey(user))}</span>
                    )}
                  </td>
                  <td className={table.muted}>
                    {user.lastLogin ? formatDateTime(user.lastLogin) : translate('Never')}
                  </td>
                  <td>
                    <div className={table.actions}>
                      <button
                        aria-label={translate('EditUser', { name: user.username })}
                        className={table.iconButton}
                        onClick={() => setEditing({ kind: 'edit', user })}
                        title={translate('Edit')}
                        type="button"
                      >
                        <icons.EDIT aria-hidden="true" size={16} strokeWidth={2.25} />
                      </button>
                      <button
                        aria-label={translate('ResetPasswordOf', { name: user.username })}
                        className={table.iconButton}
                        onClick={() => setEditing({ kind: 'password', user })}
                        title={translate('ResetPassword')}
                        type="button"
                      >
                        <icons.API_KEY aria-hidden="true" size={16} strokeWidth={2.25} />
                      </button>
                      <button
                        aria-label={translate(user.disabled ? 'EnableUser' : 'DisableUser', { name: user.username })}
                        className={table.iconButton}
                        disabled={self}
                        onClick={() => run(() => update.mutateAsync({ id: user.id, disabled: !user.disabled }))}
                        title={translate(user.disabled ? 'Enable' : 'Disable')}
                        type="button"
                      >
                        {user.disabled ? (
                          <icons.PLAY aria-hidden="true" size={16} strokeWidth={2.25} />
                        ) : (
                          <icons.PAUSE aria-hidden="true" size={16} strokeWidth={2.25} />
                        )}
                      </button>
                      {confirming === user.id ? (
                        <button
                          className={form.danger}
                          onClick={() => run(() => remove.mutateAsync({ id: user.id }))}
                          type="button"
                        >
                          {translate('ConfirmDelete')}
                        </button>
                      ) : (
                        <button
                          aria-label={translate('DeleteUser', { name: user.username })}
                          className={table.iconButton}
                          disabled={self}
                          onClick={() => setConfirming(user.id)}
                          title={translate('Delete')}
                          type="button"
                        >
                          <icons.DELETE aria-hidden="true" size={16} strokeWidth={2.25} />
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

interface FormValues {
  username: string;
  displayName: string;
  password: string;
  roleId: number;
}

interface UserFormProps {
  editing: NonNullable<Editing>;
  roles: Role[];
  /** Editing one's own account: the role cannot be changed (the server refuses it too). */
  self: boolean;
  onSubmit: (values: FormValues) => void;
  onCancel: () => void;
}

function UserForm({ editing, roles, self, onSubmit, onCancel }: Readonly<UserFormProps>) {
  const user = editing.kind === 'create' ? null : editing.user;
  const defaultRole = roles.find((role) => role.name === 'Observer') ?? roles[0];
  const [username, setUsername] = useState('');
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [password, setPassword] = useState('');
  const [roleId, setRoleId] = useState(user?.roleId ?? defaultRole?.id ?? 0);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({ username: username.trim(), displayName: displayName.trim(), password, roleId });
  };

  const needsPassword = editing.kind !== 'edit';
  const valid =
    editing.kind === 'edit'
      ? displayName.trim().length > 0
      : password.length >= 10 && (editing.kind === 'password' || username.trim().length >= 3);
  const title = formTitle(editing.kind, user?.username ?? '');

  return (
    <form aria-label={title} className={`${form.form} ${layout.userForm}`} onSubmit={submit}>
      <h3 className={layout.formTitle}>{title}</h3>
      {editing.kind === 'create' ? (
        <div className={form.field}>
          <label className={form.label} htmlFor="user-username">
            {translate('Username')}
          </label>
          <input
            autoCapitalize="off"
            className={form.input}
            id="user-username"
            maxLength={32}
            onChange={(event) => setUsername(event.target.value)}
            required
            spellCheck={false}
            type="text"
            value={username}
          />
          <span className={form.help}>{translate('UsernameHelpText')}</span>
        </div>
      ) : null}
      {editing.kind === 'password' ? null : (
        <div className={form.field}>
          <label className={form.label} htmlFor="user-display-name">
            {translate('DisplayName')}
          </label>
          <input
            className={form.input}
            id="user-display-name"
            maxLength={64}
            onChange={(event) => setDisplayName(event.target.value)}
            type="text"
            value={displayName}
          />
        </div>
      )}
      {editing.kind === 'password' ? null : (
        <div className={form.field}>
          <label className={form.label} htmlFor="user-role">
            {translate('Role')}
          </label>
          <select
            className={form.input}
            disabled={self}
            id="user-role"
            onChange={(event) => setRoleId(Number(event.target.value))}
            value={roleId}
          >
            {roles.map((role) => (
              <option key={role.id} value={role.id}>
                {role.name}
              </option>
            ))}
          </select>
          {self ? <span className={form.help}>{translate('OwnRoleHelpText')}</span> : null}
        </div>
      )}
      {needsPassword ? (
        <div className={form.field}>
          <label className={form.label} htmlFor="user-password">
            {translate(editing.kind === 'create' ? 'Password' : 'NewPassword')}
          </label>
          <input
            autoComplete="new-password"
            className={form.input}
            id="user-password"
            maxLength={128}
            onChange={(event) => setPassword(event.target.value)}
            type="password"
            value={password}
          />
          <span className={form.help}>{translate('PasswordHelpText', { count: 10 })}</span>
        </div>
      ) : null}
      <div className={form.actions}>
        <button className={form.save} disabled={!valid} type="submit">
          {translate('Save')}
        </button>
        <button className={form.secondary} onClick={onCancel} type="button">
          {translate('Cancel')}
        </button>
      </div>
    </form>
  );
}
