﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace DotNet.Status.Web.Options;

public class BuildMonitorOptions
{
    public AzurePipelinesOptions Monitor { get; set; }
    public IssuesOptions[] Issues { get; set; }

    public class AzurePipelinesOptions
    {
        public string Organization { get; set; }
        public BuildDescription[] Builds { get; set; }

        public class BuildDescription
        {
            public string Project { get; set; }
            public string DefinitionPath { get; set; }
            public string[] Branches { get; set; }
            public string Assignee { get; set; }
            public string[] Labels { get; set; }
            public string[] Tags { get; set; }
            public string IssuesId { get; set; }
        }
    }

    public class IssuesOptions
    {
        public string Id { get; set; }
        public string Owner { get; set; }
        public string Name { get; set; }
        public string[] Labels { get; set; }
        public bool UpdateExisting { get; set; }
    }
}
