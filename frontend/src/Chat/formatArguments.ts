/** A tool's arguments as indented JSON, for people (the panel never interprets them). */
export default function formatArguments(input: unknown): string {
  if (input === undefined || input === null) {
    return '{}';
  }
  try {
    return JSON.stringify(input, null, 2);
  } catch {
    // Only an object that cannot be serialized (a cycle) ends up here.
    return typeof input === 'object' ? '{…}' : String(input);
  }
}
