namespace InvoiceTracker.IntegrationTests.Infrastructure;

/// <summary>
/// One Testcontainers Postgres instance (and one <see cref="IntegrationTestWebApplicationFactory"/>)
/// for the entire integration test assembly — starting/migrating a fresh container per test class
/// would be correct but needlessly slow. Every test class in this collection shares the fixture;
/// xUnit's default behavior keeps test classes within the same collection from running in parallel
/// with each other (collections themselves may still run in parallel with other collections, but
/// this is the only collection in this assembly), which avoids any cross-test contention on the
/// shared container. Isolation between tests is achieved by each test using its own uniquely-named
/// Business/user (see <see cref="IntegrationTestBase"/>), not by resetting the database.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestWebApplicationFactory>
{
    public const string Name = "Integration";
}
