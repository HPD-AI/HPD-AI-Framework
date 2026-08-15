export type GatewayObservabilityLink = Readonly<{
  id: string;
  label: string;
  kind: 'metrics' | 'traces' | 'logs' | 'dashboard';
  href: string;
}>;

export function validateGatewayObservabilityLinks(
  values: readonly GatewayObservabilityLink[],
): readonly GatewayObservabilityLink[] {
  if (values.length > 16) throw new RangeError('Gateway observability link count exceeded.');
  const ids = new Set<string>();
  let bytes = 0;
  const result = values.map(value => {
    if (!/^[!-~]{1,64}$/.test(value.id) || value.id.normalize('NFC') !== value.id || ids.has(value.id))
      throw new TypeError('Gateway observability link identity is invalid.');
    ids.add(value.id);
    if (value.label.normalize('NFC') !== value.label || new TextEncoder().encode(value.label).length < 1 ||
      new TextEncoder().encode(value.label).length > 128 || /\p{Cc}/u.test(value.label))
      throw new TypeError('Gateway observability link label is invalid.');
    if (!['metrics', 'traces', 'logs', 'dashboard'].includes(value.kind))
      throw new TypeError('Gateway observability link kind is invalid.');
    let url: URL;
    try { url = new URL(value.href); }
    catch { throw new TypeError('Gateway observability URL is invalid.'); }
    if (url.protocol !== 'https:' || url.username || url.password || url.hash || /[{}]/.test(value.href))
      throw new TypeError('Gateway observability URL is unsafe.');
    const copy = Object.freeze({ ...value });
    bytes += new TextEncoder().encode(JSON.stringify(copy)).length;
    return copy;
  });
  if (bytes > 16_384) throw new RangeError('Gateway observability link catalog is too large.');
  return Object.freeze(result.sort((left, right) => left.id < right.id ? -1 : left.id > right.id ? 1 : 0));
}
