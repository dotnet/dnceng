// Copyright (c) Microsoft. All rights reserved.

namespace ClientApp.Models;

public record class SharedCultures
{
    [JsonPropertyName("translation")]
    public IDictionary<string, AzureCulture> AvailableCultures { get; set; }

    public SharedCultures(IDictionary<string, AzureCulture> availableCultures)
    {
        AvailableCultures = availableCultures;
    }
}