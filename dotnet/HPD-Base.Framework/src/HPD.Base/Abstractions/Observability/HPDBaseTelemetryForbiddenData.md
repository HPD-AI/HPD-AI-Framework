# HPD.BASE Telemetry Forbidden Data

HPD.BASE telemetry is safe-by-default and deny-by-default for attribute values.

Do not emit these values in spans, span events, metrics, structured logs, or `OperationDiagnostics.SafeData`:

- record ids, object ids, object keys, file names, channel names, event ids, client realtime refs, causation ids;
- user ids, subject ids, service principal ids, tenant ids, tenant memberships, project ids, session ids, credential ids, grant ids;
- auth tokens, cookies, authorization headers, refresh tokens, API keys, claim values, role names, display names, usernames, email addresses;
- record payloads, patch payloads, file contents, request bodies, response bodies, realtime snapshots, before/after payloads, vector contents, metadata values;
- raw query filters, query values, sort/include field names unless explicitly schema-defined and allowlisted, `ValidationIssue.RejectedValue`;
- raw SQL, generated SQL, SQL parameters, command text, PRAGMA command text, native definitions, native expressions, JSON paths derived from user fields;
- connection strings, database paths, SQLite `DataSource`, WAL/SHM file names, provider secrets;
- `BaseError.Target`, `ConflictInfo.Resource`, revision tokens, ETags, checksums, idempotency keys;
- exception stack traces as attributes, provider native exception messages, `StoreErrorInfo.NativeMessage`;
- request IP address, user agent, raw route values, query strings, arbitrary client metadata.

Sensitive or diagnostic modes must still not emit raw SQL, SQL parameters, payload values, file contents, tokens, claim values, raw ids, object keys, checksums, native paths, or native provider messages.
