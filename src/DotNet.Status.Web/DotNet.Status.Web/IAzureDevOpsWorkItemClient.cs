// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DotNet.Status.Web;

public interface IAzureDevOpsWorkItemClient
{
    Task<int> CreateWorkItemAsync(
        string project,
        string workItemType,
        Dictionary<string, object> fields,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, string>> GetWorkItemFieldsAsync(
        int workItemId,
        CancellationToken cancellationToken = default);

    Task UpdateWorkItemFieldsAsync(
        int workItemId,
        Dictionary<string, object> fields,
        CancellationToken cancellationToken = default);

    Task AddCommentAsync(
        string project,
        int workItemId,
        string text,
        CancellationToken cancellationToken = default);

    Task<int[]> QueryWorkItemsByWiqlAsync(
        string project,
        string wiql,
        CancellationToken cancellationToken = default);
}
