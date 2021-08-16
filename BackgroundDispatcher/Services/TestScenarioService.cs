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

#region TestScenarioService description




#endregion

namespace BackgroundDispatcher.Services
{
    public interface ITestScenarioService
    {
        public Task<(List<string>, List<int>)> CreateTestScenarioLists(ConstantsSet constantsSet, List<int> uniqueBookIdsFromStorageKey);
    }

    public class TestScenarioService : ITestScenarioService
    {
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public TestScenarioService(
            IAuxiliaryUtilsService aux,
            ICacheManagerService cache)
        {
            _aux = aux;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestScenarioService>();
        // метод из ключа описания сценария создаёт
        // список string rawPlainTextFields гуид-полей сырых текстов
        // и задержек между ними (List<int> delayList)
        // и это синхронные списки (используется значение из того, где оно не пустое/нулевое)
        public async Task<(List<string>, List<int>)> CreateTestScenarioLists(ConstantsSet constantsSet, List<int> uniqueBookIdsFromStorageKey)
        {
            string testScenarioSequenceKey = constantsSet.Prefix.IntegrationTestPrefix.TestScenarioSequenceKey.Value; // test-scenario-sequence
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
            // ключ, в котором хранятся все хэши - keyBookPlainTextsHashesVersionsList - key-book-plain-texts-hashes-versions-list
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list

            int uniqueBookIdsFromStorageKeyCount = uniqueBookIdsFromStorageKey.Count;

            // выходной список для запуска выбранного тестового сценария - задержки
            List<int> delayList = new();
            // поля сырых плоских текстов
            List<string> rawPlainTextFields = new();
            string emptyMeansDelay = "";

            // можно выходные данные сделать а виде словаря - ключ это гуид-поле, а значение - это int задержка после этого поля
            // вариант - меньше нуля (задержка), выравнивание хромает - задержка получается без ключа, надо смотреть на момент применения
            // можно выходные данные сделать а виде словаря - ключ это гуид-поле, а значение - это инт задержка после этого поля
            // проблема в том, что задержка занимает полный цикл, в котором нет гуид-полей, а задержку, получается,
            // надо записать в предыдущий элемент словаря - предварительно проверив, что он существует
            // длина словаря совсем не соответствует длине массива/списка исходного задания - потому что обычно в шаге задания пары книг
            // словарь вообще не подходит, он не гарантирует сохранение порядка
            //IDictionary<string, int> plainTextFieldsWithDelayAfter = new Dictionary<string, int>();

            IDictionary<int, int> fieldValuesResult = await _cache.FetchHashedAllAsync<int, int>(testScenarioSequenceKey);
            int fieldValuesResultCount = fieldValuesResult.Count;

            for (int i = 0; i < fieldValuesResultCount; i++)
            {
                int sequenceCell = fieldValuesResult[i];
                Logs.Here().Information("Scenario cell value {0} was fetched on stage {1}", sequenceCell, i);

                // а когда в цикле (во входном списке) будет задержка (sequenceCell < 0) вместо книг,
                // записать задержку в целый список и для синхронизации записать null (или лучше пустую строку) в строчный список
                if (sequenceCell < 0)
                {
                    delayList.Add(Math.Abs(sequenceCell));
                    rawPlainTextFields.Add(emptyMeansDelay);
                }
                // тогда на выходе они будут одинаковой длины, пошагово будет считываться строчный список
                // а когда встретится пустой элемент, сходим в целый список с таким же индексом и возьмём величину задержки
                // и остаётся достать значение из поля в ключе временного хранилища (это будет сырой плоский текст)
                // записать его в ключ задач контроллера с таким же полем и создать ключ события
                // интересно, как совпадут версии из будущего
                // надо же ещё не забыть прогнать временное хранилище через метод, создающий поля вечного лога
                // и ещё потом удалить все лишние ключи

                // меньше десяти (автоматический выбор)
                // когда-нибудь потом сделаем (может быть)

                if (sequenceCell > 100)
                {
                    // больше ста, варианты -
                    // сотни - номер книги по порядку от случайного отсчёта - разделить на сто, остаток разделить на 10
                    int hundreds = sequenceCell / 100;
                    int hundredsRemainder = sequenceCell % 100;

                    // десятки - варианты - 0, 1 и 2 - языки (2 - оба)
                    int tens = hundredsRemainder / 10;

                    // единицы - номер версии по порядку с начала(или с конца, без разницы)
                    int tensRemainder = hundredsRemainder % 10;
                    // лучше с конца, пока не упрётся в пустышку
                    Logs.Here().Information("Scenario Cell consists of {@B}, {@L}, {@V}", new { SequentialBookId = hundreds }, new { LanguageIdSelection = tens }, new { SequentialVersion = tensRemainder });

                    // смотрим первый элемент/значение, если пара, берём обе книги первой версии,
                    // первого номера (или случайного, как обозначить?), соответственно,
                    // если одна, то берём одну из, вторую вычёркиваем, не будем вообще использовать
                    // смотрим следующий индекс - берём или следующую книгу или следующую версию этой же
                    // будем считать, что количество сотен, это номер книги по порядку из списка (если столько в списке нет, нормируем или ?)

                    bool outOfBookIdsIndex = hundreds > uniqueBookIdsFromStorageKeyCount;
                    if (outOfBookIdsIndex)
                    {
                        _aux.SomethingWentWrong(outOfBookIdsIndex);
                    }

                    // 2 - это пара книг на двух языках, остальные варианты пока не рассматриваем, а то вообще никогда не написать
                    if (tens == 2)
                    {
                        // номера сотен идут с единицы, а индексы списка с нуля
                        int engBookId = uniqueBookIdsFromStorageKey[hundreds - 1];
                        int rusBookId = engBookId + chapterFieldsShiftFactor;

                        // достать оба поля из вечного лога
                        List<TextSentence> engPlainTextHash = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, engBookId);
                        List<TextSentence> rusPlainTextHash = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, rusBookId);

                        // вот тут можно сразу удалить эти поля из вечного лога - наверное
                        // а, нет, нельзя, к ним могут ещё обратиться - пока версии не закончатся
                        // тогда надо удалять поля в самом конце метода
                        // причём удалять все тестовые - прямо по списку uniqueBookIdsFromStorageKey + производные со сдвигом

                        // теперь версия - они тоже нумеруются с единицы, но нулевой элемент - пустышка, поэтому единицу не вычитаем 
                        int engPlainTextHashCount = engPlainTextHash.Count;
                        bool outOfTextHashIndex = tensRemainder > engPlainTextHashCount;
                        if (outOfTextHashIndex)
                        {
                            _aux.SomethingWentWrong(outOfTextHashIndex);
                        }

                        // можно сделать цикл на два прохода и уменьшить количество переменных, правда список станет двумерным
                        TextSentence engPlainTextHashVersion = engPlainTextHash[tensRemainder];
                        TextSentence rusPlainTextHashVersion = rusPlainTextHash[tensRemainder];

                        // теперь надо достать из текст значение полей из которых они родом - и это конечный результат,
                        // можно идти за текстами в тестовое хранилище

                        // создать ключ/поле из префикса и гуид книги
                        string engBookFieldGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{engPlainTextHashVersion.BookGuid}";
                        string rusBookFieldGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{rusPlainTextHashVersion.BookGuid}";

                        Logs.Here().Information("English book field-guid = {0}, rus = {1}", engBookFieldGuid, rusBookFieldGuid);

                        // придётся вернуться к идее с двумя синхронизированными списками - строчными и целым
                        // можно записывать в целый список нули каждый раз, когда пишется поле-гуид
                        rawPlainTextFields.Add(engBookFieldGuid);
                        delayList.Add(0);
                        rawPlainTextFields.Add(rusBookFieldGuid);
                        delayList.Add(0);
                    }
                }
            }

            // для понимания всей картины можно сделать логи в отдельном методе - сравнить построчный вывод с выводом серилога

            // списка полей во временном хранилище - где взять?
            // списка номеров книг оттуда
            Logs.Here().Information("List {@U}", new { UniqueBookIdsFromStorageKey = uniqueBookIdsFromStorageKey });
            //ShowListInLog(uniqueBookIdsFromStorageKey, "uniqueBookIdsFromStorageKey");

            // списка тестовых полей в вечном логе, и вывести списки версий в каждом значении
            for (int i = 0; i < uniqueBookIdsFromStorageKeyCount; i++)
            {
                // ---------- выделить в метод ----------
                int engBookId = uniqueBookIdsFromStorageKey[i];
                int rusBookId = engBookId + chapterFieldsShiftFactor;

                // достать оба поля из вечного лога
                List<TextSentence> engPlainTextHash = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, engBookId);
                //Logs.Here().Information("In {@K} / {@F} - List<TextSentence> {@L}", new { Key = keyBookPlainTextsHashesVersionsList }, new { Field = engBookId }, new { EngPlainTextHash = engPlainTextHash });

                List<TextSentence> rusPlainTextHash = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, rusBookId);
                //Logs.Here().Information("In {@K} / {@F} - List<TextSentence> {@L}", new { Key = keyBookPlainTextsHashesVersionsList }, new { Field = rusBookId }, new { RusPlainTextHash = rusPlainTextHash });

                int engPlainTextHashCount = engPlainTextHash.Count;
                int rusPlainTextHashCount = rusPlainTextHash.Count;

                if (engPlainTextHashCount == rusPlainTextHashCount)
                {
                    for (int j = 0; j < engPlainTextHashCount; j++)
                    {
                        int bookIdEng = engPlainTextHash[j].BookId;
                        int bookIdRus = rusPlainTextHash[j].BookId;
                        int verHashEng = engPlainTextHash[j].HashVersion;
                        int verHashRus = rusPlainTextHash[j].HashVersion;

                        string hashEng = engPlainTextHash[j].BookPlainTextHash;
                        string hashRus = rusPlainTextHash[j].BookPlainTextHash;
                        string bookGuidEng = engPlainTextHash[j].BookGuid;
                        string bookGuidRus = rusPlainTextHash[j].BookGuid;

                        Logs.Here().Information("Eng - BookId {0}, HashVer {1}, Hash {2}, Guid {3}", bookIdEng, verHashEng, hashEng, bookGuidEng);
                        Logs.Here().Information("Rus - BookId {0}, HashVer {1}, Hash {2}, Guid {3}", bookIdRus, verHashRus, hashRus, bookGuidRus);

                    }
                }
                else
                {
                    _aux.SomethingWentWrong(true);
                }
            }

            // списка пунктов сценария
            //ShowDictionaryInLog<int>(fieldValuesResult, "fieldValuesResult", "Step", "Action");
            Logs.Here().Information("Dictionary {@F}", new { FieldValuesResult = fieldValuesResult });

            // выходных списков полей и задержек (параллельно)
            //ShowListsInLog(rawPlainTextFields, "rawPlainTextFields", delayList, "delayList");
            Logs.Here().Information("List {@R} - List {@D}", new { RawPlainTextFields = rawPlainTextFields }, new { DelayList = delayList });

            return (rawPlainTextFields, delayList);
        }
    }
}
