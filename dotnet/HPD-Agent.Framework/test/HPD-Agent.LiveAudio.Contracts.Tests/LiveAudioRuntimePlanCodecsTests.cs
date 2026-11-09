using HPD.Agent.Audio.Authority;using HPD.Agent.Authority;
namespace HPD.Agent.LiveAudio.Contracts.Tests;
public sealed class LiveAudioRuntimePlanCodecsTests
{
 [Fact]public void Runtime_plan_round_trips_full_projection(){var schema=new SchemaReferenceV1(SchemaId.Create(),1,0);var h=Hash256.Compute([1]);var participant=new ParticipantDescriptorV1(ParticipantId.Create(),OwnerSliceId.S2,schema,[],h);var constraintsBytes=new byte[]{1,2};var constraints=new LoweredConstraintSetV1(schema,constraintsBytes,Hash256.Compute(constraintsBytes));var charge=new CapacityChargeTemplateV1(1,CapacityScopeKindV1.Session,5,CapacityPurposeId.Create());var v=new LiveAudioRuntimePlan(LiveAudioPlanId.Create(),h,1,h,[participant],constraints,[charge],h,schema,h);var b=LiveAudioRuntimePlanCodecs.Encode(v);Assert.True(LiveAudioRuntimePlanCodecs.TryDecode(b,out var d));Assert.Equal(v.PlanId,d!.PlanId);Assert.Single(d.Participants);Assert.Single(d.ChargeTemplates);Assert.Equal(LiveAudioRuntimePlanCodecs.ComputeHash(v),LiveAudioRuntimePlanCodecs.ComputeHash(d));}
 [Fact]public void Runtime_plan_fails_closed(){Assert.False(LiveAudioRuntimePlanCodecs.TryDecode(new byte[]{0xff},out _));}
}
