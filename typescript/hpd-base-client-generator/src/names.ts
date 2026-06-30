const reserved = new Set([
  "$base", "$metadata", "$snapshot", "collection", "collections", "__proto__", "constructor", "prototype",
  "default", "class", "function", "return", "const", "let", "var", "import", "export", "delete"
]);

export function safePropertyName(input: string): string {
  const parts = input.trim().split(/[^A-Za-z0-9]+/).filter(Boolean);
  const raw = parts.length === 0 ? "collection" : parts.map((part, index) => {
    const lower = part.charAt(0).toLowerCase() + part.slice(1);
    return index === 0 ? lower : lower.charAt(0).toUpperCase() + lower.slice(1);
  }).join("");
  const identifier = raw.replace(/^[^A-Za-z_$]+/, "");
  const value = identifier && /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(identifier) ? identifier : "collection";
  return reserved.has(value) ? `${value}_` : value;
}

export function safeTypeName(input: string, suffix = ""): string {
  const parts = input.trim().split(/[^A-Za-z0-9]+/).filter(Boolean);
  const base = (parts.length ? parts : ["Generated"]).map(part => part.charAt(0).toUpperCase() + part.slice(1)).join("");
  const value = base.replace(/^[^A-Za-z_$]+/, "") || "Generated";
  return `${value}${suffix}`;
}

export function uniqueName(preferred: string, used: Set<string>): string {
  if (!used.has(preferred)) {
    used.add(preferred);
    return preferred;
  }
  let index = 2;
  while (used.has(`${preferred}_${index}`)) index += 1;
  const value = `${preferred}_${index}`;
  used.add(value);
  return value;
}

export function stringLiteral(value: string): string {
  return JSON.stringify(value);
}
