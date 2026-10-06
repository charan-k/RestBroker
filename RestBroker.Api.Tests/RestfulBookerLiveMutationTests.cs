using NUnit.Framework;

namespace RestBroker.Api.Tests;

[TestFixture]
[Explicit("Live mutations remain disabled until the approved recovery-channel adapter and serialized workflow are provided.")]
[Category("LiveApiMutation")]
public sealed class RestfulBookerLiveMutationTests
{
    [Test]
    public void LiveCrudMutationScenario_IsFailClosedUntilRecoveryReporterIsConfigured()
    {
        Assert.Throws<InvalidOperationException>(
            () => LiveMutationOptions.Load(Environment.GetEnvironmentVariable, orphanRecoveryReporterAvailable: false));
    }
}
