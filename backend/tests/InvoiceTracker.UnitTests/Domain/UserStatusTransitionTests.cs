using FluentAssertions;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;

namespace InvoiceTracker.UnitTests.Domain;

/// <summary>
/// Direct unit tests for <see cref="UserBusinessMembership.TransitionTo"/> — the single place a
/// membership's status workflow's legal transitions are centralized (see architecture.md's "Domain
/// invariants: centralized status transitions" and its test plan's "Status transitions" bullet:
/// "User (Active→Deactivated and Deactivated→Active both succeed, since UserStatus is explicitly
/// bidirectional)"). Mirrors <see cref="InvoiceStatusTransitionTests"/>'s style, scaled down to
/// UserBusinessMembership's 2-status, bidirectional machine.
/// </summary>
public class UserStatusTransitionTests
{
    private static readonly Guid BusinessId = Guid.NewGuid();

    private static UserBusinessMembership MakeMembership() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        BusinessId = BusinessId,
        Role = UserRole.Staff
    };

    [Fact]
    public void ActiveToDeactivated_Succeeds_AndSetsStatusAndStatusChangedAt()
    {
        var membership = MakeMembership();
        var before = membership.StatusChangedAt;

        membership.TransitionTo(UserStatus.Deactivated);

        membership.Status.Should().Be(UserStatus.Deactivated);
        membership.StatusChangedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void DeactivatedToActive_Succeeds_AndSetsStatusAndStatusChangedAt()
    {
        var membership = MakeMembership();
        membership.TransitionTo(UserStatus.Deactivated);
        var afterDeactivation = membership.StatusChangedAt;

        membership.TransitionTo(UserStatus.Active);

        membership.Status.Should().Be(UserStatus.Active);
        membership.StatusChangedAt.Should().BeOnOrAfter(afterDeactivation);
    }

    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Deactivated)]
    public void SameStatusNoOpTransition_IsRejected(UserStatus status)
    {
        var membership = MakeMembership();
        if (status == UserStatus.Deactivated)
        {
            membership.TransitionTo(UserStatus.Deactivated);
        }

        var act = () => membership.TransitionTo(status);

        act.Should().Throw<InvalidUserStatusTransitionException>()
            .Where(ex => ex.CurrentStatus == status && ex.TargetStatus == status);
        membership.Status.Should().Be(status);
    }
}
