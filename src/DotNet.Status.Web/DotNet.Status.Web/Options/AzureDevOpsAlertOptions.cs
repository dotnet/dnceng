// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace DotNet.Status.Web.Options;

public class AzureDevOpsAlertOptions
{
    public string Organization { get; set; }
    public string Project { get; set; }
    public string AreaPath { get; set; }
    public string WorkItemType { get; set; } = "DNCENG Task";
    public string[] Tags { get; set; }
    public string TitlePrefix { get; set; }
    public string SupplementalBodyText { get; set; }
}
