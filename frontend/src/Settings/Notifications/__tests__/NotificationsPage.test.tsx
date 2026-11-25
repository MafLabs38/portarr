import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { me, notificationLog, notificationSettings, server } from '__mocks__/server';
import NotificationsPage from 'Settings/Notifications/NotificationsPage';

const withSettings = (patch: object) =>
  server.use(
    http.get('/api/v1/notifications/settings', () => HttpResponse.json({ ...notificationSettings, ...patch })),
  );

const validation = (errors: Record<string, string[]>) =>
  HttpResponse.json({ title: 'One or more validation errors occurred.', status: 400, errors }, { status: 400 });

type User = ReturnType<typeof userEvent.setup>;

const card = async (name: string) => within(await screen.findByRole('region', { name }));

const fill = async (user: User, scope: ReturnType<typeof within>, label: string, value: string) => {
  const input = scope.getByLabelText(label);
  await user.clear(input);
  await user.type(input, value);
};

const savedBody = (patch: object = {}) => ({
  enabled: false,
  recipient: '',
  from: '',
  maxPerDay: 10,
  categories: [
    { id: 'apps', enabled: true, minimumLevel: 'danger', reminderHours: 24, maxReminders: 3, maxPerDay: 3 },
    { id: 'infrastructure', enabled: true, minimumLevel: 'danger', reminderHours: 24, maxReminders: 3, maxPerDay: 3 },
    { id: 'data', enabled: true, minimumLevel: 'warning', reminderHours: 24, maxReminders: 3, maxPerDay: 3 },
    { id: 'maintenance', enabled: false, minimumLevel: 'danger', reminderHours: 24, maxReminders: 3, maxPerDay: 3 },
  ],
  ...patch,
});

describe('NotificationsPage', () => {
  it('shows the mail card, the four categories in order, the read-only connection and the log', async () => {
    renderWithProviders(<NotificationsPage />);

    const mail = await card('Mail');
    expect(mail.getByLabelText(/Write to the operator by mail/)).not.toBeChecked();
    expect(mail.getByLabelText('Recipient')).toHaveValue('');
    expect(mail.getByLabelText('Mails per day (all categories)')).toHaveValue(10);

    const titles = screen.getAllByRole('region').map((region) => region.getAttribute('aria-label'));
    expect(titles.slice(0, 5)).toEqual(['Mail', 'Apps', 'Infrastructure', 'Data', 'Maintenance']);

    const data = await card('Data');
    expect(data.getByText('Backups that fail, are missing or late.')).toBeInTheDocument();
    expect(data.getByLabelText('Send from level')).toHaveValue('warning');
    expect(data.getByLabelText('Reminder every (hours)')).toHaveValue(24);
    expect(data.getByLabelText('Reminders')).toHaveValue(3);
    expect(data.getByLabelText('Mails per day')).toHaveValue(3);
    expect(data.getByLabelText(/Send mails for this category/)).toBeChecked();
    expect(data.queryByText('Off')).not.toBeInTheDocument();
    expect(data.getByText(/Defaults: On, from Warning, a reminder every 24 h/)).toBeInTheDocument();

    const maintenance = await card('Maintenance');
    expect(maintenance.getByLabelText(/Send mails for this category/)).not.toBeChecked();
    expect(maintenance.getByText('Off')).toBeInTheDocument();
    expect((await card('Apps')).queryByText('Off')).not.toBeInTheDocument();

    const connection = await card('Connection');
    expect(connection.getByText('smtp.homelab.example')).toBeInTheDocument();
    expect(connection.getByText('587')).toBeInTheDocument();
    expect(connection.getByText('In a file (never shown)')).toBeInTheDocument();
    expect(connection.getByText(/environment variables/)).toBeInTheDocument();
    expect(connection.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('shows the recent mails with the category title, the level, the reminder and the result', async () => {
    renderWithProviders(<NotificationsPage />);

    const rows = within(await screen.findByRole('table', { name: 'Recent mails' })).getAllByRole('row');
    expect(rows).toHaveLength(4);
    const [, test, failed, delivered] = rows as [HTMLElement, HTMLElement, HTMLElement, HTMLElement];
    expect(within(test).getByText('Test mail')).toBeInTheDocument();
    expect(within(failed).getByText('Data')).toBeInTheDocument();
    expect(within(failed).getByText('Danger')).toBeInTheDocument();
    expect(within(failed).getByText('Reminder 1')).toBeInTheDocument();
    expect(within(failed).getByText('Failed')).toBeInTheDocument();
    expect(within(failed).getByText(/did not accept the connection/)).toBeInTheDocument();
    expect(within(delivered).getByText('Data')).toBeInTheDocument();
    expect(within(delivered).getByText('Warning')).toBeInTheDocument();
    expect(within(delivered).getByText('First mail')).toBeInTheDocument();
    expect(within(delivered).getByText('Delivered')).toBeInTheDocument();
  });

  it('says so when no mail has been sent yet', async () => {
    server.use(http.get('/api/v1/notifications/log', () => HttpResponse.json({ entries: [] })));
    renderWithProviders(<NotificationsPage />);

    expect(await screen.findByText('No mail has been sent yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Recent mails' })).not.toBeInTheDocument();
  });

  it('shows what is wrong with the settings and with the password file', async () => {
    withSettings({
      enabled: true,
      problem: 'The recipient is missing.',
      secretProblem: 'The password file cannot be read.',
    });
    renderWithProviders(<NotificationsPage />);

    expect(await screen.findByText(/cannot work: The recipient is missing\./)).toBeInTheDocument();
    expect(screen.getByText(/password file cannot be used: The password file cannot be read\./)).toBeInTheDocument();
  });

  it('tells when the settings cannot be loaded', async () => {
    server.use(http.get('/api/v1/notifications/settings', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<NotificationsPage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load the notification settings.');
  });

  it('tells when the log cannot be loaded, the settings still working', async () => {
    server.use(http.get('/api/v1/notifications/log', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<NotificationsPage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load the log of mails.');
    expect((await card('Mail')).getByLabelText('Recipient')).toBeInTheDocument();
  });

  it('edits one category and the mail card, then sends the whole body once with the CSRF header', async () => {
    const user = userEvent.setup();
    const sent: { header: string | null; body: unknown }[] = [];
    server.use(
      http.put('/api/v1/notifications/settings', async ({ request }) => {
        const body = (await request.json()) as ReturnType<typeof savedBody>;
        sent.push({ header: request.headers.get('X-Requested-With'), body });
        return HttpResponse.json({
          ...notificationSettings,
          enabled: body.enabled,
          recipient: body.recipient,
          from: body.from,
          maxPerDay: body.maxPerDay,
          categories: notificationSettings.categories.map((c) => ({
            ...c,
            ...body.categories.find((x) => x.id === c.id),
          })),
        });
      }),
    );
    renderWithProviders(<NotificationsPage />);
    const save = await screen.findByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    const mail = await card('Mail');
    await user.click(mail.getByLabelText(/Write to the operator by mail/));
    await fill(user, mail, 'Recipient', 'operator@homelab.example');
    await fill(user, mail, 'Sender', 'portarr@homelab.example');
    await fill(user, mail, 'Mails per day (all categories)', '5');

    const data = await card('Data');
    await user.selectOptions(data.getByLabelText('Send from level'), 'danger');
    await fill(user, data, 'Reminder every (hours)', '12');
    await fill(user, data, 'Reminders', '0');
    await fill(user, data, 'Mails per day', '2');

    const maintenance = await card('Maintenance');
    await user.click(maintenance.getByLabelText(/Send mails for this category/));
    expect(maintenance.queryByText('Off')).not.toBeInTheDocument();
    expect(save).toBeEnabled();
    await user.click(save);

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    const expected = savedBody({
      enabled: true,
      recipient: 'operator@homelab.example',
      from: 'portarr@homelab.example',
      maxPerDay: 5,
    });
    expected.categories[2] = {
      id: 'data',
      enabled: true,
      minimumLevel: 'danger',
      reminderHours: 12,
      maxReminders: 0,
      maxPerDay: 2,
    };
    expected.categories[3] = {
      id: 'maintenance',
      enabled: true,
      minimumLevel: 'danger',
      reminderHours: 24,
      maxReminders: 3,
      maxPerDay: 3,
    };
    expect(sent).toEqual([{ header: 'portarr', body: expected }]);
    expect(save).toBeDisabled();
    expect((await card('Mail')).getByLabelText('Recipient')).toHaveValue('operator@homelab.example');
    expect((await card('Data')).getByLabelText('Reminder every (hours)')).toHaveValue(12);
  });

  it('puts each field error next to its field, in the right category, and the settings sentence at the top', async () => {
    const user = userEvent.setup();
    server.use(
      http.put('/api/v1/notifications/settings', () =>
        validation({
          recipient: ['recipient must be a mail address.'],
          MaxPerDay: ["'Max Per Day' must be between 1 and 1000."],
          'categories.data.reminderHours': ["'Reminder Hours' must be between 1 and 720."],
          'categories.Apps.MinimumLevel': ['minimumLevel must be info, warning or danger.'],
          'categories.maintenance.maxPerDay': ["'Max Per Day' must be between 1 and 1000."],
          'categories[1].id': ['The category is repeated.'],
          settings: ['Set the mail server before turning notifications on.'],
        }),
      ),
    );
    renderWithProviders(<NotificationsPage />);

    const mail = await card('Mail');
    await user.click(mail.getByLabelText(/Write to the operator by mail/));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    const alerts = await screen.findAllByRole('alert');
    expect(alerts[0]).toHaveTextContent('Set the mail server before turning notifications on.');
    expect(alerts[0]).toHaveTextContent('The category is repeated.');

    const field = (scope: ReturnType<typeof within>, label: string) =>
      within(scope.getByLabelText(label).parentElement as HTMLElement);
    expect(field(mail, 'Recipient').getByText('recipient must be a mail address.')).toBeInTheDocument();
    expect(field(mail, 'Mails per day (all categories)').getByText(/must be between 1 and 1000/)).toBeInTheDocument();

    const data = await card('Data');
    expect(field(data, 'Reminder every (hours)').getByText(/must be between 1 and 720/)).toBeInTheDocument();
    expect(field(data, 'Mails per day').queryByText(/must be/)).not.toBeInTheDocument();
    expect(data.getByLabelText('Reminder every (hours)')).toHaveAttribute('aria-invalid', 'true');

    const apps = await card('Apps');
    expect(field(apps, 'Send from level').getByText(/minimumLevel must be/)).toBeInTheDocument();
    expect(field(apps, 'Reminder every (hours)').queryByText(/must be/)).not.toBeInTheDocument();

    const maintenance = await card('Maintenance');
    expect(field(maintenance, 'Mails per day').getByText(/must be between 1 and 1000/)).toBeInTheDocument();
    expect((await card('Infrastructure')).queryByText(/must be/)).not.toBeInTheDocument();
    expect(screen.queryByText('Saved.')).not.toBeInTheDocument();
  });

  it('refuses an empty number without calling the server, in the category that has it', async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      http.put('/api/v1/notifications/settings', () => {
        called = true;
        return HttpResponse.json(notificationSettings);
      }),
    );
    renderWithProviders(<NotificationsPage />);

    const apps = await card('Apps');
    await user.clear(apps.getByLabelText('Mails per day'));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await apps.findByText('This must be a whole number.')).toBeInTheDocument();
    expect((await card('Data')).queryByText('This must be a whole number.')).not.toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('restores the defaults of a category in the form without saving', async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      http.put('/api/v1/notifications/settings', () => {
        called = true;
        return HttpResponse.json(notificationSettings);
      }),
    );
    renderWithProviders(<NotificationsPage />);

    const data = await card('Data');
    const reset = data.getByRole('button', { name: 'Reset to defaults' });
    expect(reset).toBeDisabled();
    await user.selectOptions(data.getByLabelText('Send from level'), 'info');
    await fill(user, data, 'Reminders', '9');
    await user.click(data.getByLabelText(/Send mails for this category/));
    expect(reset).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();

    await user.click(reset);

    expect(data.getByLabelText('Send from level')).toHaveValue('warning');
    expect(data.getByLabelText('Reminders')).toHaveValue(3);
    expect(data.getByLabelText(/Send mails for this category/)).toBeChecked();
    expect(reset).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(called).toBe(false);
  });

  it.each([
    [401, /session ended/],
    [403, /may not change/],
    [500, /Unable to save/],
  ])('explains a %i answer', async (status, message) => {
    const user = userEvent.setup();
    server.use(http.put('/api/v1/notifications/settings', () => new HttpResponse(null, { status })));
    renderWithProviders(<NotificationsPage />);

    await user.click((await card('Mail')).getByLabelText(/Write to the operator by mail/));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(message));
  });

  it('sends a test mail without a body, says it was sent and reads the log again', async () => {
    const user = userEvent.setup();
    let tests = 0;
    let logReads = 0;
    server.use(
      http.post('/api/v1/notifications/test', async ({ request }) => {
        tests += 1;
        expect(await request.text()).toBe('');
        return HttpResponse.json({ ok: true, error: null });
      }),
      http.get('/api/v1/notifications/log', () => {
        logReads += 1;
        return HttpResponse.json({ entries: logReads === 1 ? [] : notificationLog.entries.slice(0, 1) });
      }),
    );
    renderWithProviders(<NotificationsPage />);
    expect(await screen.findByText('No mail has been sent yet.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Send a test mail' }));

    expect(await screen.findByText(/Sent\. Look in the recipient mailbox/)).toBeInTheDocument();
    expect(tests).toBe(1);
    expect(await screen.findByText('Test mail')).toBeInTheDocument();
    expect(screen.queryByText('No mail has been sent yet.')).not.toBeInTheDocument();
  });

  it('says why a test mail was not sent', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('/api/v1/notifications/test', () => HttpResponse.json({ ok: false, error: 'Notifications are off.' })),
    );
    renderWithProviders(<NotificationsPage />);

    await user.click(await screen.findByRole('button', { name: 'Send a test mail' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Not sent: Notifications are off.');
    expect(screen.queryByText(/Sent\. Look/)).not.toBeInTheDocument();
  });

  it('tells when the test call itself fails', async () => {
    const user = userEvent.setup();
    server.use(http.post('/api/v1/notifications/test', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<NotificationsPage />);

    await user.click(await screen.findByRole('button', { name: 'Send a test mail' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The test could not be started');
  });

  it('is part of the Settings pages for a role with settings.manage', async () => {
    server.use(
      http.get('/api/v1/auth/me', () =>
        HttpResponse.json({ ...me, role: 'Operator', permissions: ['settings.manage', 'users.manage'] }),
      ),
    );
    renderWithProviders(<NotificationsPage />, '/settings/notifications');

    expect(await screen.findByRole('link', { name: 'Notifications' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Users & Auth' })).toHaveAttribute('href', '/settings/users');
  });
});
