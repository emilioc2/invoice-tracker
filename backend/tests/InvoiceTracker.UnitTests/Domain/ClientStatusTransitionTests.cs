using FluentAssertions;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Domain;

/// <summary>
/// Direct unit tests for <see cref="Client.TransitionTo"/> — the single place a client's status
/// workflow's legal transitions are centralized (see architecture.md's "Domain invariants:
/// centralized status transitions" and its test plan's "Status transitions" bullet: "the same
/// pattern for Client (Active→Removed succeeds, there is no reverse transition to test)").
/// Mirrors <see cref="InvoiceStatusTransitionTests"/>'s style, scaled down to Client's 2-status,
/// 1-legal-transition machine.
/// </summary>
public class ClientStatusTransitionTests
{
    private static readonly Guid BusinessId = Guid.NewGuid();

    private static Client MakeClient() => InvoiceSeeding.MakeClient(BusinessId);

    [Fact]
    public void ActiveToRemoved_Succeeds_AndSetsStatusAndStatusChangedAt()
    {
        var client = MakeClient();
        var before = client.StatusChangedAt;

        client.TransitionTo(ClientStatus.Removed);

        client.Status.Should().Be(ClientStatus.Removed);
        client.StatusChangedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void RemovedToActive_Throws_AndLeavesStatusUnchanged()
    {
        var client = MakeClient();
        client.TransitionTo(ClientStatus.Removed);
        var statusChangedAtAfterRemoval = client.StatusChangedAt;

        var act = () => client.TransitionTo(ClientStatus.Active);

        act.Should().Throw<InvalidClientStatusTransitionException>()
            .Where(ex => ex.CurrentStatus == ClientStatus.Removed && ex.TargetStatus == ClientStatus.Active);
        client.Status.Should().Be(ClientStatus.Removed);
        client.StatusChangedAt.Should().Be(statusChangedAtAfterRemoval);
    }

    [Theory]
    [InlineData(ClientStatus.Active)]
    [InlineData(ClientStatus.Removed)]
    public void SameStatusNoOpTransition_IsRejected(ClientStatus status)
    {
        var client = MakeClient();
        if (status == ClientStatus.Removed)
        {
            client.TransitionTo(ClientStatus.Removed);
        }

        var act = () => client.TransitionTo(status);

        act.Should().Throw<InvalidClientStatusTransitionException>()
            .Where(ex => ex.CurrentStatus == status && ex.TargetStatus == status);
        client.Status.Should().Be(status);
    }
}
