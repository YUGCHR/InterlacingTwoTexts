using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Models;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BackgroundDispatcher.Services
{
    public interface ITestTasksPrepareAndStoreService
    {
        public Task<bool> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0);
        public bool SomethingWentWrong(bool result0, bool result1 = true, bool result2 = true, bool result3 = true, bool result4 = true, [CallerMemberName] string currentMethodName = "");
        public Task<bool> RemoveWorkKeyOnStart(string key);

    }

    public class TestTasksPrepareAndStoreService : ITestTasksPrepareAndStoreService
    {
        private readonly ICacheManageService _cache;

        public TestTasksPrepareAndStoreService(ICacheManageService cache)
        {
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestTasksPrepareAndStoreService>();

        //private bool _isTestInProgress;


        // метод создаёт тестовые плоские тексты для тестов
        // берет нужное количество книг/версий для сценария из хранилища
        // и из них делает ключи, неотличимые от приходящих из веб-интерфейса
        // если сценарий предусматривает, то по окончанию теста новые ключи с хэшами должны быть удалены из хранилища хэшей-версий

        // во время теста (при старте) -
        // проверить наличие ключа-хранилища тестовых задач
        // если ключа нет, то и тест надо прекратить (может, в зависимости от сценария)

        // проверить наличие ключа созданных описаний тестовых задач
        // если ключа нет, надо его создать -
        // считать всё из ключа-хранилища
        // прочитать у каждого поля номер книги и прочее
        // записать в ключ описаний листом или по полям, поля как в ключе хранения лога - номера книг и со смещением признак языка

        public async Task<bool> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0)
        {
            string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test";
            // hget bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test bookText:bookGuid:9902e124-5dc1-4ad7-9dce-428070a97d59

            //string field_ = "bookText:bookGuid:9902e124-5dc1-4ad7-9dce-428070a97d59"; // 71 - 1 - 17
            //string field_ = "bookText:bookGuid:ff67769e-ef73-41e4-a317-a313d641b252"; // 71 - 0 - 15
            //string field_ = "bookText:bookGuid:058799d2-04bb-4025-baaa-561612cf9ab3"; // 73 - 1 - 28
            //string field_ = "bookText:bookGuid:45d9b924-d9d3-47cb-8154-23c659a538be";
            //string field_ = "bookText:bookGuid:0e58ba37-8d5b-456e-820c-950b7b99b1eb";
            //string field_ = "bookText:bookGuid:3d21e753-94c2-4dcc-a8e2-d9003c60ab19";
            //string field_ = "bookText:bookGuid:7604fc0b-511f-467d-8fe4-83cb60cce49e";
            //string field_ = "bookText:bookGuid:b69189d3-4da5-437e-a6fe-71a18703c3f5";
            //string field_ = "bookText:bookGuid:c407d7d0-20ab-4c98-9cfa-102196a333bf";
            //string field_ = "bookText:bookGuid:5970f94c-99bd-4b01-b628-03c2acb90074";
            //string field_ = "bookText:bookGuid:84d94f49-9c6d-44aa-b2f3-ac40b610c823";
            //string field_ = "bookText:bookGuid:d74ceb93-7519-4b22-b165-bac0ef6c3536";
            //string field_ = "bookText:bookGuid:a0e5e2fb-e1a2-4d08-99d0-95d4a2096ba0";
            //string field_ = "bookText:bookGuid:0eaa8879-6bf0-4961-8b26-c6b6ecfcd0ee";
            //string field_ = "bookText:bookGuid:9eb0544b-2f7e-4e89-a847-8cd619c08e28";
            //string field_ = "bookText:bookGuid:2f24a7af-caa8-49db-907a-32efbedce26e";
            //string field_ = "bookText:bookGuid:b1f9b81e-ffde-4244-bec8-8bdfca9fa647";
            //string field_ = "bookText:bookGuid:7b9c873d-83c1-4e94-bc9f-28a3a96aab93";
            //string field_ = "bookText:bookGuid:e3589686-eef2-49e6-94a2-e799af2fab03";
            //string field_ = "bookText:bookGuid:6a8c2d5a-552e-4833-9385-31f83f906416";
            //string field_ = "bookText:bookGuid:dade4abd-9bf4-4fc7-8db4-6f012e8b21f1";
            //string field_ = "bookText:bookGuid:71cb7a2e-a71d-43ff-968f-bcfe1fe1d0ec";
            //string field_ = "bookText:bookGuid:e031e392-9065-4ba0-84fa-1c48b3143820";
            //string field_ = "bookText:bookGuid:34c7ac9d-3934-4bd7-a554-08bc6d72587c";
            //string field_ = "bookText:bookGuid:6b79d96e-5bb7-4e86-a2eb-0311ae69bb64";
            //string field_ = "bookText:bookGuid:c4a0d1bd-a525-4095-9f23-b0a24d166422";
            //string field_ = "bookText:bookGuid:9488cd35-3e11-4675-a4c5-918fc25b25a8";
            //string field_ = "bookText:bookGuid:74e9173b-b9c5-46fd-8d6c-085923d4a523";
            //string field_ = "bookText:bookGuid:a797dc1c-42eb-4b9d-920b-63b789349f03";
            //string field_ = "bookText:bookGuid:fd18b5fd-6d32-4e31-9630-c0446ba09831";


            string testKeyBookPlainTextsPrefix = constantsSet.BookPlainTextConstant.KeyPrefix.Value; // bookPlainTexts:bookSplitGuid
            string testKeyBookPlainTextsGuid = "5a272735-4be3-45a3-91fc-152f5654e451";
            string testKeyBookPlainTexts = $"{testKeyBookPlainTextsPrefix}:{testKeyBookPlainTextsGuid}"; // bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451
                                                                                                         //"bookPlainTexts:bookSplitGuid:f0c17236-3d50-4bce-9843-15fc9ee79bbd";
                                                                                                         //string[] fields = new string[4] { field_79_ENG, field_79_RUS, field_78_ENG, field_78_RUS };

            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list
            double keyBookPlainTextsHashesVersionsListLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.LifeTime; // 1000
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string testKeyBookPlainTextsHashesVersionsList = $"{keyBookPlainTextsHashesVersionsList}:{eventKeyTest}";

            bool storageKeyBookPlainTextsIsExisted = await _cache.IsKeyExist(storageKeyBookPlainTexts);
            if (!storageKeyBookPlainTextsIsExisted)
            {
                SomethingWentWrong(storageKeyBookPlainTextsIsExisted);
                return false;
            }

            bool testKeyBookPlainTextsHashesVersionsListIsExisted = await _cache.IsKeyExist(testKeyBookPlainTextsHashesVersionsList);
            if (!testKeyBookPlainTextsHashesVersionsListIsExisted)
            {
                IDictionary<string, TextSentence> fieldsFromStorageKeyBookPlainTexts = await _cache.FetchHashedAllAsync<TextSentence>(storageKeyBookPlainTexts);
                //IDictionary<string, TextSentence> fieldsFromHashesVersionsList = await _cache.FetchHashedAllAsync<TextSentence>(testKeyBookPlainTextsHashesVersionsList);
                int fieldsFromStorageKeyBookPlainTextsCount = fieldsFromStorageKeyBookPlainTexts.Count;
                //int fieldsFromHashesVersionsListCount = fieldsFromHashesVersionsList.Count;
                string theFirstStringElement = "00000";
                int theFirstIntElement = 1000;
                List<int> bufferGuidFieldsList = OperationWithListFirstElement<int>(theFirstIntElement);
                //HashesVersionsList

                foreach (var s in fieldsFromStorageKeyBookPlainTexts)
                {
                    (var f, var v) = s;
                    Logs.Here().Information("Dictionary element in Storage Key Book Plain Texts is {@F} {@V}.", new { Filed = f }, new { Value = v });

                    int bookId = v.BookId;
                    int languageId = v.LanguageId;
                    int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
                    int fieldBookIdWithLanguageId = bookId + languageId * chapterFieldsShiftFactor;

                    // List<T>.Contains(T) Method
                    // если список полей с номерами книг уже содержит такую книгу -
                    // достаём из этого поля список гуид полей текстов,
                    // добавляем в список новое гуид поле и кладём обратно
                    bool result = bufferGuidFieldsList.Contains(fieldBookIdWithLanguageId);
                    Logs.Here().Information("Fields (bookId) List contains {0} - is {1}.", fieldBookIdWithLanguageId, result);

                    if (result)
                    {
                        List<string> tempList = await _cache.FetchHashedAsync<int, List<string>>(testKeyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId);
                        tempList.Add(f);
                        await _cache.WriteHashedAsync<int, List<string>>(testKeyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, tempList, keyBookPlainTextsHashesVersionsListLifeTime);
                        Logs.Here().Information("Fields (bookId) List contains {0} - is {1}.", fieldBookIdWithLanguageId, result);
                    }
                    // если не содержит - создаём новый список (с первым полем пустышкой), далее так же
                    // добавляем в список новое гуид поле и кладём обратно
                    // и добавляем в буферный список новое значение поля - номера книги
                    else
                    {
                        List<string> bufferFieldsList = OperationWithListFirstElement<string>(theFirstStringElement);
                        bufferFieldsList.Add(f);
                        await _cache.WriteHashedAsync<int, List<string>>(testKeyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, bufferFieldsList, keyBookPlainTextsHashesVersionsListLifeTime);
                        //Logs.Here().Information("Test plain text was write in {@K} / {@F}", new { Key = testKeyBookPlainTexts }, new { Field = fields[i] });
                        bufferGuidFieldsList.Add(fieldBookIdWithLanguageId);
                    }

                    // 



                }

            }



            // Value Type is TextSentence

            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
                                                                   // пока время тестовых ключей задаётся отдельно (может, меньше, чем настоящие) - хотя реальной разницы нет
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime

            // проверить ключи плоского текста и тестового оповещения и, если нужно, удалить их
            // пока что удаляем при старте
            bool resultPlainText = await RemoveWorkKeyOnStart(testKeyBookPlainTexts);
            bool resultFromTest = await RemoveWorkKeyOnStart(eventKeyFrom);

            Logs.Here().Information("Test plain text keys creation is started");

            if (resultPlainText && resultFromTest)
            {
                // 1 считать ключ-хранилище тестовых плоских текстов
                // 2 создать лист модели из ключа, поля и значения плоского текста и к нему сигнального
                // (значение теста в лист не писать, доставать из кэша в момент записи нового ключа)

                // выделить for в отдельный метод и уменьшить слоистость?


                //for (int i = 0; i < testPairsCount * 2; i++)
                //{
                //    // прочитать первое поле хранилища
                //    TextSentence bookPlainText = await _cache.FetchHashedAsync<TextSentence>(storageKeyBookPlainTexts, fields[i]);
                //    Logs.Here().Information("Test plain text was read from key-storage");

                //    // создать тестовый ключ плоского текста
                //    resultPlainText = await WriteHashedAsyncWithDelayAfter<TextSentence>(testKeyBookPlainTexts, fields[i], bookPlainText, eventKeyFromTestLifeTime, stoppingToken, delayAfter);
                //    Logs.Here().Information("Test plain text was write in {@K} / {@F}", new { Key = testKeyBookPlainTexts }, new { Field = fields[i] });

                //    // создать тестовый ключ оповещения 
                //    resultFromTest = await WriteHashedAsyncWithDelayAfter<string>(eventKeyFrom, fields[i], testKeyBookPlainTexts, eventKeyFromTestLifeTime, stoppingToken, delayAfter);
                //    Logs.Here().Information("Test subscribeOnFrom was write in {@K} / {@F} / {@V}", new { Key = eventKeyFrom }, new { Field = fields[i] }, new { Value = testKeyBookPlainTexts });

                //    if (SomethingWentWrong(resultPlainText, resultFromTest))
                //    {
                //        return false;
                //    }
                //}
                //Logs.Here().Information("Test pair(s) was created in quantity {0}.", testPairsCount);
                return true;
            }
            return !SomethingWentWrong(resultPlainText, resultFromTest);
        }

        private List<T> OperationWithListFirstElement<T>(T theFirstElement)
        {
            List<T> fieldsFromStorageKey = new();
            fieldsFromStorageKey.Add(theFirstElement);
            return fieldsFromStorageKey;
        }

        private bool IsValueAlreadyInList<T>(List<T> testeeList, T testeeValue) where T : class
        {
            foreach (T v in testeeList)
            {
                if (v == testeeValue)
                {

                }
            }

            return false;
        }

        // можно сделать перегрузку с массивом на вход
        public bool SomethingWentWrong(bool result0, bool result1 = true, bool result2 = true, bool result3 = true, bool result4 = true, [CallerMemberName] string currentMethodName = "")
        { // return true if something went wrong!
            const int resultCount = 5;
            bool[] results = new bool[resultCount] { result0, result1, result2, result3, result4 };

            for (int i = 0; i < resultCount; i++)
            {
                if (!results[i])
                {
                    Logs.Here().Error("Situation in {0} where something went unexpectedly wrong is appeared - result No. {1} is {2}", currentMethodName, results[i], i);
                    return true;
                }
            }
            return false;
        }


        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            // can use Task RemoveAsync(string[] keys, CommandFlags flags = CommandFlags.None);
            bool result = await _cache.IsKeyExist(key);
            if (result)
            {
                result = await _cache.DeleteKeyIfCancelled(key);
                Logs.Here().Information("{@K} was removed with result {0}.", new { Key = key }, result);
                return result;
            }
            Logs.Here().Information("{@K} does not exist.", new { Key = key });
            return !result;
        }

    }
}
