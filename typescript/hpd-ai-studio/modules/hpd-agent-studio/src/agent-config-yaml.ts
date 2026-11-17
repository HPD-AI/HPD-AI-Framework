/** Serializes a portable AgentConfig projection without adding YAML-specific runtime behavior. */
export function materializeAgentConfigYaml(value: unknown): string {
  return `${writeValue(value, 0)}\n`;
}

function writeValue(value: unknown, depth: number): string {
  if (value === null) return "null";
  if (typeof value === "string") return JSON.stringify(value);
  if (typeof value === "number") {
    if (!Number.isFinite(value)) throw new TypeError("Agent configuration contains a non-finite number.");
    return String(value);
  }
  if (typeof value === "boolean") return value ? "true" : "false";
  if (Array.isArray(value)) {
    if (value.length === 0) return "[]";
    const indent = "  ".repeat(depth);
    return value.map(item => {
      const rendered = writeValue(item, depth + 1);
      if (item !== null && typeof item === "object" && !Array.isArray(item))
        return `${indent}-\n${indent}${rendered.split("\n").join(`\n${indent}`)}`;
      return `${indent}- ${rendered}`;
    }).join("\n");
  }
  if (value && typeof value === "object") {
    const entries = Object.entries(value as Record<string, unknown>)
      .filter(([, member]) => member !== undefined);
    if (entries.length === 0) return "{}";
    const indent = "  ".repeat(depth);
    return entries.map(([key, member]) => {
      const rendered = writeValue(member, depth + 1);
      if (member !== null && typeof member === "object" && !Array.isArray(member) &&
          Object.keys(member as Record<string, unknown>).length > 0)
        return `${indent}${yamlKey(key)}:\n${rendered}`;
      if (Array.isArray(member) && member.length > 0)
        return `${indent}${yamlKey(key)}:\n${rendered}`;
      return `${indent}${yamlKey(key)}: ${rendered}`;
    }).join("\n");
  }
  throw new TypeError("Agent configuration contains an unsupported value.");
}

function yamlKey(value: string): string {
  return /^[A-Za-z_][A-Za-z0-9_-]*$/u.test(value) ? value : JSON.stringify(value);
}
