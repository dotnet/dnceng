using System;
using Azure;
using Azure.Data.Tables;

namespace RolloutScorer.Models;

public class AnnotationEntity : ITableEntity
{
    public AnnotationEntity()
    {
    }

    public AnnotationEntity(string service, string id)
    {
        PartitionKey = service;
        RowKey = id;
    }

    public AnnotationEntity(string service, string id, int grafanaId)
    {
        PartitionKey = service;
        RowKey = id;
        GrafanaAnnotationId = grafanaId;
        Started = DateTimeOffset.UtcNow;
    }

    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int GrafanaAnnotationId { get; set; }

    public DateTimeOffset? Started { get; set; }

    public DateTimeOffset? Ended { get; set; }
}