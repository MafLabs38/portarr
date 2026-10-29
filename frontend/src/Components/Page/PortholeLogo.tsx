interface PortholeLogoProps {
  size: number;
}

/** The Portarr porthole (DESIGN.md §6.2). Decorative: the product name is always next to it. */
export default function PortholeLogo({ size }: Readonly<PortholeLogoProps>) {
  return (
    <svg aria-hidden="true" height={size} viewBox="0 0 32 32" width={size}>
      <circle cx="16" cy="16" fill="var(--accent)" r="15" />
      <circle cx="16" cy="16" fill="var(--paperSunken)" r="10" />
      <circle cx="16" cy="16" fill="none" r="7" stroke="var(--secondary)" strokeWidth="2" />
      {[0, 90, 180, 270].map((angle) => (
        <circle cx="16" cy="3.5" fill="var(--accentDeep)" key={angle} r="1.5" transform={`rotate(${angle} 16 16)`} />
      ))}
    </svg>
  );
}
