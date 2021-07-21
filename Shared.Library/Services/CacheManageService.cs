using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts.Providers;
using Shared.Library.Models;

namespace Shared.Library.Services
{
    public interface ICacheManageService
    {
        public Task SetStartConstants(KeyType startConstantKey, string startConstantField, ConstantsSet constantsSet);
        public Task SetConstantsStartGuidKey(KeyType startConstantKey, string startConstantField, string constantsStartGuidKey);
        public Task<TV> FetchUpdatedConstant<TK, TV>(string key, TK field);
        public Task<IDictionary<TK, TV>> FetchUpdatedConstantsAndDeleteKey<TK, TV>(string key);
        public Task<bool> DeleteKeyIfCancelled(string startConstantKey);
        public Task<bool> IsKeyExist(string key);
        public Task<bool> DelKeyAsync(string key);
        public Task<bool> DelFieldAsync(string key, string field);
        public Task<bool> DelFieldAsync<TK>(string key, TK field);
        public Task<int> DelFieldAsync<TK>(string key, List<TK> fields);
        public Task<T> FetchHashedAsync<T>(string key, string field);
        public Task<TV> FetchHashedAsync<TK, TV>(string key, TK field);
        public Task WriteHashedAsync<T>(string key, string field, T value, double ttl);
        public Task WriteHashedAsync<TK, TV>(string key, TK field, TV value, double ttl);
        public Task WriteHashedAsync<TK, TV>(string key, IEnumerable<KeyValuePair<TK, TV>> fieldValues, double ttl);
        public Task<IDictionary<string, T>> FetchHashedAllAsync<T>(string key);
        public Task<IDictionary<TK, TV>> FetchHashedAllAsync<TK, TV>(string key);
    }

    public class CacheManageService : ICacheManageService
    {
        private readonly ICacheProviderAsync _cache;

        public CacheManageService(ICacheProviderAsync cache)
        {
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<CacheManageService>();

        public async Task SetStartConstants(KeyType keyTime, string field, ConstantsSet constantsSet)
        {
            if (field == constantsSet.ConstantsVersionBaseField.Value)
            {
                // обновлять версию констант при записи в ключ гуид
                constantsSet.ConstantsVersionNumber.Value++;
                Logs.Here().Information("ConstantsVersionNumber was incremented and become {0}.", constantsSet.ConstantsVersionNumber.Value);
            }
            await _cache.SetHashedAsync<ConstantsSet>(keyTime.Value, field, constantsSet, SetLifeTimeFromKey(keyTime));
            Logs.Here().Debug("SetStartConstants set constants (EventKeyFrom for example = {0}) in key {1}.", constantsSet.EventKeyFrom.Value, keyTime.Value);
        }

        public async Task SetConstantsStartGuidKey(KeyType keyTime, string field, string constantsStartGuidKey)
        {
            await _cache.SetHashedAsync<string>(keyTime.Value, field, constantsStartGuidKey, SetLifeTimeFromKey(keyTime));
            string test = await _cache.GetHashedAsync<string>(keyTime.Value, field);
            Logs.Here().Debug("SetStartConstants set {@G} \n {0} --- {1}.", new { GuidKey = keyTime.Value }, constantsStartGuidKey, test);
            // можно читать и сравнивать - и возвращать true
        }

        private static TimeSpan SetLifeTimeFromKey(KeyType time)
        {
            return TimeSpan.FromDays(time.LifeTime);
        }

        public async Task<TV> FetchUpdatedConstant<TK, TV>(string key, TK field)
        {
            return await _cache.GetHashedAsync<TK, TV>(key, field);
        }

        public async Task<IDictionary<TK, TV>> FetchUpdatedConstantsAndDeleteKey<TK, TV>(string key)
        {
            // Gets all the values from a hash, assuming all the values in the hash are of the same type <typeparamref name="TV" />.
            // The keys of the dictionary are the field names of type <typeparamref name="TK" /> and the values are the objects of type <typeparamref name="TV" />.
            // <typeparam name="TK">The field type</typeparam>
            // <typeparam name="TV">The value type</typeparam>

            IDictionary<TK, TV> updatedConstants = await _cache.GetHashedAllAsync<TK, TV>(key);
            bool result = await _cache.RemoveAsync(key);
            if (result)
            {
                return updatedConstants;
            }
            Logs.Here().Error("{@K} removing was failed.", new { Key = key });
            return null;
        }

        public async Task<bool> DeleteKeyIfCancelled(string startConstantKey)
        {
            return await _cache.RemoveAsync(startConstantKey);
        }

        public async Task<bool> IsKeyExist(string key)
        {
            return await _cache.KeyExistsAsync(key);
        }

        public async Task<bool> DelKeyAsync(string key)
        {
            return await _cache.RemoveAsync(key);
        }

        public async Task<bool> DelFieldAsync(string key, string field)
        {
            return await _cache.RemoveHashedAsync(key, field);
        }

        public async Task<bool> DelFieldAsync<TK>(string key, TK field)
        {
            return await _cache.RemoveHashedAsync<TK>(key, field);
        }

        public async Task<int> DelFieldAsync<TK>(string key, List<TK> fields)
        {
            int count = 0;
            if (fields != null)
            {
                foreach (var f in fields)
                {
                    bool result = await _cache.RemoveHashedAsync<TK>(key, f);
                    if (!result)
                    {
                        return -1;
                    }
                    count++;
                }
            }
            return count;
        }

        public async Task<T> FetchHashedAsync<T>(string key, string field)
        {
            return await _cache.GetHashedAsync<T>(key, field);
        }

        // Task<TV> GetHashedAsync<TK, TV>(string key, TK field, CommandFlags flags = CommandFlags.None);
        public async Task<TV> FetchHashedAsync<TK, TV>(string key, TK field)
        {
            return await _cache.GetHashedAsync<TK, TV>(key, field);
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

        public async Task<IDictionary<string, T>> FetchHashedAllAsync<T>(string key)
        {
            return await _cache.GetHashedAllAsync<T>(key);
        }

        public async Task<IDictionary<TK, TV>> FetchHashedAllAsync<TK, TV>(string key)
        {// Task<IDictionary<TK, TV>> GetHashedAllAsync<TK, TV>(string key, CommandFlags flags = CommandFlags.None);
            return await _cache.GetHashedAllAsync<TK, TV>(key);
        }
    }
}
