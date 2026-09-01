using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal static class GraphRuntimePayloadRegistrationsV1
{
    internal static AuthorityPayloadRegistrationV1 Command { get; } = Register(GraphRuntimeCodecsV1.CommandOuterSchemaId,
        ValidateCommandPayload);
    internal static AuthorityPayloadRegistrationV1 Fact { get; } = Register(GraphRuntimeCodecsV1.FactOuterSchemaId,
        ValidateFactPayload);

    internal static bool ValidateCommandEnvelope(AuthorityFactEnvelopeV1 envelope)
    {
        try
        {
        if(envelope is null||!ValidEnvelope(envelope,Command)||!GraphRuntimeCodecsV1.TryDecodeOuter(envelope.PayloadMemory,out var outer)||
            !GraphRuntimeCodecsV1.TryDecodeCommand(outer!.Body,out var command)||command is null||
            envelope.FactId!=GraphRuntimeFactIdsV1.Command(envelope.Position.Session,command.OperationId,command.Kind)||
            command.ExpectedPredecessor.Sequence>=envelope.Position.Sequence)return false;
        return command switch
        {
            GraphRuntimeCommandV1.Activate a=>a.GraphAuthorityFact.Sequence<envelope.Position.Sequence&&a.CapacityGrantFact.Sequence<envelope.Position.Sequence&&
                a.EffectRequestHash==GraphRuntimeEffectHashesV1.Activate(outer.Session,a.OperationId,a.GraphAuthorityFact,a.TopologyFingerprint,a.GraphGeneration,a.CapacityGrantFact),
            GraphRuntimeCommandV1.Retire r=>r.ActiveRuntimeFact.Sequence<envelope.Position.Sequence&&
                r.EffectRequestHash==GraphRuntimeEffectHashesV1.Retire(outer.Session,r.OperationId,r.ActiveRuntimeFact),_=>false
        };
        }
        catch(Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException) { return false; }
    }

    internal static bool ValidateFactEnvelope(AuthorityFactEnvelopeV1 envelope)
    {
        try
        {
        if(envelope is null||!ValidEnvelope(envelope,Fact)||!GraphRuntimeCodecsV1.TryDecodeOuter(envelope.PayloadMemory,out var outer)||
            !GraphRuntimeCodecsV1.TryDecodeFact(outer!.Body,out var fact)||fact is null)return false;
        var success=fact.Outcome is GraphRuntimeOutcomeV1.Activated or GraphRuntimeOutcomeV1.Retired;
        return envelope.FactId==GraphRuntimeFactIdsV1.Result(fact.CommandFact)&&fact.CommandFact.Sequence<envelope.Position.Sequence&&
            fact.ExpectedPredecessor.Sequence<fact.CommandFact.Sequence&&fact.ActualPredecessor.Sequence<fact.CommandFact.Sequence&&
            (fact.ResultingSnapshot is null||(success?fact.ResultingSnapshot.LastRuntimeFact==envelope.Position:
                fact.ResultingSnapshot.LastRuntimeFact.Sequence<envelope.Position.Sequence));
        }
        catch(Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException) { return false; }
    }

    private static bool ValidateCommandPayload(ReadOnlyMemory<byte> payload,SessionAuthorityStampV1 session)
    {
        try
        {
            if(!session.IsValid||!GraphRuntimeCodecsV1.TryDecodeOuter(payload,out var outer)||outer!.Session!=session||
                !GraphRuntimeCodecsV1.TryDecodeCommand(outer.Body,out var command)||command is null||command.ExpectedPredecessor.Session!=session)return false;
            return command switch{GraphRuntimeCommandV1.Activate a=>a.GraphAuthorityFact.Session==session&&a.CapacityGrantFact.Session==session,
                GraphRuntimeCommandV1.Retire r=>r.ActiveRuntimeFact.Session==session,_=>false};
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or OverflowException){return false;}
    }

    private static bool ValidateFactPayload(ReadOnlyMemory<byte> payload,SessionAuthorityStampV1 session)
    {
        try
        {
            if(!session.IsValid||!GraphRuntimeCodecsV1.TryDecodeOuter(payload,out var outer)||outer!.Session!=session||
                !GraphRuntimeCodecsV1.TryDecodeFact(outer.Body,out var fact)||fact is null||fact.CommandFact.Session!=session||
                fact.ExpectedPredecessor.Session!=session||fact.ActualPredecessor.Session!=session)return false;
            var s=fact.ResultingSnapshot;return s is null||(s.CurrentAuthority.Session==session&&s.CapacityGrantFact.Session==session&&
                s.ActivationFact.Session==session&&s.LastRuntimeFact.Session==session&&
                (s.Retirement is null||s.Retirement.RetireCommandFact.Session==session));
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or OverflowException){return false;}
    }

    private static bool ValidEnvelope(AuthorityFactEnvelopeV1 e,AuthorityPayloadRegistrationV1 r)=>
        e.Position.Session.IsValid&&e.Owner==OwnerSliceId.S2&&e.ThreadScope is null&&e.PayloadSchema==r.Schema&&
        e.PayloadHash==AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,e.PayloadBytes)&&r.Validate(e.PayloadMemory,e.Position.Session);
    private static AuthorityPayloadRegistrationV1 Register(string schema,Func<ReadOnlyMemory<byte>,SessionAuthorityStampV1,bool> validate)=>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(schema),1,0,OwnerSliceId.S2,GraphRuntimeCodecsV1.MaximumOuterBytes,validate);
}
