namespace HPD.Base.Events;

public enum EventResourceKind { Runtime, Collection, Record, Schema, Module, Capability, Custom }
public enum EventDeliveryGuarantee { BestEffort, DurableEnqueued, DurablePublished, Transactional }
