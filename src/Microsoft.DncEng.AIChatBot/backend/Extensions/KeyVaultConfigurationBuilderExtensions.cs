namespace MinimalApi.Extensions;

internal static class KeyVaultConfigurationBuilderExtensions
{
    internal static ConfigurationManager ConfigureAzureKeyVault(this ConfigurationManager builder)
    {
        var azureKeyVaultEndpoint = builder["AzureKeyVaultEndpoint"];
        ArgumentNullException.ThrowIfNullOrEmpty(azureKeyVaultEndpoint);

        builder.AddAzureKeyVault(
            new Uri(azureKeyVaultEndpoint), new DefaultAzureCredential());

        return builder;
    }
}
