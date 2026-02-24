// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace DotNet.Status.Web.Options;

public class GrafanaAlertOptions
{
    public string Organization { get; set; }
    public string Project { get; set; }
    public string AreaPath { get; set; }
    public string WorkItemType { get; set; } = "Bug";
    public string[] NotificationTargets { get; set; }
    public string[] AlertTags { get; set; }
    public string[] EnvironmentTags { get; set; }
    public string TitlePrefix { get; set; }
    public string SupplementalBodyText { get; set; }
    public string ManagedIdentityClientId { get; set; }
}
