// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.Identity;
using Azure.Data.Tables;
using Azure;
using System.Runtime.Serialization;

namespace DotNet.Status.Web;

public class AzureTableTokenStore : ITokenStore, ITokenRevocationProvider
{
    private readonly IHostEnvironment _env;
    private readonly IOptionsMonitor<AzureTableTokenStoreOptions> _options;
    private readonly ILogger<AzureTableTokenStore> _logger;

    public AzureTableTokenStore(
        IHostEnvironment env,
        IOptionsMonitor<AzureTableTokenStoreOptions> options,
        ILogger<AzureTableTokenStore> logger)
    {
        _env = env;
        _options = options;
        _logger = logger;
    }

    private async Task<TableClient> GetCloudTable()
    {
        TableClient table;
        AzureTableTokenStoreOptions options = _options.CurrentValue;
        if (_env.IsDevelopment())
        {
            table = new TableClient("UseDevelopmentStorage=true", options.TableName);
            await table.CreateIfNotExistsAsync();
        }
        else
        {
            table = new TableClient(new Uri(options.TableUri, UriKind.Absolute), options.TableName, new DefaultAzureCredential());
        }
        return table;
    }

    public async Task<bool> IsTokenRevokedAsync(long userId, long tokenId)
    {
        var token = await GetTokenAsync(userId, tokenId);
        return token.RevocationStatus != RevocationStatus.Active;
    }

    private Task UpdateAsync(long userId, long tokenId, Action<TokenEntity> update)
    {
        return UpdateAsync(userId.ToString(), tokenId.ToString(), update);
    }

    private async Task UpdateAsync<T>(string partitionKey, string rowKey, Action<T> update) where T : class, ITableEntity
    {
        TableClient table = await GetCloudTable();
        while (true)
        {
            var fetchResult = await table.GetEntityIfExistsAsync<T>(partitionKey, rowKey);
            if (!fetchResult.HasValue)
                throw new KeyNotFoundException();

            var entity = (T) fetchResult.Value;
            update(entity);

            var updateResult = await table.UpdateEntityAsync(entity, entity.ETag, mode: TableUpdateMode.Replace);
            if (updateResult.Status == (int) HttpStatusCode.PreconditionFailed)
            {
                _logger.LogInformation("Concurrent update failed, re-fetching and updating...");
                continue;
            }
            return;
        }
    }

    public async Task RevokeTokenAsync(long userId, long tokenId)
    {
        try
        {
            await UpdateAsync(userId, tokenId, t => t.RevocationStatus = RevocationStatus.Revoked);
        }
        catch (KeyNotFoundException)
        {
            // it doesn't exist, we'll call that revoked
        }
    }

    public async Task<StoredTokenData> IssueTokenAsync(long userId, DateTimeOffset expiration, string description)
    {
        TableClient table = await GetCloudTable();

        long MakeTokenId()
        {
            Span<byte> bits = stackalloc byte[sizeof(long)];
            RandomNumberGenerator.Fill(bits);
            return BitConverter.ToInt64(bits);
        }

        while (true)
        {
            try
            {
                var entity = new TokenEntity(
                    userId,
                    MakeTokenId(),
                    DateTimeOffset.UtcNow,
                    expiration,
                    description,
                    RevocationStatus.Active
                );

                await table.AddEntityAsync(entity);

                return Return(entity);
            }
            catch (RequestFailedException e) when (e.Status == (int) HttpStatusCode.Conflict)
            {
                _logger.LogInformation("Duplicate token insertion attempted, generating new ID and retrying...");
            }
        }
    }

    public async Task<StoredTokenData> GetTokenAsync(long userId, long tokenId)
    {
        TableClient table = await GetCloudTable();
        var result = await table.GetEntityIfExistsAsync<TokenEntity>(userId.ToString(), tokenId.ToString());
        if (!result.HasValue)
        {
            return null;
        }

        return Return(result.Value);
    }

    public async Task<IEnumerable<StoredTokenData>> GetTokensForUserAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        TableClient table = await GetCloudTable();
        var results = table.QueryAsync<TokenEntity>(entity => entity.PartitionKey == userId.ToString());

        return await results.Select(t => Return(t)).ToListAsync();
    }

    private StoredTokenData Return(TokenEntity entity)
    {
        return new StoredTokenData(entity.UserId,
            entity.TokenId,
            entity.Issued,
            entity.Expiration,
            entity.Description,
            entity.RevocationStatus);
    }

    private class TokenEntity : ITableEntity
    {
        public TokenEntity() { }

        public TokenEntity(long userId, long tokenId)
        {
            UserId = userId;
            TokenId = tokenId;
        }

        public TokenEntity(
            long userId,
            long tokenId,
            DateTimeOffset issued,
            DateTimeOffset expiration,
            string description,
            RevocationStatus revocationStatus) : this(userId, tokenId)
        {
            Issued = issued;
            Expiration = expiration;
            Description = description;
            RevocationStatus = revocationStatus;
        }

        [IgnoreDataMember]
        public long UserId
        {
            get => long.Parse(PartitionKey);
            set => PartitionKey = value.ToString();
        }

        [IgnoreDataMember]
        public long TokenId
        {
            get => long.Parse(RowKey);
            set => RowKey = value.ToString();
        }
        public DateTimeOffset Issued { get; set; }
        public DateTimeOffset Expiration { get; set; }
        public string Description { get; set; }

        [IgnoreDataMember]
        public RevocationStatus RevocationStatus
        {
            get => Enum.Parse<RevocationStatus>(RevocationString);
            set => RevocationString = value.ToString();
        }

        public string RevocationString { get; set; }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

public static class AzureTableTokenStoreExtension
{
    public static IServiceCollection AddAzureTableTokenStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureTableTokenStoreOptions>(configuration);
        services.AddSingleton<AzureTableTokenStore>();
        services.AddSingleton<ITokenRevocationProvider>(s => s.GetRequiredService<AzureTableTokenStore>());
        services.AddSingleton<ITokenStore>(s => s.GetRequiredService<AzureTableTokenStore>());
        return services;
    }
}
