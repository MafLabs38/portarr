import translate from 'Utilities/String/translate';

/** CPU in cores (D-33): "0.25 vCPU"; tiny values keep three decimals so idle is not shown as zero. */
export default function formatCores(cores: number): string {
  const digits = cores > 0 && cores < 0.1 ? 3 : 2;
  return translate('CoresValue', { value: cores.toFixed(digits) });
}
