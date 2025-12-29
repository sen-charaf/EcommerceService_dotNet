using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecommerce.Services
{
    /// <summary>
    /// Service for caching and tracking product views using Redis
    /// </summary>
    public class RedisCacheService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly JsonSerializerOptions _jsonOptions;

        public RedisCacheService(IConnectionMultiplexer redis)
        {
            _redis = redis;
            _db = redis.GetDatabase();

            // Configure JSON serialization to handle circular references
            _jsonOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        #region Product View Tracking

        /// <summary>
        /// Increment view count for a product
        /// </summary>
        public async Task IncrementProductViewAsync(int productId)
        {
            string key = $"product:views:{productId}";
            await _db.StringIncrementAsync(key);

            // Also add to sorted set for most viewed products
            await _db.SortedSetIncrementAsync("products:most_viewed", productId, 1);
        }

        /// <summary>
        /// Get view count for a specific product
        /// </summary>
        public async Task<long> GetProductViewCountAsync(int productId)
        {
            string key = $"product:views:{productId}";
            var value = await _db.StringGetAsync(key);
            return value.HasValue ? (long)value : 0;
        }

        /// <summary>
        /// Get most viewed products (top N)
        /// </summary>
        public async Task<List<(int ProductId, long ViewCount)>> GetMostViewedProductsAsync(int count = 10)
        {
            var products = await _db.SortedSetRangeByRankAsync(
                "products:most_viewed",
                start: 0,
                stop: count - 1,
                order: Order.Descending
            );

            var result = new List<(int ProductId, long ViewCount)>();

            foreach (var product in products)
            {
                var productId = (int)product;
                var score = await _db.SortedSetScoreAsync("products:most_viewed", productId);
                result.Add((productId, (long)(score ?? 0)));
            }

            return result;
        }

        #endregion

        #region Generic Caching

        /// <summary>
        /// Get cached object by key
        /// </summary>
        public async Task<T?> GetAsync<T>(string key)
        {
            var value = await _db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
                return default;

            try
            {
                // Use explicit string conversion to avoid ambiguity
                string jsonString = value.ToString();
                return JsonSerializer.Deserialize<T>(jsonString, _jsonOptions);
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// Set cached object with expiration
        /// </summary>
        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);

            if (expiration.HasValue)
            {
                await _db.StringSetAsync(key, json, expiration.Value);
            }
            else
            {
                await _db.StringSetAsync(key, json);
            }
        }

        /// <summary>
        /// Remove cached item
        /// </summary>
        public async Task RemoveAsync(string key)
        {
            await _db.KeyDeleteAsync(key);
        }

        /// <summary>
        /// Check if key exists
        /// </summary>
        public async Task<bool> ExistsAsync(string key)
        {
            return await _db.KeyExistsAsync(key);
        }

        #endregion

        #region Product Caching

        /// <summary>
        /// Cache product data
        /// </summary>
        public async Task CacheProductAsync(int productId, object product, TimeSpan? expiration = null)
        {
            string key = $"product:data:{productId}";
            expiration ??= TimeSpan.FromHours(1); // Default 1 hour cache
            await SetAsync(key, product, expiration);
        }

        /// <summary>
        /// Get cached product
        /// </summary>
        public async Task<T?> GetCachedProductAsync<T>(int productId)
        {
            string key = $"product:data:{productId}";
            return await GetAsync<T>(key);
        }

        /// <summary>
        /// Clear product cache
        /// </summary>
        public async Task ClearProductCacheAsync(int productId)
        {
            string key = $"product:data:{productId}";
            await RemoveAsync(key);
        }

        /// <summary>
        /// Cache product list (e.g., category products)
        /// </summary>
        public async Task CacheProductListAsync(string cacheKey, object products, TimeSpan? expiration = null)
        {
            expiration ??= TimeSpan.FromMinutes(30); // Default 30 minutes
            await SetAsync(cacheKey, products, expiration);
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public async Task<Dictionary<string, string>> GetCacheStatsAsync()
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var info = await server.InfoAsync();

            var stats = new Dictionary<string, string>();
            foreach (var section in info)
            {
                foreach (var item in section)
                {
                    stats[item.Key] = item.Value;
                }
            }

            return stats;
        }

        #endregion
    }
}
