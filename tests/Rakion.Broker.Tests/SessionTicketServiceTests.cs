using Rakion.Broker.Services;

namespace Rakion.Broker.Tests;

[TestClass]
public sealed class SessionTicketServiceTests
{
    [TestMethod]
    public void IssueTicket_ValidInput_CanBeRetrievedByUsernameAndClientIp()
    {
        var service = new SessionTicketService();
        uint ticket = service.IssueTicket("admin", "127.0.0.1");

        Assert.AreNotEqual(0u, ticket);

        bool foundByUser = service.TryGetTicketByUsername("admin", out var userTicket);
        Assert.IsTrue(foundByUser);
        Assert.AreEqual(ticket, userTicket.Ticket);
        Assert.AreEqual("admin", userTicket.Username);
        Assert.AreEqual("127.0.0.1", userTicket.ClientIp);

        bool foundByIp = service.TryGetTicketByClientIp("127.0.0.1", out var ipTicket);
        Assert.IsTrue(foundByIp);
        Assert.AreEqual("admin", ipTicket.Username);
        Assert.AreEqual(ticket, ipTicket.Ticket);
    }

    [TestMethod]
    public void TryGetTicketByClientIp_LoopbackVariants_MatchSuccessfully()
    {
        var service = new SessionTicketService();
        uint ticket = service.IssueTicket("tester", "127.0.0.1");

        bool foundIpv6Loopback = service.TryGetTicketByClientIp("::1", out var info);
        Assert.IsTrue(foundIpv6Loopback);
        Assert.AreEqual("tester", info.Username);
        Assert.AreEqual(ticket, info.Ticket);
    }
}

