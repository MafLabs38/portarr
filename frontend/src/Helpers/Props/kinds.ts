// Prop vocabulary shared with the *arr apps (DESIGN.md §1.4).
export const DEFAULT = 'default';
export const PRIMARY = 'primary';
export const SUCCESS = 'success';
export const WARNING = 'warning';
export const DANGER = 'danger';
export const INFO = 'info';
export const INVERSE = 'inverse';
export const DISABLED = 'disabled';

export type Kind =
  | typeof DEFAULT
  | typeof PRIMARY
  | typeof SUCCESS
  | typeof WARNING
  | typeof DANGER
  | typeof INFO
  | typeof INVERSE
  | typeof DISABLED;
