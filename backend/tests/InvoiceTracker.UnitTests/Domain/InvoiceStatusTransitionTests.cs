using FluentAssertions;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Domain;

/// <summary>
/// Direct, exhaustive unit tests for <see cref="Invoice.TransitionTo"/> itself — the single place
/// the status workflow's legal transitions are now centralized (see architecture.md's "Domain
/// invariants: centralized status transitions" and its test plan's "Status transitions" bullet:
/// "every valid transition succeeds ..., every invalid one is rejected (e.g. Paid→Cancelled)").
///
/// The individual command handler test suites (SendInvoiceCommandHandlerTests,
/// MarkInvoicePaidCommandHandlerTests, CancelInvoiceCommandHandlerTests,
/// OverdueInvoiceScannerServiceTests) already exercise this method through their own call sites,
/// but none of them ever calls <c>TransitionTo(Draft)</c> or <c>TransitionTo(Overdue)</c> from a
/// starting status other than the one their own production code path uses — so several illegal
/// (status, target) pairs in the switch inside <see cref="Invoice.TransitionTo"/> (e.g.
/// Draft→Overdue, Paid→Draft, Cancelled→Overdue) are never reached by any existing test. This
/// fills that gap by testing the domain method directly and exhaustively, covering every
/// (status, target) pair in the 5x5 matrix exactly once.
/// </summary>
public class InvoiceStatusTransitionTests
{
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    private static Invoice MakeInvoiceWithStatus(InvoiceStatus status) =>
        InvoiceSeeding.MakeInvoice(BusinessId, ClientId, status);

    public static TheoryData<InvoiceStatus, InvoiceStatus> LegalTransitions() => new()
    {
        { InvoiceStatus.Draft, InvoiceStatus.Unpaid },
        { InvoiceStatus.Unpaid, InvoiceStatus.Overdue },
        { InvoiceStatus.Unpaid, InvoiceStatus.Paid },
        { InvoiceStatus.Overdue, InvoiceStatus.Paid },
        { InvoiceStatus.Draft, InvoiceStatus.Cancelled },
        { InvoiceStatus.Unpaid, InvoiceStatus.Cancelled },
    };

    public static TheoryData<InvoiceStatus, InvoiceStatus> IllegalTransitions()
    {
        var allStatuses = Enum.GetValues<InvoiceStatus>();
        var legal = new HashSet<(InvoiceStatus, InvoiceStatus)>
        {
            (InvoiceStatus.Draft, InvoiceStatus.Unpaid),
            (InvoiceStatus.Unpaid, InvoiceStatus.Overdue),
            (InvoiceStatus.Unpaid, InvoiceStatus.Paid),
            (InvoiceStatus.Overdue, InvoiceStatus.Paid),
            (InvoiceStatus.Draft, InvoiceStatus.Cancelled),
            (InvoiceStatus.Unpaid, InvoiceStatus.Cancelled),
        };

        var data = new TheoryData<InvoiceStatus, InvoiceStatus>();
        foreach (var from in allStatuses)
        {
            foreach (var to in allStatuses)
            {
                if (!legal.Contains((from, to)))
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void LegalTransition_Succeeds_AndUpdatesStatus(InvoiceStatus from, InvoiceStatus to)
    {
        var invoice = MakeInvoiceWithStatus(from);

        invoice.TransitionTo(to);

        invoice.Status.Should().Be(to);
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void IllegalTransition_ThrowsInvalidInvoiceStatusTransitionException_AndLeavesStatusUnchanged(
        InvoiceStatus from, InvoiceStatus to)
    {
        var invoice = MakeInvoiceWithStatus(from);

        var act = () => invoice.TransitionTo(to);

        act.Should().Throw<InvalidInvoiceStatusTransitionException>()
            .Where(ex => ex.CurrentStatus == from && ex.TargetStatus == to);

        invoice.Status.Should().Be(from);
    }

    [Fact]
    public void EveryStatus_HasASameStatusNoOpRejected()
    {
        foreach (var status in Enum.GetValues<InvoiceStatus>())
        {
            var invoice = MakeInvoiceWithStatus(status);

            var act = () => invoice.TransitionTo(status);

            act.Should().Throw<InvalidInvoiceStatusTransitionException>();
            invoice.Status.Should().Be(status);
        }
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Unpaid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void PaidIsTerminal_NoTransitionOutOfPaidIsEverLegal(InvoiceStatus target)
    {
        var invoice = MakeInvoiceWithStatus(InvoiceStatus.Paid);

        var act = () => invoice.TransitionTo(target);

        act.Should().Throw<InvalidInvoiceStatusTransitionException>();
        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Unpaid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Paid)]
    public void CancelledIsTerminal_NoTransitionOutOfCancelledIsEverLegal(InvoiceStatus target)
    {
        var invoice = MakeInvoiceWithStatus(InvoiceStatus.Cancelled);

        var act = () => invoice.TransitionTo(target);

        act.Should().Throw<InvalidInvoiceStatusTransitionException>();
        invoice.Status.Should().Be(InvoiceStatus.Cancelled);
    }
}
