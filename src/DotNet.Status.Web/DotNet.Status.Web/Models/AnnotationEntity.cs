// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Azure;
using Azure.Data.Tables;

namespace DotNet.Status.Web.Models;

public class AnnotationEntity : ITableEntity
{
    [IgnoreDataMember]
    public string Service
    {
        get => PartitionKey;
        set => PartitionKey = value;
    }

    [IgnoreDataMember]
    public string Id
    {
        get => RowKey;
        set => RowKey = value;
    }

    public int GrafanaAnnotationId { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Ended { get; set; }

    public AnnotationEntity() { }

    public AnnotationEntity(string service, string id)
    {
        PartitionKey = service;
        RowKey = id;
    }

    public AnnotationEntity(string service, string id, int grafanaId) : this(service, id)
    {
        GrafanaAnnotationId = grafanaId;
        Started = DateTimeOffset.UtcNow;
    }

    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}