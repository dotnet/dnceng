using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

public class DependencyInjectionTests
{
    [TestCase("azure-devops-access-token")]
    [TestCase("azure-devops-service-endpoint")]
    public void RetiredSecretTypesCannotBeResolved(string name)
    {
        var services = new ServiceCollection();
        new Program().ConfigureServiceCollection(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<SecretTypeRegistry>();
        registry.Invoking(r => r.Get(name)).Should().Throw<KeyNotFoundException>();
    }

    [Test]
    public void RetiredServiceConnectionStorageCannotBeResolved()
    {
        var services = new ServiceCollection();
        new Program().ConfigureServiceCollection(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<StorageLocationTypeRegistry>();
        registry.Invoking(r => r.Get("azure-devops-project")).Should().Throw<KeyNotFoundException>();
    }

    [Test]
    public void AreDependenciesCoherent()
    {
        bool dependenciesCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
            collection =>
            {
                var program = new Program();
                program.ConfigureServiceCollection(collection);
                collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            },
            out string errorMessage,
            typeof(Program).Assembly.GetTypes().Where(t => t.GetCustomAttribute<CommandAttribute>() != null).ToList()
        );

        dependenciesCoherent.Should().BeTrue(errorMessage);
    }
}
