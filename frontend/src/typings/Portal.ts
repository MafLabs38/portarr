/** The colors of the badge texts: a hex color (`#rgb`, `#rrggbb`) or a theme token name (accent, ink, inkMuted…). */
export interface PortalColors {
  name: string;
  version: string;
}

export default interface Portal {
  /** The name of the infrastructure; Portarr's own when the catalog says nothing (D-91). */
  name: string;
  /** The custom logo, with a hash of the file in its address; null for Portarr's own logo. */
  logoUrl: string | null;
  /** The version of the running Portarr. */
  version: string;
  /** Why a declared logo is not used. */
  logoProblem: string | null;
  /** Whether the name and logo can be changed through the API (the catalog is writable). */
  writable: boolean;
  /** The colors of the name and of the version in the badge; Portarr's accent by default. */
  colors: PortalColors;
}
