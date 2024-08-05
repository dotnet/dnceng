using System;
using Azure;
using ITableEntity = Azure.Data.Tables.ITableEntity;

namespace RolloutScorer.Models;

public class ScorecardEntity : ITableEntity
{
    public ScorecardEntity()
    {
    }

    public ScorecardEntity(DateTimeOffset date, string repo)
    {
        PartitionKey = date.ToString("yyyy-MM-dd");
        RowKey = repo;
    }
    
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int TotalScore { get; set; }

    public double TimeToRolloutSeconds { get; set; }

    public int CriticalIssues { get; set; }

    public int Hotfixes { get; set; }

    public int Rollbacks { get; set; }

    public double DowntimeSeconds { get; set; }

    public bool Failure { get; set; }

    public int TimeToRolloutScore { get; set; }

    public int CriticalIssuesScore { get; set; }

    public int HotfixScore { get; set; }

    public int RollbackScore { get; set; }

    public int DowntimeScore { get; set; }
}