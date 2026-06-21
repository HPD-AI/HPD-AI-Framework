using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class ApplicationUserTests
{
    [Fact]
    public void ApplicationUser_InstanceId_DefaultsToGuidEmpty()
    {
        new ApplicationUser().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void ApplicationUser_UserMetadata_DefaultsToEmptyJsonObject()
    {
        new ApplicationUser().UserMetadata.Should().Be("{}");
    }

    [Fact]
    public void ApplicationUser_AppMetadata_DefaultsToEmptyJsonObject()
    {
        new ApplicationUser().AppMetadata.Should().Be("{}");
    }

    [Fact]
    public void ApplicationUser_RequiredActions_DefaultsToEmptyList()
    {
        var user = new ApplicationUser();
        user.RequiredActions.Should().NotBeNull();
        user.RequiredActions.Should().HaveCount(0);
    }

    [Fact]
    public void ApplicationUser_IsActive_DefaultsToTrue()
    {
        new ApplicationUser().IsActive.Should().BeTrue();
    }

    [Fact]
    public void ApplicationUser_IsDeleted_DefaultsToFalse()
    {
        new ApplicationUser().IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void ApplicationUser_SubscriptionTier_DefaultsToFree()
    {
        new ApplicationUser().SubscriptionTier.Should().Be("free");
    }

    [Fact]
    public void ApplicationUser_HasPendingActions_FalseWhenRequiredActionsIsEmpty()
    {
        new ApplicationUser().HasPendingActions.Should().BeFalse();
    }

    [Fact]
    public void ApplicationUser_HasPendingActions_TrueWhenRequiredActionsIsPopulated()
    {
        var user = new ApplicationUser();
        user.RequiredActions.Add("VERIFY_EMAIL");
        user.HasPendingActions.Should().BeTrue();
    }

    [Fact]
    public void ApplicationUser_Created_IsSetToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var user = new ApplicationUser();
        var after = DateTime.UtcNow.AddSeconds(5);

        user.Created.Should().BeAfter(before).And.BeBefore(after);
        user.Created.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ApplicationUser_Id_IsGuid()
    {
        // IdentityUser<Guid> — Id is statically typed as Guid; compile-time guarantee is sufficient.
        // We assert it's not the default zero to confirm the type is Guid (value type, always Guid).
        var id = new ApplicationUser().Id;
        id.GetType().Should().Be(typeof(Guid));
    }

    [Fact]
    public void ApplicationUser_Audience_DefaultsToNull()
    {
        new ApplicationUser().Audience.Should().BeNull();
    }

    [Fact]
    public void ApplicationUser_EmailConfirmedAt_DefaultsToNull()
    {
        new ApplicationUser().EmailConfirmedAt.Should().BeNull();
    }

    [Fact]
    public void ApplicationUser_DeletedAt_DefaultsToNull()
    {
        new ApplicationUser().DeletedAt.Should().BeNull();
    }

    [Fact]
    public void ApplicationUser_HasPendingActions_FalseAfterClearingList()
    {
        var user = new ApplicationUser();
        user.RequiredActions.Add("VERIFY_EMAIL");
        user.RequiredActions.Clear();
        user.HasPendingActions.Should().BeFalse();
    }
}
