namespace HPD.Base;

/// <summary>Defines the event resource kind contract.</summary>
public enum EventResourceKind { /// <summary>Identifies runtime.</summary>
Runtime, /// <summary>Identifies collection.</summary>
Collection, /// <summary>Identifies record.</summary>
Record, /// <summary>Identifies schema.</summary>
Schema, /// <summary>Identifies module.</summary>
Module, /// <summary>Identifies capability.</summary>
Capability, /// <summary>Identifies custom.</summary>
Custom }
/// <summary>Defines the event delivery guarantee contract.</summary>
public enum EventDeliveryGuarantee { /// <summary>Identifies best effort.</summary>
BestEffort, /// <summary>Identifies durable enqueued.</summary>
DurableEnqueued, /// <summary>Identifies durable published.</summary>
DurablePublished, /// <summary>Identifies transactional.</summary>
Transactional }
