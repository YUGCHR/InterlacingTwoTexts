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

#region TestTasksPreparationService description

// 0, 1 и 2 убираем, можно только 10-12, 20-22 и так далее, номеров тестовых книг вряд ли будет сильно больше десятка
// потом можно добавить, что 0 - отметка начала повтора, 1 - конец и потом идёт число повторов
// но тестовых книг не так много, чтобы так расходиться, оставим на (далёкое) будущее
// отрицательное число в ряду - задержка в миллисекундах, зная задержку таймера из констант,
// можно подбираться ближе к срабатыванию с разных сторон, добиваясь сдвоенного срабатывания (или нет)

// можно сразу в одном ряду проверять несколько задержек, скажем -
// 12, 22, -4700, 32, -1000,
// 12, 22, -4800, 32, -1000,
// 12, 22, -4900, 32, -1000

// будет означать, что взять пару книг с номером 73 (к примеру) первой версии, потом пару 75 и, после задержки 4,7 сек, пару 77, потом на всякий случай секунду (и ещё вариант без неё) и повторить с другой версией и другой задержкой
// как тогда сделать повтор
// можно перейти к трёхзначным числам и последняя цифра будет версией
// если версий/номеров не хватает, ряд (сценарий) заканчивается с оповещением, чего не хватило

// тогда такой вид -
// 121, 221, -4700, 321, -1000,
// 122, 222, -4800, 322, -1000,
// 123, 223, -4900, 323, -1000
// три пары книг по три разных версии каждой

// если в версии 0, это может означать взять следующую версию автоматически
// аналогично, с номерами книг -
// 2, 2, -4700, 2, -1000,
// 2, 2, -4800, 2, -1000,
// 2, 2, -4900, 2, -1000
// может означать, что каждый раз брать новую пару книг, пока не кончатся, потом брать следующие версии

// и после теста надо же проверить ключи кафе и понять, соответствуют ли они заявленным требованиям

// тогда параметр сценария может быть либо массивом (списком) целых чисел, либо ключом, в котором поле это индекс массива, а в значении значение
// ключ выглядит более заманчиво, особенно, когда его надо будет передать из веб-интерфейса

// порядок действий примерно такой -
// смотрим первый элемент/значение, если пара, берём обе книги первой версии,
// первого номера (или случайного, как обозначить?), соответственно,
// если одна, то берём одну из, вторую вычёркиваем, не будем вообще использовать
// смотрим следующий индекс - берём или следующую книгу или следующую версию этой же

// тогда создаём список гуид-полей следующим образом -
// берём номер книги (для определённости скажем случайный и книги парами, одиночные тоже понятно как),
// достаём оба списка версий из номерных полей пары,
// берём первую (с нулевым индексом - или лучше последнюю?) версию,
// достаём из списка гуид-поле текста книги (обоих книг) и складываем в выходной список по порядку

// второй такой же список, только int, делаем с задержками, потом когда будем считывать список книг для генерации событий,
// параллельно будем смотреть в список задержек с таким же индексом, 0 - нет задержки и так далее

// пустышки не удаляем ни в коем случае, ни сейчас, ни раньше - когда в отдельно взятом списке останется только пустышка, просто удалим это поле

// после того, как будут готовы выходные списки - один с гуид-полями, другой с задержками, надо удалить всё лишнее -

// 1 все тестовые поля из вечного лога, теперь не надо что-то оставлять,
// чтобы посмотреть на наложение (повтор книги) - это можно предусмотреть в сценарии

// 2 удалить образовавшийся пакет задач
// (хотя можно и оставить для сравнения, отредактировав его в соответствии с выходным списком - просто удалив ненужные поля)
// нет, возиться с пакетом задач нет смысла, просто удалить - пакет всё же лучше не удалять,
// а привести в нужное состояние и использовать для сравнения
// хотя там есть вычисленные хэши и версии хэшей, надо ещё подумать над его использованием

// кстати, метод, который за контроллером пишет ключ с книгой, а потом ключ с оповещением,
// надо вынести в общую библиотеку и потом его же использовать в тесте
// (там, наверное, стоит регулируемая задержка между записями или проверка записи книги) -
// чтобы тест точно совпадал с рабочим вариантом

// и надо при проверке результатов не пытаться состязаться с серверами в чтении ключа кафе,
// а тихо и спокойно получить ключ пакета задач до создания ключа кафе - метод, создающий ключ кафе перед его записью может проверить,
// не тест ли сейчас и сразу передать ключ в эту проверку (или в отдельный следующий вызов) 

// а за ключом кафе, как и предполагалось, просто смотреть со стороны - как его едят

// CreateTestScenarioKey - временный метод, создающий ключ, в котором хранится последовательность тестового сценария,
// потом переделать его в генерацию ключа из веба
// 

// CreateTestScenarioLists




#endregion

namespace BackgroundDispatcher.Services
{
    public interface ITestTasksPreparationService
    {
        public Task<bool> TestDepthSetting(ConstantsSet constantsSet, int testDepth = 0);
        public Task<(List<string>, int)> CreateScenarioTasksAndEvents(ConstantsSet constantsSet, string storageKeyBookPlainTexts, List<string> rawPlainTextFields, List<int> delayList);
        public Task<int> RemoveTestBookIdFieldsFromEternalLog(ConstantsSet constantsSet, string key, List<int> uniqueBookIdsFromStorageKey);
    }

    public class TestTasksPreparationService : ITestTasksPreparationService
    {
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICollectTasksInPackageService _collect;
        private readonly ITestScenarioService _scenario;
        private readonly ITestRawBookTextsStorageService _store;
        private readonly IRawBookTextAddAndNotifyService _add;
        private readonly ICacheManagerService _cache;

        public TestTasksPreparationService(
            IAuxiliaryUtilsService aux,
            ICollectTasksInPackageService collect,
            ITestScenarioService scenario,
            ITestRawBookTextsStorageService store,
            IRawBookTextAddAndNotifyService add,
            ICacheManagerService cache)
        {
            _aux = aux;
            _collect = collect;
            _scenario = scenario;
            _store = store;
            _add = add;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestTasksPreparationService>();

        //private bool _isTestInProgress;

        // во время теста (при старте) -
        // проверить наличие ключа-хранилища тестовых задач
        // если ключа нет, то и тест надо прекратить (может, в зависимости от сценария)

        // проверить наличие ключа созданных описаний тестовых задач
        // если ключа нет, надо его создать -
        // считать всё из ключа-хранилища
        // прочитать у каждого поля номер книги и прочее
        // записать в ключ описаний листом или по полям, поля как в ключе хранения лога - номера книг и со смещением признак языка

        // план работ -
        // _1 разделить методы по новым классам
        // _2 методы логов списков выбросить
        // 3 добавить в метод нового элемента списка само создание списка, если на вход не дали готовый
        // 4 посмотреть класс создания ключа пакета и потом ключа кафе - они явно должны подойти для теста (нет)
        // метод, который пишет в ключ текста и события, есть в контроллере, чтобы его (их) сделать общим, надо вынести в общую библиотеку
        // 5 и, кстати, надо туда же вынести все операции с Redis (AccessCacheData from BooksTextsSplit.Library)
        // (а потом и базой - возможно) с базой всё же один сервер работает, всем остальным про неё знать не нужно
        // 6 перенести общие модели в общую библиотеку

        public async Task<bool> TestDepthSetting(ConstantsSet constantsSet, int testDepth = 0)
        {
            string testSettingKey1 = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.Value; // testSettingKey1
            double testSettingKey1LifeTime = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.LifeTime;

            bool testSettingKey1WasDeleted = await _aux.RemoveWorkKeyOnStart(testSettingKey1); // TO REMOVE

            string testSettingField1 = constantsSet.Prefix.IntegrationTestPrefix.SettingField1.Value; // f1 (test depth)

            string test1Depth2 = constantsSet.Prefix.IntegrationTestPrefix.DepthValue2.Value; // DistributeTaskPackageInCafee

            // to collect all depths in array and select the element via testDepth is it set (if testDepth=0 it means automatic mode)

            // здесь задаётся глубина теста - название метода, в котором надо закончить тест
            // при дальнейшем углублении теста показывать этапы прохождения
            await _cache.WriteHashedAsync<string>(testSettingKey1, testSettingField1, test1Depth2, testSettingKey1LifeTime);

            return testSettingKey1WasDeleted;
        }

        // создать из полей временного хранилища тестовую задачу, загрузить её и создать ключ оповещения о приходе задачи
        // получает список string rawPlainTextFields гуид-полей сырых текстов и задержек между ними (List<int> delayList)
        // это синхронные списки (используется значение из того, где оно не пустое/нулевое)
        public async Task<(List<string>, int)> CreateScenarioTasksAndEvents(ConstantsSet constantsSet, string storageKeyBookPlainTexts, List<string> rawPlainTextFields, List<int> delayList)
        {
            Logs.Here().Information("CreateScenarioTasksAndEvents started but it is still empty");
            List<string> uploadedBookGuids = new();
            int timeOfAllDelays = 0;
            // создать ключ для хранения плоского текста книги из префикса BookTextFieldPrefix и имитации(!) bookTextSplit server Guid
            string bookTextSplitGuid = Guid.NewGuid().ToString();
            string bookPlainText_KeyPrefixGuid = $"{constantsSet.BookPlainTextConstant.KeyPrefix.Value}:{bookTextSplitGuid}";
            double keyExistingTimePlain = constantsSet.BookPlainTextConstant.KeyPrefix.LifeTime;
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            double keyExistingTimeFrom = constantsSet.EventKeyFrom.LifeTime;

            int rawPlainTextFieldsCount = rawPlainTextFields.Count;
            int delayListCount = delayList.Count;
            bool areTheListsLengthDifferent = rawPlainTextFieldsCount != delayListCount;
            // ещё проверить списки на нул и на нулевую длину
            
            if (areTheListsLengthDifferent)
            {
                _aux.SomethingWentWrong(areTheListsLengthDifferent);
            }

            for (int i = 0; i < rawPlainTextFieldsCount; i++)
            {
                string currentField = rawPlainTextFields[i];
                int currentDelay = delayList[i];
                if (String.Equals(currentField, ""))
                {
                    if (currentDelay == 0)
                    {
                        _aux.SomethingWentWrong(true);
                    }
                    //delay(currentDelay) here
                    Logs.Here().Information("Delay on {0} msec is started.", currentDelay);
                    await Task.Delay(currentDelay);
                    timeOfAllDelays += currentDelay;
                    Logs.Here().Information("Delay was finished, total time of all delays = {0} msec.", timeOfAllDelays);
                }
                else
                {
                    // имитация получения книги от контроллера
                    TextSentence bookPlainTextWithDescription = await _cache.FetchHashedAsync<TextSentence>(storageKeyBookPlainTexts, currentField);
                    string bookGuid = bookPlainTextWithDescription.BookGuid;
                    uploadedBookGuids.Add(bookGuid);
                    Logs.Here().Information("BookGuid comparing - currentField = {0}, bookGuid = {1} ", currentField, bookGuid);

                    // create the key with the plain text
                    // key - тестовый ключ, имитирующий ключ-гуид фронт-сервера записи книг
                    // можно прямо в этом методе генерировать произвольный ключ -
                    // скажем, соответствующий префикс фронт-сервера и созданный здесь гуид
                    await _cache.WriteHashedAsync<TextSentence>(bookPlainText_KeyPrefixGuid, currentField, bookPlainTextWithDescription, keyExistingTimePlain);
                    Logs.Here().Information("Key bookPlainText with BookId = {0} was created - {@K} \n {@F}", bookPlainTextWithDescription.BookId, new { Key = bookPlainText_KeyPrefixGuid }, new { Field = currentField });

                    // create the key From
                    await _cache.WriteHashedAsync<string>(eventKeyFrom, currentField, bookPlainText_KeyPrefixGuid, keyExistingTimeFrom);
                    Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V}", new { Key = eventKeyFrom }, new { Field = currentField }, new { Value = bookPlainText_KeyPrefixGuid });
                }
            }
            return (uploadedBookGuids, timeOfAllDelays);
        }

        public async Task<int> PrepareTestBookIdsListFromEternalLog(ConstantsSet constantsSet, List<string> rawPlainTextFields, List<int> delayList)
        {




            return 0;
        }

        // при первом удалении можно проверять наличие
        public async Task<int> RemoveTestBookIdFieldsFromEternalLog(ConstantsSet constantsSet, string keyBookPlainTextsHashesVersions, List<int> uniqueBookIdsFromStorageKey)
        {
            int count = 0;
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.EternalBookPlainTextHashesLog.Value; // key-book-plain-texts-hashes-versions-list
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
            if (uniqueBookIdsFromStorageKey != null && keyBookPlainTextsHashesVersions != null)
            {
                if (keyBookPlainTextsHashesVersions == "")
                {
                    keyBookPlainTextsHashesVersions = keyBookPlainTextsHashesVersionsList;
                }
                List<int> filedsToDelete = new();
                foreach (var engBookId in uniqueBookIdsFromStorageKey)
                {
                    filedsToDelete.Add(engBookId);
                    int rusBookId = engBookId + chapterFieldsShiftFactor;
                    filedsToDelete.Add(rusBookId);
                }
                count = await _cache.DelFieldAsync<int>(keyBookPlainTextsHashesVersions, filedsToDelete);
            }
            return count;
        }

        private List<T> OperationWithListFirstElement<T>(T theFirstElement)
        {
            List<T> fieldsFromStorageKey = new();
            fieldsFromStorageKey.Add(theFirstElement);
            return fieldsFromStorageKey;
        }
    }
}
