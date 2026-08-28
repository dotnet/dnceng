// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Microsoft.DncEng.GrafanaWatchdog.Models;

/// <summary>
/// The subset of the Grafana "/api/org" response body that the watchdog validates. A successful,
/// well-formed response here proves the bearer token was accepted and authenticated as an org member,
/// which a bare HTTP 200 (e.g. from a captive portal or misconfigured proxy) would not prove.
/// </summary>
internal sealed class GrafanaOrgResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
