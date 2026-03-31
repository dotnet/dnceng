// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

/// <summary>
///     Service Fabric stateless service host for <see cref="AzureDevOpsTimelineProcessor"/>.
///     This thin wrapper exists solely to implement <see cref="IServiceImplementation"/>
///     (which lives in a platform-specific assembly) so the processor logic can
///     be tested on any architecture.
/// </summary>
public sealed class AzureDevOpsTimeline : IServiceImplementation
{
    private readonly ILogger<AzureDevOpsTimeline> _logger;
    private readonly AzureDevOpsTimelineOptions _options;
    private readonly AzureDevOpsTimelineProcessor _processor;

    public AzureDevOpsTimeline(
        ILogger<AzureDevOpsTimeline> logger,
        IOptionsSnapshot<AzureDevOpsTimelineOptions> options,
        AzureDevOpsTimelineProcessor processor)
    {
        _logger = logger;
        _options = options.Value;
        _processor = processor;
    }

    public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.Parse(_options.InitialDelay), cancellationToken);
            await _processor.RunLoop(cancellationToken);
        }
        catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "AzureDevOpsTimeline failed with unhandled exception");
        }

        return TimeSpan.Parse(_options.Interval);
    }
}
