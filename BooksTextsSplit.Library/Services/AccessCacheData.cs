using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Models;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Logging;
using Shared.Library.Services;

namespace BooksTextsSplit.Library.Services
{
    public interface IAccessCacheData
    {
        public Task<T> GetObjectAsync<T>(string key);
        public Task<T> GetObjectAsync<T>(string key, string field);
        public Task InsertUser<T>(T user, string userId);
        public Task<T> FetchObjectAsync<T>(string redisKey, string fieldKey, Func<Task<T>> func, TimeSpan? expiry = null); // FetchHashedAsync
        public Task<T> FetchObjectAsync<T>(string key, Func<Task<T>> func, TimeSpan? expiry = null);
        public Task<T> FetchBookTable<T>(string key, int field);
        public Task<IDictionary<string, T>> FetchHashedAllAsync<T>(string key);
        public Task<IDictionary<TK, TV>> FetchHashedAllAsync<TK, TV>(string key);
        public Task SetObjectAsync<T>(string key, T value, TimeSpan? ttl = null);
        public Task SetObjectAsync<T>(string redisKey, string fieldKey, T value, TimeSpan? ttl = null); // SetHashedAsync
        public Task WriteHashedAsync<T>(string key, string field, T value, double ttl);
        public Task WriteHashedAsync<TK, TV>(string key, TK field, TV value, double ttl);
        public Task WriteHashedAsync<TK, TV>(string key, IEnumerable<KeyValuePair<TK, TV>> fieldValues, double ttl);
        public Task<bool> SetObjectAsyncCheck<T>(string key, T value, TimeSpan? ttl = null);
        public Task<bool> RemoveAsync(string key);
        public Task<bool> KeyExistsAsync(string key);
        public Task<bool> KeyExistsAsync<T>(string key, string field);
        public Task<bool> KeyExpireAsync(string key, DateTime expiration);
        public Task<bool> RemoveWorkKeyOnStart(string key);
    }

    public class AccessCacheData : IAccessCacheData
    {
        private readonly ISettingConstantsService _constant;
        private readonly ICacheProviderAsync _cache;
        private readonly ICosmosDbService _context;

        public AccessCacheData(
            ISettingConstantsService constant,
            ICosmosDbService cosmosDbService,
            ICacheProviderAsync cache)
        {
            _constant = constant;
            _cache = cache;
            _context = cosmosDbService;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<AccessCacheData>();

        public async Task<T> GetObjectAsync<T>(string key)
        {
            var cacheValue = await _cache.GetObjectAsync<T>(key);
            if (cacheValue != null)
            {
                return cacheValue;
            }
            return default;
        }
        
        public async Task<T> GetObjectAsync<T>(string key, string field)
        {
            var cacheValue = await _cache.GetHashedAsync<T>(key, field);
            if (cacheValue != null)
            {
                return cacheValue;
            }
            return default;
        }

        public async Task InsertUser<T>(T user, string userId) // for GetTest() only
        {
            var redisKey = "users:added";
            var fieldKey = $"user:id:{userId}";
            await _cache.SetHashedAsync<T>(redisKey, fieldKey, user);
        }

        public async Task<T> FetchObjectAsync<T>(string redisKey, string fieldKey, Func<Task<T>> func, TimeSpan? expiry = null)
        {
            return await _cache.FetchHashedAsync<T>(redisKey, fieldKey, func, expiry);            
        }

        public async Task<T> FetchObjectAsync<T>(string key, Func<Task<T>> func, TimeSpan? expiry = null)
        {
            //var cacheValue = await _cache.GetObjectAsync<T>(key);
            if (await _cache.KeyExistsAsync(key))
            {
                return await _cache.GetObjectAsync<T>(key);
            }
            else
            {
                T value = default;
                var task = func.Invoke();
                if (task != null)
                {
                    value = await task;
                    if (value != null)
                    {
                        await _cache.SetObjectAsync(key, value, expiry);
                    }
                }
                return value;
            }
        }


        public async Task<T> FetchBookTable<T>(string key, int field)
        {
            bool isKeyExist = await _cache.KeyExistsAsync(key);
            if(isKeyExist)
            {
                T value = await _cache.GetHashedAsync<int, T>(key, field);
                //Logs.Here().Error("{@K} removing was failed.", new { Key = key });
                if (value != null)
                {
                    return value;
                }
            }
            return default;
        }

        public async Task<IDictionary<string, T>> FetchHashedAllAsync<T>(string key)
        {
            return await _cache.GetHashedAllAsync<T>(key);
        }

        public async Task<IDictionary<TK, TV>> FetchHashedAllAsync<TK, TV>(string key)
        {// Task<IDictionary<TK, TV>> GetHashedAllAsync<TK, TV>(string key, CommandFlags flags = CommandFlags.None);
            return await _cache.GetHashedAllAsync<TK, TV>(key);
        }

        public async Task SetObjectAsync<T>(string key, T value, TimeSpan? ttl = null)
        {
            await _cache.SetObjectAsync(key, value, ttl);
        }

        public async Task SetObjectAsync<T>(string redisKey, string fieldKey, T value, TimeSpan? ttl = null)
        {
            //ttl ??= TimeSpan.FromMinutes(_constant.GetPercentsKeysExistingTimeInMinutes);
            await _cache.SetHashedAsync<T>(redisKey, fieldKey, value, ttl);
        }

        public async Task<bool> SetObjectAsyncCheck<T>(string key, T value, TimeSpan? ttl = null)
        {
            await _cache.SetObjectAsync(key, value, ttl);
            return await _cache.KeyExistsAsync(key);
        }

        public async Task WriteHashedAsync<T>(string key, string field, T value, double ttl)
        {
            await _cache.SetHashedAsync<T>(key, field, value, TimeSpan.FromDays(ttl));
        }

        public async Task WriteHashedAsync<TK, TV>(string key, TK field, TV value, double ttl)
        {
            await _cache.SetHashedAsync<TK, TV>(key, field, value, TimeSpan.FromDays(ttl));

        }

        public async Task WriteHashedAsync<TK, TV>(string key, IEnumerable<KeyValuePair<TK, TV>> fieldValues, double ttl)
        {
            await _cache.SetHashedAsync<TK, TV>(key, fieldValues, TimeSpan.FromDays(ttl));
        }

        public async Task<bool> RemoveAsync(string key)
        {
            return await _cache.RemoveAsync(key);
        }

        public async Task<bool> KeyExistsAsync(string key)
        {
            return await _cache.KeyExistsAsync(key);
        }

        public async Task<bool> KeyExistsAsync<T>(string key, string field)
        {
            var cacheValue = await _cache.GetHashedAsync<T>(key, field);
            if (cacheValue != null)
            {
                return true;
            }
            return false;
        }        

        public async Task<bool> KeyExpireAsync(string key, DateTime expiration) //Set a timeout on key. After the timeout has expired, the key will automatically be deleted
        {
            return await _cache.KeyExpireAsync(key, expiration);
        }

        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            // can use Task RemoveAsync(string[] keys, CommandFlags flags = CommandFlags.None);
            bool result = await KeyExistsAsync(key);
            if (result)
            {
                result = await RemoveAsync(key);
                Logs.Here().Information("{@K} was removed with result {0}.", new { Key = key }, result);
                return result;
            }
            Logs.Here().Information("{@K} does not exist.", new { Key = key });
            return !result;
        }


        //public async Task<TimeSpan?> KeyTimeToLiveAsync(string key)
        //public async Task<bool> KeyPersistAsync(string key)
        //public async Task FlushAllAsync()
    }
}
