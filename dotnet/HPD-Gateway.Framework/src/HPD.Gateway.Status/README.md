# HPD.Gateway.Status

Bounded node-local status and readiness for the HPD-owned YARP publication
path. Register after YARP publication:

```csharp
services.AddReverseProxy();
services.AddHpdGatewayYarpPublication();
services.AddHpdGatewayStatus();

app.MapHpdGatewayHealth();
app.MapReverseProxy();
```

`IGatewayStatusReader` returns one immutable current snapshot and a one-shot
invalidation token. `/health/live` is process-local. `/health/ready` exposes
only the readiness schema, boolean, process-local sequence/time, and closed
reason codes.

This package does not provide an Admin API, history, provider freshness,
fleet readiness, telemetry authority, per-destination status, or an event
stream. `HPD-Events` may be used by a future optional downstream projection;
it is not required to establish status truth.
