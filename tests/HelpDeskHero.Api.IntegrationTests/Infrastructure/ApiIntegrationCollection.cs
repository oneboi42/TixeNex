namespace HelpDeskHero.Api.IntegrationTests.Infrastructure;

[CollectionDefinition("ApiIntegration", DisableParallelization = true)]
public sealed class ApiIntegrationCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}
