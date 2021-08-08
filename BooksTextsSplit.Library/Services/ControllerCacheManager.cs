using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Helpers;
using BooksTextsSplit.Library.Models;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BooksTextsSplit.Library.Services
{
    public interface IControllerCacheManager
    {
        public Task<int[]> FetchAllBooksIds(string keyBooksIds, int languageId, string propName, int actualityLevel);
        public Task<int[]> FetchAllBooksIds(string keyBooksIds, int languageId, string propName, int actualityLevel, int currentBooksIds);
        public Task<List<BookPropertiesExistInDb>> FetchAllBookIdsLanguageIdsFromCache();
        public Task<TaskUploadPercents> FetchTaskState(TaskUploadPercents taskStateCurrent);
        public Task<bool> SetTaskGuidKeys(TaskUploadPercents uploadPercents, int keysExistingTimeFactor);
        public Task<BookTable> CheckBookId(string bookTablesKey, int uploadVersion);
        public Task AddHashValue<T>(string Key, int id, T context);
        public Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid);
    }

    public class ControllerCacheManager : IControllerCacheManager
    {
        //private readonly ISettingConstantsService _constant;
        private readonly IControllerDbManager _db;
        private readonly IRawBookTextAddAndNotifyService _add;
        private readonly IAccessCacheData _access;

        public ControllerCacheManager(
            //ISettingConstantsService constant,
            IControllerDbManager db,
            IRawBookTextAddAndNotifyService add,
            IAccessCacheData access)
        {
            //_constant = constant;
            _db = db;
            _add = add;
            _access = access;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<ControllerCacheManager>();

        public async Task<int[]> FetchAllBooksIds(string keyBooksIds, int languageId, string propName, int actualityLevel)
        {
            int[] allBooksIds = await _access.FetchObjectAsync<int[]>(keyBooksIds, () => _db.FetchItemsArrayFromDb(languageId, propName, actualityLevel));

            return allBooksIds;
        }

        public async Task<int[]> FetchAllBooksIds(string keyBooksIds, int languageId, string propName, int actualityLevel, int currentBooksIds)
        {
            int[] allBooksIds = await _access.FetchObjectAsync<int[]>(keyBooksIds, () => _db.FetchItemsArrayFromDb(languageId, propName, actualityLevel, currentBooksIds));

            return allBooksIds;
        }

        public async Task<List<BookPropertiesExistInDb>> FetchAllBookIdsLanguageIdsFromCache()
        {
            // определиться, откуда взять recordActualityLevel (from Constant or from UI - and UI will receive from Constant)
            int level = 6; // _constants.GetRecordActualityLevel; // Constants.RecordActualityLevel;

            string keyBookId = "bookId"; // bookId
            string keyBookIdNum = "all"; // all
            string redisKey = keyBookId.KeyBaseRedisKey(keyBookIdNum); // bookId:all
            string keyLanguageId = "languageId"; // languageId
            string keyLanguageIdNum = "all"; // all
            string fieldKey = keyLanguageId.KeyBaseRedisKey(keyLanguageIdNum); // languageId:all

            List<BookPropertiesExistInDb> foundBooksIds = await _access.FetchObjectAsync<List<BookPropertiesExistInDb>>(redisKey, fieldKey, () => _db.FetchBooksNamesVersionsPropertiesFromDb(level));

            return foundBooksIds;
        }

        public async Task<List<T>> FetchBookIdLanguageIdFromCache<T>()
        {
            int level = 6; // _constant.GetRecordActualityLevel;
            List<BookPropertiesExistInDb> foundBooksIds = await _db.FetchBooksNamesVersionsPropertiesFromDb(level);

            for (int i = 0; i < foundBooksIds.Count; i++)
            {
                string keyBookId = "bookId";
                string keyBookIdNum = foundBooksIds[i].BookId.ToString();
                var redisKey = $"{keyBookId}:{keyBookIdNum}";

                for (int j = 0; j < 2; j++)
                {
                    var lang = foundBooksIds[i].BookVersionsLanguageInBook;

                    string keyLanguageId = "languageId";
                    string keyLanguageIdNum = lang[j].LanguageId.ToString();
                    var fieldKey = $"{keyLanguageId}:{keyLanguageIdNum}";



                    //var booksVersionsProperties = await _access.FetchObjectAsync<List<T>>(redisKey, fieldKey, () => ());
                }
            }
            return default;
        }

        public async Task<TaskUploadPercents> FetchTaskState(TaskUploadPercents taskStateCurrent)
        {
            return await _access.GetObjectAsync<TaskUploadPercents>(taskStateCurrent.RedisKey, taskStateCurrent.FieldKeyPercents);
        }

        public async Task<bool> SetTaskGuidKeys(TaskUploadPercents uploadPercents, int keysExistingTimeFactor)
        {
            TimeSpan keysExistingTime = TimeSpan.FromMinutes(5) * keysExistingTimeFactor; //_constant.GetPercentsKeysExistingTimeInMinutes
            await _access.SetObjectAsync(uploadPercents.RedisKey, uploadPercents.FieldKeyPercents, uploadPercents, keysExistingTime);
            return true;
        }

        public async Task<BookTable> CheckBookId(string bookTableKey, int uploadVersion)
        {
            BookTable bookTable = await _access.FetchBookTable<BookTable>(bookTableKey, uploadVersion);

            return bookTable;
        }

        public async Task AddHashValue<T>(string Key, int id, T context)
        {
            double chaptersExistingTime = 0.01; // время хранения книг в кэше, взять из констант

            await _access.WriteHashedAsync<int, T>(Key, id, context, chaptersExistingTime);
        }

        // временно оставлено для совместимости
        public async Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid)
        {            
            return await _add.AddPainBookText(constantsSet, bookPlainTextWithDescription, bookGuid);
        }
    }
}
