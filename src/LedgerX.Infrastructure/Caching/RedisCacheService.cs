using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using LedgerX.Core.Interfaces;

namespace LedgerX.Infrastructure.Caching
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDatabase? _database;
        private readonly IConnectionMultiplexer? _connection;
        private readonly ILogger<RedisCacheService> _logger;

        public RedisCacheService(IConnectionMultiplexer? connection, ILogger<RedisCacheService> _logger)
        {
            this._logger = _logger;
            try
            {
                _connection = connection;
                _database = connection?.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Redis. Caching is disabled or falling back.");
            }
        }

        private bool IsConnected => _database != null && _connection != null && _connection.IsConnected;

        public async Task<T?> GetAsync<T>(string key)
        {
            if (!IsConnected)
            {
                _logger.LogWarning("Redis is not connected. Bypassing Get cache for key: {Key}", key);
                return default;
            }

            try
            {
                var value = await _database!.StringGetAsync(key);
                if (value.IsNullOrEmpty)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(value!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error during Get for key: {Key}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            if (!IsConnected)
            {
                _logger.LogWarning("Redis is not connected. Bypassing Set cache for key: {Key}", key);
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(value);
                await _database!.StringSetAsync(key, json, expiration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error during Set for key: {Key}", key);
            }
        }

        public async Task RemoveAsync(string key)
        {
            if (!IsConnected)
            {
                return;
            }

            try
            {
                await _database!.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error during Remove for key: {Key}", key);
            }
        }

        public async Task RemoveByPatternAsync(string pattern)
        {
            if (!IsConnected || _connection == null)
            {
                return;
            }

            try
            {
                var endpoints = _connection.GetEndPoints();
                foreach (var endpoint in endpoints)
                {
                    var server = _connection.GetServer(endpoint);
                    if (server.IsReplica) continue;

                    // Note: KEYS can block the Redis event loop on very large databases, but in local/small SaaS deployments
                    // it is acceptable. For enterprise production, SCAN is preferred. StackExchange.Redis Server.Keys uses SCAN under the hood.
                    var keys = server.Keys(pattern: pattern);
                    foreach (var key in keys)
                    {
                        await _database!.KeyDeleteAsync(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error during RemoveByPattern for pattern: {Pattern}", pattern);
            }
        }
    }
}
