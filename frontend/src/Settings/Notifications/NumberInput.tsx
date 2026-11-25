import form from 'Components/Form/Form.module.css';

import styles from './Notifications.module.css';

interface NumberInputProps {
  id: string;
  label: string;
  help: string;
  min: number;
  max: number;
  value: string;
  errors: string[] | undefined;
  onChange: (value: string) => void;
}

/** A whole number with its label, its range as a hint and the server's errors under it. */
export default function NumberInput({
  id,
  label,
  help,
  min,
  max,
  value,
  errors,
  onChange,
}: Readonly<NumberInputProps>) {
  return (
    <div className={form.field}>
      <label className={form.label} htmlFor={id}>
        {label}
      </label>
      <input
        aria-describedby={`${id}-help`}
        aria-invalid={errors ? true : undefined}
        className={form.input}
        id={id}
        inputMode="numeric"
        max={max}
        min={min}
        onChange={(event) => onChange(event.target.value)}
        type="number"
        value={value}
      />
      <span className={form.help} id={`${id}-help`}>
        {help}
      </span>
      {errors?.map((message) => (
        <p className={styles.fieldError} key={message}>
          {message}
        </p>
      ))}
    </div>
  );
}
