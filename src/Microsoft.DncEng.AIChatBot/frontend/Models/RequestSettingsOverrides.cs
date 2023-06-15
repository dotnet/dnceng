// Copyright (c) Microsoft. All rights reserved.

using Shared.Models;

namespace ClientApp.Models;

public record RequestSettingsOverrides
{
    public Approach Approach { get; set; }
    public RequestOverrides Overrides { get; set; } = new();
}
