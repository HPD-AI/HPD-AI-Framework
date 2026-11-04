# HPD-Agent.Sandbox.HostProcess

Agent adapter for the `HPD.Environment.HostProcess` backend. This package
selects the Environment provider, creates a real host and execution unit, and
publishes the shared runtime process binding. It does not contain operating
system sandbox or network-enforcement implementations.

## Install

```bash
dotnet add package HPD-Agent.Sandbox.HostProcess
```

## Use When

Use this package when an Agent runtime should execute through the direct Host
Process Environment backend.
