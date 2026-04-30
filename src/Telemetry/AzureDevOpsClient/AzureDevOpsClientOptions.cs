// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
namespace Microsoft.DotNet.Internal.AzureDevOps;

public class AzureDevOpsClientOptions
{
    public string Organization { get; set; }
    public int MaxParallelRequests { get; set; } = 4;
    public string AccessToken { get; set; }

    /// <summary>
    /// When <c>true</c>, the client authenticates to Azure DevOps using a Managed Identity
    /// instead of a PAT. For system-assigned identities, leave <see cref="ManagedIdentityClientId"/>
    /// empty. For user-assigned identities, also set <see cref="ManagedIdentityClientId"/>.
    /// Ignored when <see cref="AccessToken"/> is provided.
    /// </summary>
    public bool UseManagedIdentity { get; set; }

    /// <summary>
    /// The client ID of a user-assigned Managed Identity. Only required when
    /// <see cref="UseManagedIdentity"/> is <c>true</c> and the identity is user-assigned.
    /// For system-assigned identities this should be left empty.
    /// </summary>
    public string ManagedIdentityClientId { get; set; }
}
