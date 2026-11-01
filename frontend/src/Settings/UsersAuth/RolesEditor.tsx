import { type FormEvent, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import form from 'Components/Form/Form.module.css';
import table from 'Components/Table/Table.module.css';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import type { Role, RoleList } from 'typings/Auth';
import translate from 'Utilities/String/translate';

import layout from './UsersAuth.module.css';
import { useCreateRole, useDeleteRole, useUpdateRole } from './useUsersAuth';

interface RolesEditorProps {
  list: RoleList;
}

/** A role being looked at or edited; `id` is null for a new one, `fixed` for a built-in role (read only). */
interface Draft {
  id: number | null;
  name: string;
  description: string;
  permissions: string[];
  fixed: boolean;
}

function reason(error: unknown): string {
  return error instanceof ApiError && error.detail ? error.detail : translate('RoleActionFailed');
}

/**
 * The roles (V7, D-93): named sets of permissions, grouped by area. Admin, Operator, Observer and Guest are built in and
 * fixed (shown read only; "Duplicate" starts a custom role from one); custom roles are created, renamed, given other
 * permissions and deleted when no user has them.
 */
export default function RolesEditor({ list }: Readonly<RolesEditorProps>) {
  const create = useCreateRole();
  const update = useUpdateRole();
  const remove = useDeleteRole();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [confirming, setConfirming] = useState<number | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const areas = [...new Set(list.permissions.map((permission) => permission.area))];

  const run = async (action: () => Promise<unknown>) => {
    setProblem(null);
    try {
      await action();
      setDraft(null);
      setConfirming(null);
    } catch (error) {
      setProblem(reason(error));
    }
  };

  // Deleting a role asks to confirm; a role in use cannot be deleted.
  const deleteButton = (role: Role) =>
    confirming === role.id ? (
      <button className={form.danger} onClick={() => run(() => remove.mutateAsync({ id: role.id }))} type="button">
        {translate('ConfirmDelete')}
      </button>
    ) : (
      <button
        aria-label={translate('DeleteRole', { name: role.name })}
        className={form.danger}
        disabled={role.users > 0}
        onClick={() => setConfirming(role.id)}
        title={role.users > 0 ? translate('RoleInUse') : undefined}
        type="button"
      >
        {translate('Delete')}
      </button>
    );

  const open = (role: Role) =>
    setDraft({
      id: role.id,
      name: role.name,
      description: role.description ?? '',
      permissions: role.permissions,
      fixed: role.builtIn,
    });
  const duplicate = (role: Role) =>
    setDraft({
      id: null,
      name: '',
      description: '',
      permissions: role.permissions.filter((key) => !list.permissions.find((p) => p.key === key)?.reserved),
      fixed: false,
    });

  const toggle = (key: string) =>
    setDraft((current) =>
      current && !current.fixed
        ? {
            ...current,
            permissions: current.permissions.includes(key)
              ? current.permissions.filter((p) => p !== key)
              : [...current.permissions, key],
          }
        : current,
    );

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!draft || draft.fixed) {
      return;
    }
    const body = {
      name: draft.name.trim(),
      description: draft.description.trim() || undefined,
      permissions: draft.permissions,
    };
    void run(() => (draft.id === null ? create.mutateAsync(body) : update.mutateAsync({ id: draft.id, ...body })));
  };

  return (
    <Card title={translate('Roles')}>
      <p className={form.help}>{translate('RolesHelpText')}</p>
      {problem ? <Alert kind="danger">{problem}</Alert> : null}
      <div className={form.actions}>
        <button
          className={form.save}
          onClick={() => setDraft({ id: null, name: '', description: '', permissions: [], fixed: false })}
          type="button"
        >
          {translate('AddRole')}
        </button>
      </div>

      {draft ? (
        <form
          aria-label={draft.id === null ? translate('AddRole') : translate('EditRole', { name: draft.name })}
          className={`${form.form} ${layout.userForm}`}
          onSubmit={submit}
        >
          <div className={form.field}>
            <label className={form.label} htmlFor="role-name">
              {translate('Name')}
            </label>
            <input
              className={form.input}
              disabled={draft.fixed}
              id="role-name"
              maxLength={40}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
              required
              type="text"
              value={draft.name}
            />
          </div>
          <div className={form.field}>
            <label className={form.label} htmlFor="role-description">
              {translate('Description')}
            </label>
            <input
              className={form.input}
              disabled={draft.fixed}
              id="role-description"
              maxLength={200}
              onChange={(event) => setDraft({ ...draft, description: event.target.value })}
              type="text"
              value={draft.description}
            />
          </div>
          {draft.fixed ? <span className={form.help}>{translate('BuiltInRoleHelpText')}</span> : null}
          <div className={layout.areas}>
            {areas.map((area) => (
              <fieldset className={layout.area} key={area}>
                <legend>{translate(`Area_${area}`)}</legend>
                {list.permissions
                  .filter((permission) => permission.area === area)
                  .map((permission) => (
                    <label className={layout.permission} key={permission.key}>
                      <input
                        checked={draft.permissions.includes(permission.key)}
                        disabled={draft.fixed || permission.reserved}
                        onChange={() => toggle(permission.key)}
                        type="checkbox"
                      />
                      <span>
                        {translate(`Permission_${permission.key}`)}
                        <br />
                        <span className={`${layout.permissionKey} ${table.muted}`}>{permission.key}</span>
                        {permission.reserved ? <span className={table.muted}> · {translate('Reserved')}</span> : null}
                      </span>
                    </label>
                  ))}
              </fieldset>
            ))}
          </div>
          <div className={form.actions}>
            {draft.fixed ? null : (
              <button
                className={form.save}
                disabled={!draft.name.trim() || draft.permissions.length === 0}
                type="submit"
              >
                {translate('Save')}
              </button>
            )}
            <button className={form.secondary} onClick={() => setDraft(null)} type="button">
              {translate(draft.fixed ? 'Close' : 'Cancel')}
            </button>
          </div>
        </form>
      ) : null}

      <ul aria-label={translate('Roles')} className={layout.roleList}>
        {list.roles.map((role) => (
          <li key={role.id}>
            <div>
              <div className={layout.roleHeader}>
                <h3 className={layout.roleName}>{role.name}</h3>
                {role.builtIn ? <span className={table.badge}>{translate('BuiltIn')}</span> : null}
                <span className={table.muted}>{translate('UsersCount', { count: role.users })}</span>
              </div>
              <span className={table.muted}>
                {role.description ?? translate('PermissionsCount', { count: role.permissions.length })}
              </span>
            </div>
            <div className={table.actions}>
              <button
                aria-label={translate(role.builtIn ? 'ViewRole' : 'EditRole', { name: role.name })}
                className={form.secondary}
                onClick={() => open(role)}
                type="button"
              >
                {translate(role.builtIn ? 'View' : 'Edit')}
              </button>
              <button
                aria-label={translate('DuplicateRole', { name: role.name })}
                className={form.secondary}
                onClick={() => duplicate(role)}
                type="button"
              >
                {translate('Duplicate')}
              </button>
              {role.builtIn ? null : deleteButton(role)}
            </div>
          </li>
        ))}
      </ul>
    </Card>
  );
}
