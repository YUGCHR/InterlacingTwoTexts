using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Models;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

#region TestRawBookTextsStorageService description




#endregion

namespace BackgroundDispatcher.Services
{
    public interface ITestRawBookTextsStorageService
    {
        public Task<(List<int>, List<string>)> CreateTestBookIdsListFromStorageKey(ConstantsSet constantsSet, string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test");


    }

    public class TestRawBookTextsStorageService : ITestRawBookTextsStorageService
    {
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public TestRawBookTextsStorageService(
            IAuxiliaryUtilsService aux, 
            ICacheManagerService cache)
        {
            _aux = aux;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestRawBookTextsStorageService>();


        // сюда можно передать ключ временного хранилища тестовых плоских текстов, если он будет как-то вычисляться,
        // а не генерироваться контроллером с guid BooTextSplit-а - bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test
        public async Task<(List<int>, List<string>)> CreateTestBookIdsListFromStorageKey(ConstantsSet constantsSet, string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test")
        {
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000

            List<int> uniqueBookIdsFromStorageKey = new();
            List<string> guidFieldsFromStorageKey = new();

            bool storageKeyBookPlainTextsIsExisted = await _cache.IsKeyExist(storageKeyBookPlainTexts);
            if (!storageKeyBookPlainTextsIsExisted)
            {
                _aux.SomethingWentWrong(storageKeyBookPlainTextsIsExisted);
                return (null, null);
            }

            IDictionary<string, TextSentence> fieldsFromStorageKeyBookPlainTexts = await _cache.FetchHashedAllAsync<TextSentence>(storageKeyBookPlainTexts);

            foreach (KeyValuePair<string, TextSentence> s in fieldsFromStorageKeyBookPlainTexts)
            {
                (string f, TextSentence v) = s;

                guidFieldsFromStorageKey.Add(f);
                // пишем в уникальный список только базовые номера книг
                int b = v.BookId;// + v.LanguageId * chapterFieldsShiftFactor;

                //if (uniqueBookIdsFromStorageKeyCount == 0 || (uniqueBookIdsFromStorageKeyCount > 0 && !uniqueBookIdsFromStorageKey.Contains(b)))
                // и надо проверить, что в тестовом наборе у каждой английской книги есть пара - русская
                // самое простое - можно сначала писать уникальные номера обоих языков, а потом проверить парность и номера русских книг выкинуть
                // потом добавить, а пока считаем, что в тестовых книгах всегда пары

                if (!uniqueBookIdsFromStorageKey.Contains(b))
                {
                    uniqueBookIdsFromStorageKey.Add(b);
                    Logs.Here().Information("Unique BookId {0} from StorageKey was added to List.", b);
                }
            }
            return (uniqueBookIdsFromStorageKey, guidFieldsFromStorageKey);
        }






















    }
}