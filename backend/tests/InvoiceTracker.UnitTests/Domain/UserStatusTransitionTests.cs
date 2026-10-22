using FluentAssertions;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;

namespace InvoiceTracker.UnitTests.Domain;

/// <summary>
/// Direct unit tests for <see cref="User.TransitionTo"/> — the single place a user's status
/// workflow's legal transitions are centralized (see architecture.md's "Domain invariants:
/// centralized status transitions" and its test plan's "Status transitions" bullet: "User
/// (Active→Deactivated and Deactivated→Active both succeed, since UserStatus is explicitly
/// bidirectional)"). Mirrors <see cref="InvoiceStatusTransitionTests"/>'s style, scaled down to
/// User's 2-status, bidirectional machine.
/// </summary>
public class UserStatusTransitionTests
{
    private static readonly Guid BusinessId = Guid.NewGuid();

    private static User MakeUser() => new()
    {
        Id = Guid.NewGuid(),
        BusinessId = BusinessId,
        Email = "staff@example.com",
        Role = UserRole.Staff
    };

    [Fact]
    public void ActiveToDeactivated_Succeeds_AndSetsStatusAndStatusChangedAt()
    {
        var user = MakeUser();
        var before = user.StatusChangedAt;

        user.TransitionTo(UserStatus.Deactivated);

        user.Status.Should().Be(UserStatus.Deactivated);
        user.StatusChangedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void DeactivatedToActive_Succeeds_AndSetsStatusAndStatusChangedAt()
    {
        var user = MakeUser();
        user.TransitionTo(UserStatus.Deactivated);
        var afterDeactivation = user.StatusChangedAt;

        user.TransitionTo(UserStatus.Active);

        user.Status.Should().Be(UserStatus.Active);
        user.StatusChangedAt.Should().BeOnOrAfter(afterDeactivation);
    }

    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Deactivated)]
    public void SameStatusNoOpTransition_IsRejected(UserStatus status)
    {
        var user = MakeUser();
        if (status == UserStatus.Deactivated)
        {
            user.TransitionTo(UserStatus.Deactivated);
        }

        var act = () => user.TransitionTo(status);

        act.Should().Throw<InvalidUserStatusTransitionException>()
            .Where(ex => ex.CurrentStatus == status && ex.TargetStatus == status);
        user.Status.Should().Be(status);
    }
}
