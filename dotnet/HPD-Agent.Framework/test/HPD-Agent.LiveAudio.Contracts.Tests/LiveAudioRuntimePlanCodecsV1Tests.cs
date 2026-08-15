using HPD.Agent.Audio.Authority;using HPD.Agent.Authority;
namespace HPD.Agent.LiveAudio.Contracts.Tests;
public sealed class LiveAudioRuntimePlanCodecsV1Tests
{
 [Fact]public void Runtime_plan_round_trips_full_projection(){var schema=new SchemaReferenceV1(SchemaId.Create(),1,0);var h=Hash256.Compute([1]);var participant=new ParticipantDescriptorV1(ParticipantId.Create(),OwnerSliceId.S2,schema,[],h);var constraintsBytes=new byte[]{1,2};var constraints=new LoweredConstraintSetV1(schema,constraintsBytes,Hash256.Compute(constraintsBytes));var charge=new CapacityChargeTemplateV1(1,CapacityScopeKindV1.Session,5,CapacityPurposeId.Create());var v=new LiveAudioRuntimePlanV1(LiveAudioPlanId.Create(),h,1,h,[participant],constraints,[charge],h,schema,h);var b=LiveAudioRuntimePlanCodecsV1.Encode(v);Assert.True(LiveAudioRuntimePlanCodecsV1.TryDecode(b,out var d));Assert.Equal(v.PlanId,d!.PlanId);Assert.Single(d.Participants);Assert.Single(d.ChargeTemplates);Assert.Equal(LiveAudioRuntimePlanCodecsV1.ComputeHash(v),LiveAudioRuntimePlanCodecsV1.ComputeHash(d));}
 [Fact]public void Runtime_plan_fails_closed(){Assert.False(LiveAudioRuntimePlanCodecsV1.TryDecode(new byte[]{0xff},out _));}
}
