// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace DotNet.Status.Web.Options;

// Storage coordinates for the deployment annotations table. The DeploymentController writes
// deployment start/end records here and the AnnotationsController reads them back to serve
// Grafana's annotation queries.
public class DeploymentTableOptions
{
    public string TableUri { get; set; }
    public string TableName { get; set; }
}
