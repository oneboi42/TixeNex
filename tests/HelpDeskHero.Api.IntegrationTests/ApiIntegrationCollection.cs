namespace HelpDeskHero.Api.IntegrationTests;

[CollectionDefinition("ApiIntegration", DisableParallelization = true)]
public sealed class ApiIntegrationCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}
