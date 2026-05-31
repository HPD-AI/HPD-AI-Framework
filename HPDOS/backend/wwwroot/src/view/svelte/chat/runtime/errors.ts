const expectedPatternError = /string did not match the expected pattern/i;

export function chatErrorMessage(error: unknown, fallback: string): string {
  const message = error instanceof Error ? error.message : "";

  if (expectedPatternError.test(message)) {
    return `${fallback} The browser rejected an internal URL; check the HPD-Agent API base path and live connection route.`;
  }

  return message || fallback;
}
