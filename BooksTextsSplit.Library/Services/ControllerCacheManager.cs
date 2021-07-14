﻿using BooksTextsSplit.Library.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Helpers;
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
        private readonly ILogger<ControllerDataManager> _logger;
        private readonly ISettingConstants _constant;
        private readonly IControllerDbManager _db;
        private readonly IAccessCacheData _access;

        public ControllerCacheManager(
            ILogger<ControllerDataManager> logger,
            ISettingConstants constant,
            IControllerDbManager db,
            IAccessCacheData access)
        {
            _logger = logger;
            _constant = constant;
            _db = db;
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
            int level = _constant.GetRecordActualityLevel; // Constants.RecordActualityLevel;

            string keyBookId = _constant.GetKeyBookId; // bookId
            string keyBookIdNum = _constant.GetKeyAllNumbers; // all
            string redisKey = keyBookId.KeyBaseRedisKey(keyBookIdNum); // bookId:all
            string keyLanguageId = _constant.GetKeyLanguageId; // languageId
            string keyLanguageIdNum = _constant.GetKeyAllNumbers; // all
            string fieldKey = keyLanguageId.KeyBaseRedisKey(keyLanguageIdNum); // languageId:all

            List<BookPropertiesExistInDb> foundBooksIds = await _access.FetchObjectAsync<List<BookPropertiesExistInDb>>(redisKey, fieldKey, () => _db.FetchBooksNamesVersionsPropertiesFromDb(level));

            return foundBooksIds;
        }

        public async Task<List<T>> FetchBookIdLanguageIdFromCache<T>()
        {
            int level = _constant.GetRecordActualityLevel;
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
            TimeSpan keysExistingTime = TimeSpan.FromMinutes(_constant.GetPercentsKeysExistingTimeInMinutes) * keysExistingTimeFactor;
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

        public async Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid)
        {
            // хранить все хэши плоских текстов в специальном ключе
            // с полем номер/язык книги (со сдвигом другого языка)
            // это хорошо согласуется с идеей хранить логи загрузки плоских текстов
            // можно типа значения оставить TextSentence, добавить поле хэша, а сам плоский текст удалять для экономии места
            // версия будет присваиваться где-то дальше, после разбора на главы и предложения или прямо здесь?
            // старую версию оставить как есть - для совместимости
            // новую версию, через хэш, присваивать где-то в тестах - до создания события subscribeOnFrom
            // 

            // достать нужные префиксы, ключи и поля из констант
            //string bookPlainText_KeyPrefixGuid = $"{constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value}:test"; //TEMP FOR TEST
            string bookPlainText_KeyPrefixGuid = constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value;
            //double keyExistingTime = 1000; // constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime; //TEMP FOR TEST
            double keyExistingTime = constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime;

            // создать ключ/поле из префикса и гуид книги
            string bookPlainText_FieldPrefixGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{bookGuid}";

            // здесь проверить тестовый ключ и ждать, если идет тест
            bool isTestInProgress = await IsTestInProgress(constantsSet);
            if(isTestInProgress)
            {
                // выполняется тест и не освободился, надо возвращать пользователю отлуп
                Logs.Here().Information("The waiting time of the end of the test was expired - {0}", isTestInProgress);

                return false;
            }

            // записать текст в ключ bookPlainTextKeyPrefix + this Server Guid и поле bookTextFieldPrefix + BookGuid
            // перенести весь _access в Shared.Library.Services CacheManageService
            // ключ для хранения плоского текста книги из префикса BookTextFieldPrefix и bookTextSplit server Guid
            // поле представляет собой префикс bookText:bookGuid: + bookGuid и хранит в значении плоский текст книги с полным описанием 
            await _access.WriteHashedAsync<TextSentence>(bookPlainText_KeyPrefixGuid, bookPlainText_FieldPrefixGuid, bookPlainTextWithDescription, keyExistingTime);
            Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V} \n", new { Key = bookPlainText_KeyPrefixGuid }, new { Field = bookPlainText_FieldPrefixGuid }, new { ValueOfBookId = bookPlainTextWithDescription.BookId });



            // а как передать BookGuid бэк-серверу?
            // 1 никак, будет искать по всем полям
            // 2 через ключ оповещения подписки, поле сделать номером по синхронному счётчику, а в значении это самое поле книги
            // тогда меньше операций с ключами на стороне бэк-сервера - не надо каждый раз вытаскивать все поля (со значениями, между прочим), а сразу взять нужное
            // но как тогда синхронизировать счётчик?

            // записываем то же самое поле в ключ subscribeOnFrom, а в значение (везде одинаковое) - ключ всех исходников книг
            // на стороне диспетчера всё достать словарём и найти новое (если приедет много сразу из нескольких клиентов)
            // уже обработанное поле сразу удалить, чтобы не накапливались
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            await _access.WriteHashedAsync<string>(eventKeyFrom, bookPlainText_FieldPrefixGuid, bookPlainText_KeyPrefixGuid, keyExistingTime);
            Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V} \n", new { Key = eventKeyFrom }, new { Field = bookPlainText_FieldPrefixGuid }, new { Value = bookPlainText_KeyPrefixGuid });
            
            // типа справились (тут можно подождать какой-то реакции диспетчера - скажем, когда исчезнет ключ subscribeOnFrom
            return true;
        }

        private async Task<bool> IsTestInProgress(ConstantsSet constantsSet)
        {
            // здесь проверить тестовый ключ и, если выполняется тест, ждать
            // можно ждать стандартные 5 сек (или свое время) и, если не освободилось, возвращать пользователю отлуп
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            int delayTimeForTest1 = constantsSet.IntegerConstant.IntegrationTestConstant.DelayTimeForTest1.Value; // 1000
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value; // 5000
            int totalTimeOfTestEndWaiting = (int)(timerIntervalInMilliseconds * 2.001) / delayTimeForTest1;
            int currentTimeToWaitEndOfTest = 0;

            bool isTestInProgress = true;
            // крутимся в цикле, пока существует ключ запуска теста
            while (isTestInProgress)
            {
                isTestInProgress = await _access.KeyExistsAsync(eventKeyTest);
                Logs.Here().Information("Test {@K} is existed - {0}", new { Key = eventKeyTest }, isTestInProgress);

                if (!isTestInProgress)
                {
                    // если ключа теста нет, возвращаем, что теста нет - без ожидания
                    Logs.Here().Information("Test {@K} is not found - {0}", new { Key = eventKeyTest }, !isTestInProgress);
                    return false;
                }

                await Task.Delay(delayTimeForTest1);
                currentTimeToWaitEndOfTest++;
                Logs.Here().Information("Test {@K} is existed - {0}, it is waited {1} msec for {2} times", new { Key = eventKeyTest }, isTestInProgress, delayTimeForTest1, currentTimeToWaitEndOfTest);

                if (currentTimeToWaitEndOfTest > totalTimeOfTestEndWaiting)
                {
                    // время ожидания вышло, тест почему-то не закончился - всё плохо, больше не ждём
                    Logs.Here().Information("The waiting time {0} was expired {1}, the test {@K} for some reason is {2} yet", currentTimeToWaitEndOfTest, totalTimeOfTestEndWaiting, new { Key = eventKeyTest }, isTestInProgress);

                    return true;
                }
            }
            // вообще всё пропало
            return true;
        }
    }
}
