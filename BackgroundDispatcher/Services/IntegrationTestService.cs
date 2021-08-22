using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Models;
using BooksTextsSplit.Library.Services;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

// план работ -
// 1. выделить в метод запуск ключей задач (название ключа, количество задач, задержка между запусками ...)

// 2. сделать ключ регулирования запуска теста теста - порядок запуска задача-тест-задача, можно в значении в виде массива инт/бул
// (массив неудобно при ручном запуске, лучше текстом или числом, предусмотреть возможную задержку между пунктами)
// key - "testSettingKey2", rename it to test-of-addition-of-3rd-task:settings (value int)
// field (int) - test steps
// values mean the following - 
// -100 - delay in milliseconds
// 0 - stop the test processing
// 100 - start task subscribeOnFrom
// 20x - start hset test test x (x = 1, 2, 3 - the test depth)
// 300 - start task subscribeOnFrom:test
// потом (при формировании из веб-интерфейса) можно сделать модель с массивом
//
// можно было не громоздить фальшивый ключ для тестов, а проверять из контроллера поле и запрещать прохождение реальной задачи
//
// организовать тестовый ключ с плоским текстом
// key - bookPlainTexts:bookSplitGuid:f0c17236-3d50-4bce-9843-15fc9ee79bbd:test
// field_1 - bookText:bookGuid:0622f50c-d1d7-4dac-af14-b2a936fa750a - LanguageId:0, UploadVersion:30, BookId:79
// field_2 - bookText:bookGuid:99e02275-c842-426c-8369-3ee72b668845 - LanguageId:1, UploadVersion:30, BookId:79
// field_3 - bookText:bookGuid:a97346d4-1506-4b63-8f6d-4ff7afd217f4 - LanguageId:0, UploadVersion:30, BookId:78
// field_4 - bookText:bookGuid:2d4e3513-ee43-4ff9-8993-2eb0bff53aed - LanguageId:1, UploadVersion:30, BookId:78
// из этого ключа-хранилища создавать ключ со стандартным названием (без test в конце) и с одним из полей по очереди
// на каждый ключ генерировать subscribeFrom field_1 key(w/o test)
// 
// план -
// 1 считать ключ-хранилище тестовых плоских текстов
// 2 создать лист модели из ключа, поля и значения плоского текста и к нему сигнального
// (значение теста в лист не писать, доставать из кэша в момент записи нового ключа)
// 3 поля тестового хранилища записаны заранее в нужном порядке и хранятся прямо в методе константами
// 4
// 
// тогда можно писать подряд ключ плоского текста и сигнальный ключ подписки для него
//
// прозрачные тесты - сервер не должен видеть никакой разницы между тестом и работой
// в тесто проверять счётчик надо - чтобы дождаться момента, когда свободно
// потом как-то донести блокировку до контроллера - можно в контроллере проверять ключ тест
// а в тесте проверять счётчик и кафе перед загрузкой тестовых задач
// 

namespace BackgroundDispatcher.Services
{
    public interface IIntegrationTestService
    {
        public Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public Task<bool> EventCafeOccurred(ConstantsSet constantsSet, CancellationToken stoppingToken);
        //public Task<bool> IsTestResultAsserted(ConstantsSet constantsSet, string keyEvent, CancellationToken stoppingToken);

        // create key with field/value/lifetime one or many times (with possible delay after each key has been created)
        //public Task<bool> TestKeysCreationInQuantityWithDelay(int keysCount, int delayBetweenMsec, string key, string field, string value, double lifeTime);

        // create one key with many fields/values (with possible delay after each key has been created)
        //public Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string[] key, string[] field, string[] value, double[] lifeTime);

        // create many keys with field/value/lifetime each (with possible delay after each key has been created)
        //public Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string key, string[] field, string[] value, double lifeTime);
        public void SetIsTestInProgress(bool init_isTestInProgress);
        //public bool IsTestInProgress();
        //public Task<bool> IsPreassignedDepthReached(ConstantsSet constantsSet, string currentDepth, CancellationToken stoppingToken);
        //public Task<bool> TempTestOf3rdTaskAdded(ConstantsSet constantsSet, bool tempTestOf3rdTaskAdded, bool startTask3beforeTest);
        public Task<bool> RemoveWorkKeyOnStart(string key);
    }

    public class IntegrationTestService : IIntegrationTestService
    {
        #region Health Check operating procedure

        // порядок работы встроенного интеграционного теста (далее health check) -

        // _convert.CreateTestScenarioKey
        // создание сценария производится из выбранного (по номеру сценария) заранее заданного массива с действиями
        // каждый элемент массива означает книгу или пару с номером и версией (не конкретными, а по порядку) или время задержки
        // метод записывает этот массив сценария в ключ testScenarioSequenceKey = test-scenario-sequence,
        // снабжая его номерами шагов (номера полей) - в будущем это будет общение между серверами

        // List<int> uniqueBookIdsFromStorageKey = _prepare.CreateTestBookPlainTexts(constantsSet, stoppingToken, testPairsCount, delayAfter);
        // тут неправильно указание количества пар и задержка - теперь всё из сценария
        // и вообще, переделать на последовательное выполнение, а не цепочка вглубь - фиг, что поймёшь потом

        // TestTasksPreparationService CreateTestBookPlainTexts
        // берёт ключ хранилища тестовых книг, указанный явным образом (временно, потом что-то придумать)

        // производим инвентаризацию хранилища тестовых книг - составляем список полей (всех хранящихся книг) и список уникальных номеров книг (английских, русская книга из пары вычисляется)

        // вызывается _prepare.CreateTestBookPlainTexts и создается комплект тестовых книг
        // для этого обращаемся к стационарному хранилищу тестовых книг в ключе storageKeyBookPlainTexts
        // *** потом их надо уметь удобно обновлять и хранить копии в базе (в специальном разделе?)
        // вызывается _store.CreateTestBookIdsListFromStorageKey
        // общее назначение метода - создать два списка - номеров книг и мест хранения (название поля в хранилище)
        // для этого создаём два новых списка -
        // int uniqueBookIdsFromStorageKey - уникальные номера книг и string guidFieldsFromStorageKey - названия полей в хранилище
        // проверяем наличие ключа хранилища
        // выгружаем всё хранилище в словарь
        // перебираем пары <string, TextSentence>
        // название поля string сразу записываем в новый список
        // достаём номер книги из очередного TextSentence и проверяем его наличие в новом списке номеров
        // если такого номера ещё нет, добавляем его в список
        // возвращаем из метода два списка (очевидно несинхронные и разной длины)

        // вызывается RemoveTestBookIdFieldsFromEternalLog
        // используя список уникальных ключей, надо удалить все тестовые ключи из вечного лога
        // здесь для первичной очистки и для контроля (вдруг по дороге упадёт и ключи останутся)

        // вызывается _collect.CreateTaskPackageAndSaveLog
        // вне теста этот метод используется для для создания ключа готового пакета задач -
        // с последующей генерацией (другим методом) ключа кафе для оповещения о задачах бэк-сервера
        // сохраняются названия гуид-полей книг, созданные контроллером, но они перезаписываются в новый ключ, уникальный для собранного пакета
        // одновременно, при перезаписи содержимого книг, оно анализируется (вычисляется хэш текста) и проверяется на уникальность
        // если такая книга уже есть, это гуид-поле удаляется
        // здесь этот метод используется для записи хэшей в вечный лог -
        // при этом вычисляются номера версий загружаемых книг, что и нужно вызывающему методу

        // вызывается _scenario.CreateTestScenarioLists - этот метод из ключа описания сценария
        // создаёт последовательность (список string rawPlainTextFields) гуид-полей сырых текстов
        // и задержек между ними (List<int> delayList) - и это синхронные списки
        // используется значение из того, где оно не нулевое
        // 
        // и опять вызывается RemoveTestBookIdFieldsFromEternalLog - удалить все тестовые ключи из вечного лога второй раз -
        // после завершения использования для подготовки тестовых текстов
        // 
        // вызывается CreateScenarioTasksAndEvents
        // создать из полей временного хранилища тестовую задачу, загрузить её и создать ключ оповещения о приходе задачи

        // *** отчёт по тесту -
        // *** надо создавать в контрольных точках по мере прохождения теста
        // *** и сохранять в ключе тест_отчёт с полями номерам шагов
        // *** или названиями контрольных точек
        // *** (но номера тоже хотелось бы)

        #endregion

        private readonly IAuxiliaryUtilsService _aux;
        private readonly IConvertArrayToKeyWithIndexFields _convert;
        private readonly ITestScenarioService _scenario;
        private readonly ITestRawBookTextsStorageService _store;
        private readonly ICollectTasksInPackageService _collect;
        private readonly ICacheManagerService _cache;
        private readonly ITestTasksPreparationService _prepare;

        public IntegrationTestService(
            IAuxiliaryUtilsService aux,
            IConvertArrayToKeyWithIndexFields convert,
            ITestScenarioService scenario,
            ITestRawBookTextsStorageService store,
            ICollectTasksInPackageService collect,
            ICacheManagerService cache,
            ITestTasksPreparationService prepare)
        {
            _aux = aux;
            _convert = convert;
            _scenario = scenario;
            _store = store;
            _collect = collect;
            _cache = cache;
            _prepare = prepare;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<IntegrationTestService>();

        private bool _isTestInProgress; // can be removed

        // поле "тест запущен" _isTestInProgress ставится в true - его проверяет контроллер при отправке задач
        // устанавливаются необходимые константы
        // записывается ключ глубины теста test1Depth-X - в нём хранится название метода, в котором тест должен закончиться
        // *** потом надо переделать глубину в список контрольных точек, в которых тест будет отчитываться о достижении их
        // удаляются результаты тестов (должны удаляться после теста, но всякое бывает)
        public async Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            Logs.Here().Information("Integration test was started.");
            // поле - отражение такого же поля в классе подписок, формально они не связаны, но по логике меняются вместе
            _isTestInProgress = true; // not used?

            #region Constants preparation

            // есть способ сделать этот ключ заранее известным -
            // надо в режим записи тестовых книг добавить изменение ключа записи в стандартный (стабильный) из констант
            // тогда можно всюду не передавать, но можно уже и не менять (наверное)
            // ещё вариант - добавить слово тест в начале и потом искать ключ по маске, не обращая внимания на гуид 
            // тогда можно суммировать несколько ключей с тестовыми книгами
            // можно объединить две идеи - BookPlainText будет сохранять ключи с гуид,
            // а тест будет проверять их все по маске и сохранять в свой постоянный ключ - а исходные удалять
            // ещё предусмотреть удаление - какой-то способ очистки ключа хранения тестовых книг
            string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test";

            // проверить обновление констант перед вызовом теста

            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventFileldTest = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            string testScenario1description = constantsSet.IntegerConstant.IntegrationTestConstant.TestScenario1.Description;
            // EternalLog (needs to rename)
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list
            string testResultsKey1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.Value; // testResultsKey1

            #endregion

            //вариант устарел, надо изменить способ сообщения о прохождении шага теста
            // можно из очередного метода по ходу работы вызвать контрольный метод из тестов,
            // он проверит тест сейчас или нет и, если надо, запишет имя вызвавшего метода в ключ результатов с полем имени метода
            // а в конце текста проверять значение в определённом имени
            // в качестве значения можно передавать что-то целое, что есть в текущем методе
            // счётчик вызовов, например
            // записывать в поле времени или в поле по порядку записывать сложный класс с местом и временем, а последний номер хранить в нулевом поле
            // после теста посчитать все временные промежутки и сравнить с эталоном
            // из каждого метода вызывать метод теста типа отчёт о ходе выполнения задачи, он проверит поле класса и если не тест, сразу же вернётся
            // этому методу передавать название
            // в классе тестов хранить поля -
            // bool тест или нет (уже есть)
            // int счётчик - номер поля (доступ к номеру через Interlocked)
            // ещё нужен номер всего теста, но его можно хранить проще - где-то в ключе или классе теста, он будет нужен в конце, при записи в вечный лог
            // при вызове метода AddStageToTestTaskProgressReport передать ему класс TextSentence или что будет доступно в этой точке
            // внутри метода добавить текущее время (потом можно засечку секундомера)
            // взять инкремент текущего номера и это будет имя поля в ключе отчёта
            // дописать всё в класс текст и записать его в ключ
            // по окончанию теста показать ход его выполнения с отклонениями от эталона
            // и потом добавить в список и записать в вечный лог с нулевым номером (bookId)

            bool testSettingKey1WasDeleted = await _prepare.TestDepthSetting(constantsSet);
            // сделать перегрузку для множества ключей
            bool testResultsKey1WasDeleted = await _aux.RemoveWorkKeyOnStart(testResultsKey1);
            bool controlListOfTestBookFieldsKeyWasDeleted = await _aux.RemoveWorkKeyOnStart(controlListOfTestBookFieldsKey);

            if (_aux.SomethingWentWrong(testSettingKey1WasDeleted, testResultsKey1WasDeleted, controlListOfTestBookFieldsKeyWasDeleted))
            {
                return false;
            }

            // test scenario selection - получение номера сценария из ключа запуска теста
            int testScenario = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

            // достаётся из ключа запуска теста номер (вариант) сценария и создаётся сценарий - временно по номеру
            // *** потом из веба будет приходить массив инт с описанием сценария
            // *** добавить в метод необязательный параметр массив инт
            // *** дальше надо думать с определением номера сценария - по идее больше это не нужно, выбранный сценарий хранится в ключе
            // тут временно создаём ключ с ходом сценария (потом это будет делать веб)
            _ = await _convert.CreateTestScenarioKey(constantsSet, testScenario);

            Logs.Here().Information("Test scenario {0} was selected and TestScenarioKey was created.", testScenario);

            // производим инвентаризацию хранилища тестовых книг - составляем список полей (всех хранящихся книг) и список уникальных номеров книг (английских, русская книга из пары вычисляется)
            // storageKeyBookPlainTexts - ключ хранилища исходных текстов тестовых книг
            // uniqueBookIdsFromStorageKey - список уникальных номеров английских книг
            // guidFieldsFromStorageKey - список полей всех хранящихся книг
            (List<int> uniqueBookIdsFromStorageKey, List<string> guidFieldsFromStorageKey) = await _store.CreateTestBookIdsListFromStorageKey(constantsSet, storageKeyBookPlainTexts); //, string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test")

            // используя список уникальных ключей, надо удалить все тестовые ключи из вечного лога
            // в вечном логе отпечатки книг хранятся в полях int - с номерами bookId (русские - плюс константа сдвига)
            // здесь для первичной очистки и для контроля (вдруг по дороге упадёт и ключи останутся)
            int result1 = await _prepare.RemoveTestBookIdFieldsFromEternalLog(constantsSet, keyBookPlainTextsHashesVersionsList, uniqueBookIdsFromStorageKey);
            if (!(result1 > 0))
            {
                _aux.SomethingWentWrong(true);
            }

            // передаём список всех полей из временного хранилища, чтобы создать нужные записи в вечном логе
            // вне теста этот метод используется для для создания ключа готового пакета задач -
            // с последующей генерацией (другим методом) ключа кафе для оповещения о задачах бэк-сервера
            // сохраняются названия гуид-полей книг, созданные контроллером, но они перезаписываются в новый ключ, уникальный для собранного пакета
            // одновременно, при перезаписи содержимого книг, оно анализируется (вычисляется хэш текста) и проверяется на уникальность
            // если такая книга уже есть, это гуид-поле удаляется
            // здесь этот метод используется для записи хэшей в вечный лог -
            // при этом вычисляются номера версий загружаемых книг, что и нужно вызывающему методу
            // поскольку сценарий предусматривает выбор книг для тестирования по bookId, languageId и hashVersion -
            // всё это для тестовых книг вычисляет этот метод
            // taskPackageGuid здесь не используется и надо удалить этот ключ со всем содержимым
            string taskPackageGuid = await _collect.CreateTaskPackageAndSaveLog(constantsSet, storageKeyBookPlainTexts, guidFieldsFromStorageKey);
            if (taskPackageGuid != null)
            {
                bool taskPackageGuidWasDeleted = await _aux.RemoveWorkKeyOnStart(taskPackageGuid);
                Logs.Here().Information("TaskPackageGuid Key with all storage books was deleted with result - {0}.", taskPackageGuidWasDeleted);
            }

            // выходной список для запуска выбранного тестового сценария - поля сырых плоских текстов и задержки - два синхронных списка
            // в списке полей на месте, где будет задержка стоит "", а списке задержек на месте, где загружаются книги - нули
            (List<string> rawPlainTextFields, List<int> delayList) = await _scenario.CreateTestScenarioLists(constantsSet, uniqueBookIdsFromStorageKey);

            // и удалить их второй раз после завершения использования для подготовки тестовых текстов
            int result2 = await _prepare.RemoveTestBookIdFieldsFromEternalLog(constantsSet, keyBookPlainTextsHashesVersionsList, uniqueBookIdsFromStorageKey);
            if (!(result2 > 0))
            {
                _aux.SomethingWentWrong(true);
            }

            // вот тут создать ключ для проверки теста - с входящими полями книг
            int resultStartTest = await SetKeyWithControlListOfTestBookFields(constantsSet, storageKeyBookPlainTexts, rawPlainTextFields, stoppingToken);

            // создать из полей временного хранилища тестовую задачу, загрузить её и создать ключ оповещения о приходе задачи
            (List<string> uploadedBookGuids, int timeOfAllDelays) = await _prepare.CreateScenarioTasksAndEvents(constantsSet, storageKeyBookPlainTexts, rawPlainTextFields, delayList);
            Logs.Here().Information("CreateScenarioTasksAndEvents finished");

            bool testWasSucceeded = await WaitForTestFinishingTags(constantsSet, timeOfAllDelays, controlListOfTestBookFieldsKey, stoppingToken);

            // удалили ключ запуска теста, в дальнейшем - если полем запуска будет определяться глубина, то удалять только поле
            // но лучше из веб-интерфейса загружать в значение сложный класс - сразу и сценарий и глубину (и ещё что-то)
            bool eventKeyTestWasDeleted = await _cache.DeleteKeyIfCancelled(eventKeyTest);
            //bool testResultIsAsserted = testResult == test1IsPassed;
            bool finalResult = testWasSucceeded;

            // все константы или убрать в константы и/или перенести в метод DisplayResultInFrame
            string testDescription = $"Test scenario <{testScenario1description}>";
            char separTrue = '+';
            string textTrue = $"passed successfully";
            char separFalse = 'X';
            string textFalse = $"is FAILED";
            DisplayResultInFrame(finalResult, testDescription, separTrue, textTrue, separFalse, textFalse);

            // а потом удалить их третий раз - после завершения теста и проверки его результатов
            int result3 = await _prepare.RemoveTestBookIdFieldsFromEternalLog(constantsSet, "", uniqueBookIdsFromStorageKey);
            if (!(result3 > 0))
            {
                _aux.SomethingWentWrong(true);
            }

            // возвращаем состояние _isTestInProgress - тест больше не выполняется
            _isTestInProgress = false;
            return _isTestInProgress;
        }

        private async Task<bool> WaitForTestFinishingTags(ConstantsSet constantsSet, int timeOfAllDelays, string controlListOfTestBookFieldsKey, CancellationToken stoppingToken)
        {
            string testResultsKey1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.Value; // testResultsKey1
            string testResultsField1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField1.Value; // testResultsField1
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value; // 5000
            int delayTimeForTest1 = constantsSet.IntegerConstant.IntegrationTestConstant.DelayTimeForTest1.Value; // 1000

            // надо собрать (сложить) все задержки из сценария, добавить задержку таймера
            // и только после этого времени изучать результаты теста -
            // может быть несколько созданий ключа кафе
            // или смотреть на последнюю книгу в сценарии и ждать её появления в кафе
            // вообще, по смыслу ожидание начинается с окончания прогона сценария
            // или нет, ожидается специальный ключ результата - наверное, это уже устарело
            int preliminaryDelay = timerIntervalInMilliseconds + timeOfAllDelays;
            Logs.Here().Information("Tests control will wait asserted results {0} msec.", preliminaryDelay);
            await Task.Delay(preliminaryDelay);

            Logs.Here().Information("Tests control will wait when key {0} disappear.", controlListOfTestBookFieldsKey);
            bool isTestControlStillWaiting = true;
            bool isControlListOfTestBookFieldsKeyExist = true;
            int waitingCounter = (int)((double)preliminaryDelay / delayTimeForTest1) + 1; // +1 - на всякий случай
            Logs.Here().Information("Tests control will wait {0} attempts of key checking.", waitingCounter);

            while (isTestControlStillWaiting)
            {
                // вынести в отдельный метод с ранним возвратом по исчезнувшему ключу                
                (isControlListOfTestBookFieldsKeyExist, isTestControlStillWaiting) = await WaitIntoWhile(controlListOfTestBookFieldsKey, waitingCounter, delayTimeForTest1);
            }

            // исчезнувший ключ - не вполне надёжное средство оповещения, поэтому надо записать ещё ключ testResultsKey1 и тест дополнительно проверит его
            // надо осветить ситуацию, что ключ не исчез, а количество полей равно нулю
            // скажем, за время проверки в ключе добавились поля -
            // первоначальные поля удалены, счётчик ноль, а в ключе поля остались - сообщить об ошибке
            int remaindedFields = await _cache.FetchHashedAsync<int>(testResultsKey1, testResultsField1);
            // при нормальном завершении теста isControlListOfTestBookFieldsKeyExist = false и пройдет мимо сообщения об ошибке
            if (isControlListOfTestBookFieldsKeyExist)
            {
                Logs.Here().Error("Tests finished abnormally - Control List Key - {0}, Remainded Fields = {1}.", isControlListOfTestBookFieldsKeyExist, remaindedFields);
            }
            bool testWasSucceeded = !isControlListOfTestBookFieldsKeyExist && remaindedFields == 0;
            if (testWasSucceeded)
            {
                Logs.Here().Information("Tests finished - Control List Key - {0}, Remainded Fields = {1}.", isControlListOfTestBookFieldsKeyExist, remaindedFields);
            }
            return testWasSucceeded;
        }

        private async Task<(bool, bool)> WaitIntoWhile(string controlListOfTestBookFieldsKey, int waitingCounter, int delayTimeForTest1)
        {
            Logs.Here().Information("Tests control is still waiting asserted results.");
            bool isControlListOfTestBookFieldsKeyExist = await _cache.IsKeyExist(controlListOfTestBookFieldsKey);
            // isTestControlStillWaiting - это контроль while ожидать, пока тест не закончится или время ожидания не выйдет
            bool isTestControlStillWaiting = isControlListOfTestBookFieldsKeyExist;

            // если ключ исчез - isControlListOfTestBookFieldsKeyExist = false - сразу на выход
            if (isControlListOfTestBookFieldsKeyExist)
            {
                // если ключ ещё существует, сначала проверяем запасной ключ, потом немного ожидаем
                Logs.Here().Information("Key {0} existence check result is {1}.", controlListOfTestBookFieldsKey, isTestControlStillWaiting);
                waitingCounter--;
                Logs.Here().Information("Tests control still waiting {0} attempts of key checking.", waitingCounter);
                if (waitingCounter < 0)
                {
                    isTestControlStillWaiting = false;
                    Logs.Here().Information("Tests control finished waiting - while will {0}.", isTestControlStillWaiting);
                    // если время истекло, выходим без лишнего ожидания, хотя разница невелика
                    return (isControlListOfTestBookFieldsKeyExist, isTestControlStillWaiting);
                }
                await Task.Delay(delayTimeForTest1);
            }

            return (isControlListOfTestBookFieldsKeyExist, isTestControlStillWaiting);
        }

        // создать ключ для проверки теста - с входящими полями книг - в нужном порядке
        // нет, порядок не важен, но поля должны быть полями книг, а в значения можно записать bookId
        // будет много одинаковых, наверное, лучше писать плоский текст без текста
        // но это (пока) не имеет особого значения
        private async Task<int> SetKeyWithControlListOfTestBookFields(ConstantsSet constantsSet, string sourceKeyWithPlainTexts, List<string> rawPlainTextFields, CancellationToken stoppingToken)
        {
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            double keyExistTime = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.LifeTime; // 0.01
            Logs.Here().Information("Control list was fetched in list rawPlainTextFields - total {0} fields were fetched.", rawPlainTextFields.Count);

            int rawPlainTextFieldsCount = 0;
            foreach (string f in rawPlainTextFields)
            {
                TextSentence bookPlainText = await _cache.FetchHashedAsync<TextSentence>(sourceKeyWithPlainTexts, f);
                if (f != "")
                {
                    int bookId = bookPlainText.BookId;
                    bookPlainText.BookPlainText = null;
                    await _cache.WriteHashedAsync<TextSentence>(controlListOfTestBookFieldsKey, f, bookPlainText, keyExistTime);
                    rawPlainTextFieldsCount++;
                    Logs.Here().Information("Control list of test books - field {0} / value {1} was set in key {2}.", f, bookId, controlListOfTestBookFieldsKey);
                }
            }
            Logs.Here().Information("Control list was created - total {0} fields were set.", rawPlainTextFieldsCount);

            return rawPlainTextFieldsCount;
        }

        public async Task<bool> EventCafeOccurred(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package

            // выгрузить содержимое ключа кафе и сразу вернуть true в подписку, чтобы освободить место для следующего вызова
            // и всё равно можно прозевать вызов
            // для надёжности надо вернуть true, а потом сразу выгрузить ключ кафе, тогда точно не пропустить второй вызов
            // для точности сделать eventCafeIsNotExisted полем класса и отсюда её поставить в true - а потом не спеша делать всё остальное
            IDictionary<string, string> taskPackageGuids = await _cache.FetchHashedAllAsync<string>(cafeKey);

            _ = AssertProcessedBookFieldsAreEqualToControl(constantsSet, taskPackageGuids, stoppingToken);

            return true;
        }

        // метод предварительного сбора результатов теста
        // получает ключ пакета, достаёт бук и что с ним делает?
        // можно создать ключ типа список книг теста (важен порядок или нет?) при старте теста
        // и при проверке прохождения теста удалять из него поля, предварительно сравнивая bookId, bookGuid и bookHash
        // как все поля исчезнут, так тест прошёл нормально - если, конечно, не осталось лишних на проверку после теста
        // в смысле, ненайденных в стартовом списке теста
        private async Task<bool> AssertProcessedBookFieldsAreEqualToControl(ConstantsSet constantsSet, IDictionary<string, string> taskPackageGuids, CancellationToken stoppingToken)
        {
            // добавить счётчик потоков и проверить при большом количестве вызовов
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            string testResultsKey1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.Value; // testResultsKey1
            double keyExistingTime = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.LifeTime; // 0.007
            string testResultsField1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField1.Value; // testResultsField1
            int remaindedFields = -1;

            foreach (var g in taskPackageGuids)
            {
                (string taskPackageGuid, string vG) = g;

                Logs.Here().Information("taskPackageGuid {0} was fetched.", taskPackageGuid);

                IDictionary<string, TextSentence> fieldValuesResult = await _cache.FetchHashedAllAsync<TextSentence>(taskPackageGuid);
                int fieldValuesResultCount = fieldValuesResult.Count;
                int deletedFields = 0;
                Logs.Here().Information("fieldValuesResult with count {0} was fetched from taskPackageGuid.", fieldValuesResultCount);

                // write test asserted results in the report key

                foreach (KeyValuePair<string, TextSentence> p in fieldValuesResult)
                {
                    (string fP, TextSentence vP) = p;
                    Logs.Here().Information("Field {0} was found in taskPackageGuid and will be deleted in key {1}.", fP, controlListOfTestBookFieldsKey);

                    bool result0 = await CheckAssertFieldsAreEqualToControlAndEternal(constantsSet, fP, vP, stoppingToken);

                    if (result0)
                    {
                        bool result1 = await _cache.DelFieldAsync(controlListOfTestBookFieldsKey, fP);
                        if (result1)
                        {
                            deletedFields++;
                            Logs.Here().Information("The comparison returned {0} and field {1} / value {2} was sucessfully deleted in key {3}.", result0, fP, vP.BookId, controlListOfTestBookFieldsKey);
                        }
                    }
                }

                remaindedFields = fieldValuesResultCount - deletedFields;

                bool result2 = await _cache.IsKeyExist(controlListOfTestBookFieldsKey);
                if (!result2)
                {
                    Logs.Here().Information("There are no remained fields in key {0}. Test is completed (but does not know about it).", controlListOfTestBookFieldsKey);

                    // исчезнувший ключ - не вполне надёжное средство оповещения,
                    // поэтому надо записать ещё ключ testResultsKey1 и тест дополнительно проверит его
                    await _cache.WriteHashedAsync<int>(testResultsKey1, testResultsField1, remaindedFields, keyExistingTime);

                    return true;
                }
            }

            bool assertedResult = false;
            if (remaindedFields == 0)
            {
                assertedResult = true;
            }
            return assertedResult;
        }

        // 
        private async Task<bool> CheckAssertFieldsAreEqualToControlAndEternal(ConstantsSet constantsSet, string fP, TextSentence vP, CancellationToken stoppingToken)
        {
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000

            // здесь сравнить bookId, bookGuid и bookHash книг 3
            int bookId = vP.BookId;
            int languageId = vP.LanguageId;
            int bookHashVersion = vP.HashVersion;

            TextSentence bookPlainFromControl = await _cache.FetchHashedAsync<TextSentence>(controlListOfTestBookFieldsKey, fP);
            //Logs.Here().Information("{@C}.", new { Value = bookPlainFromControl });
            bool bookIdComparingWithControl = bookPlainFromControl.BookId == vP.BookId;
            bool bookGuidComparingWithControl = String.Equals(bookPlainFromControl.BookGuid, vP.BookGuid);

            // здесь ещё посмотреть и сравнить в вечном логе
            // здесь надо перевести bookId в вид со сдвигом
            int fieldBookIdWithLanguageId = bookId + languageId * chapterFieldsShiftFactor;
            Logs.Here().Information("Check FetchHashedAsync<int, List<TextSentence>> - key {0}, field {1}, element {2}.", keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, bookHashVersion);
            List<TextSentence> bookPlainTextsVersions = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId);
            TextSentence bookPlainFromEternalLog = bookPlainTextsVersions[bookHashVersion];
            //Logs.Here().Information("{@E} is bookPlainTextsVersions[{1}].", new { BookPlainFromEternalLog = bookPlainFromEternalLog }, bookHashVersion);

            bool bookIdComparingWithEternal = bookPlainFromEternalLog.BookId == vP.BookId;
            bool bookGuidComparingWithEternal = String.Equals(bookPlainFromEternalLog.BookGuid, vP.BookGuid);
            bool bookHashComparingWithEternal = String.Equals(bookPlainFromEternalLog.BookPlainTextHash, vP.BookPlainTextHash);
            bool bookHashVersionComparingWithEternal = bookPlainFromEternalLog.HashVersion == vP.HashVersion;

            bool result0 = bookIdComparingWithControl && bookGuidComparingWithControl && bookIdComparingWithEternal && bookGuidComparingWithEternal && bookHashComparingWithEternal && bookHashVersionComparingWithEternal;

            return result0;
        }

        // финальный метод проверки результатов теста
        //public async Task<bool> IsTestResultAsserted(ConstantsSet constantsSet, string keyEvent, CancellationToken stoppingToken)
        //{
        //    // можно сохранять ключи всех проверенных пакетов - в листе (за текущий сеанс) или в ключе (на произвольное время)
        //    // если в ключе, то полем может быть дата или лучше гуид сеанса(текущий гуид сервера), а в значении лист ключей пакетов
        //    // другой вариант - ключ пакета это поле, а в значении что-нибудь, например, лист задач пакета
        //    // теперь пора сделать правильные тестовые тексты и проверять хэш текста при загрузке

        //    // рекурсия?
        //    IDictionary<string, string> keyEventDataList = await _cache.FetchHashedAllAsync<string>(keyEvent);
        //    int keyEventDataListCount = keyEventDataList.Count;

        //    foreach (var d in keyEventDataList)
        //    {
        //        // обычно должен быть один элемент, но надо рассмотреть вариант, что может успеть появиться второй
        //        (var f, var v) = d;
        //        Logs.Here().Information("Dictionary element is {@F} {@V}.", new { Filed = f }, new { Value = v });

        //        // поле и значение одинаковые, там ключ пакета задач
        //        // достать все поля и значения из ключа, в значениях текст, сравнить его (хэш?) с исходным
        //        IDictionary<string, TextSentence> plainTextsDataList = await _cache.FetchHashedAllAsync<TextSentence>(v);

        //        foreach (var t in plainTextsDataList)
        //        {
        //            (var bookGuid, var bookPlainText) = t;
        //            Logs.Here().Information("Dictionary element is HashVersion = {0}, BookId = {1}, LanguageId = {2}, \n BookGuidField = {3}, \n BookGuid = {4}, \n BookPlainTextHash = {5}", bookPlainText.HashVersion, bookPlainText.BookId, bookPlainText.LanguageId, bookGuid, bookPlainText.BookGuid, bookPlainText.BookPlainTextHash);


        //        }
        //    }
        //    return true;
        //}

        //private async Task<bool> WriteHashedAsyncWithDelayAfter<T>(string key, string field, T value, double lifeTime, CancellationToken stoppingToken, int delayAfter = 0)
        //{
        //    if (lifeTime > 0)
        //    {
        //        Logs.Here().Information("Event {@K} {@F} will be created.", new { Key = key }, new { Field = field });
        //        await _cache.WriteHashedAsync<T>(key, field, value, lifeTime);

        //        if (delayAfter > 0)
        //        {
        //            Logs.Here().Information("Delay after key writing  will be {0} msec.", delayAfter);
        //            await Task.Delay(delayAfter);
        //        }
        //        return true;
        //    }
        //    Logs.Here().Warning("{@K} with {@T} cannot be created.", new { Key = key }, new { LifeTime = lifeTime });
        //    return false;
        //}

        //public async Task<bool> IsPreassignedDepthReached(ConstantsSet constantsSet, string currentDepth, CancellationToken stoppingToken)
        //{
        //    // создаем сообщение об успешном тесте
        //    string testResultsKey1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.Value; // testResultsKey1
        //    double testResultsKey1LifeTime = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.LifeTime;
        //    string testResultsField1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField1.Value; // testResultsField1
        //    int resultTest1Passed = constantsSet.IntegerConstant.IntegrationTestConstant.ResultTest1Passed.Value; // 1

        //    // этот ключ можно использовать как счетчик вызовов обработчика (но лучше поле)
        //    await _cache.WriteHashedAsync<int>(testResultsKey1, testResultsField1, resultTest1Passed, testResultsKey1LifeTime);

        //    string testSettingKey1 = "testSettingKey1";
        //    string testSettingField1 = "f1"; // test depth

        //    // получать в параметрах, чтобы определить, из какого места вызвали
        //    string test1Depth1 = "HandlerCallingDistributore"; // other values - in constants
        //    string test1Depth2 = "DistributeTaskPackageInCafee";

        //    // здесь задана нужная глубина теста
        //    string targetTest1Depth = await _cache.FetchHashedAsync<string>(testSettingKey1, testSettingField1);
        //    bool keyWasDeleted = await _cache.DelFieldAsync(testResultsKey1, testSettingField1);

        //    // this method result is returned to variable <bool> targetDepthNotReached
        //    return currentDepth != targetTest1Depth;
        //}

        public void SetIsTestInProgress(bool init_isTestInProgress)
        {
            Logs.Here().Information("SetIsTestInProgress will changed _isTestInProgress {0} on {1}.", _isTestInProgress, init_isTestInProgress);
            _isTestInProgress = init_isTestInProgress;
            Logs.Here().Information("New state of _isTestInProgress is {0}.", _isTestInProgress);
        }

        //public bool IsTestInProgress()
        //{
        //    Logs.Here().Information("The state of _isTestInProgress was requested. It is {0}.", _isTestInProgress);
        //    return _isTestInProgress;
        //}

        private void DisplayResultInFrame(bool result, string testDescription, char separTrue, string textTrue, char separFalse, string textFalse)
        {// Display result in different frames (true in "+" and false in "X" for example)
            if (result)
            {
                string successTextMessage = $"{testDescription} {textTrue}";
                (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separTrue, successTextMessage);
                Logs.Here().Information("{0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
            }
            else
            {
                string successTextMessage = $"{testDescription} {textFalse}";
                (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separFalse, successTextMessage);
                Logs.Here().Information("{0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
                //Logs.Here().Warning("Test scenario {0} FAILED.", testScenario);
            }
        }

        // метод, создающий ключи в цикле, дополнить и массивом ключей тоже - можно сделать несколько перезагрузок
        // если массив ключей, то массивы полей и значений совпадающие с ним по размерности
        // а если ключ один, то в цикле пишутся несколько полей со значениями
        // если всё по одному, то в цикле пишутся одинаковые - но зачем?

        // rename to TestKeysCreationInQuantityWithDelayAfter
        //public async Task<bool> TestKeysCreationInQuantityWithDelay(int keysCount, int delayBetweenMsec, string key, string field, string value, double lifeTime)
        //{// create key with field/value/lifetime one or many times (with possible delay after each key has been created)
        //    if (keysCount > 0 && lifeTime > 0)
        //    {
        //        for (int i = 0; i < keysCount; i++)
        //        {
        //            Logs.Here().Information("Event {@K} {@F} will be created.", new { Key = key[i] }, new { Field = field[i] });
        //            await _cache.WriteHashedAsync<string>(key, field, value, lifeTime);

        //            if (delayBetweenMsec > 0)
        //            {
        //                await Task.Delay(delayBetweenMsec);
        //                Logs.Here().Information("Delay between events is {0} msec.", delayBetweenMsec);
        //            }
        //        }
        //        return true;
        //    }
        //    Logs.Here().Warning("{@K} with {@T} in {@C} cannot be created.", new { Key = key }, new { LifeTime = lifeTime }, new { KeysCount = keysCount });
        //    return false;
        //}

        //public async Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string[] key, string[] field, string[] value, double[] lifeTime)
        //{// create one key with many fields/values (with possible delay after each key has been created)
        //    int keyLength = key.Length;
        //    int fieldLength = field.Length;
        //    int valueLength = value.Length;
        //    int lifeTimeLength = lifeTime.Length;

        //    if (keyLength == fieldLength && fieldLength == valueLength && valueLength == lifeTimeLength)
        //    {
        //        for (int i = 0; i < keyLength; i++)
        //        {
        //            bool createKeyI = await TestKeysCreationInQuantityWithDelay(1, delayBetweenMsec, key[i], field[i], value[i], lifeTime[i]);
        //            if (!createKeyI)
        //            {
        //                return false;
        //            }
        //        }
        //        return true;
        //    }
        //    Logs.Here().Warning("Some arrays lengths are mismatched.");
        //    return false;
        //}

        //public async Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string key, string[] field, string[] value, double lifeTime)
        //{// create many keys with field/value/lifetime each (with possible delay after each key has been created)
        //    int fieldLength = field.Length;
        //    int valueLength = value.Length;

        //    if (fieldLength == valueLength)
        //    {
        //        for (int i = 0; i < fieldLength; i++)
        //        {
        //            bool createKeyI = await TestKeysCreationInQuantityWithDelay(1, delayBetweenMsec, key, field[i], value[i], lifeTime);
        //            if (!createKeyI)
        //            {
        //                return false;
        //            }
        //        }
        //        return true;
        //    }
        //    Logs.Here().Warning("Some arrays lengths are mismatched.");
        //    return false;
        //}

        //public async Task<bool> TempTestOf3rdTaskAdded(ConstantsSet constantsSet, bool tempTestOf3rdTaskAdded, bool startTask3beforeTest)
        //{
        //    // создание третьей задачи, когда две только уехали на обработку по таймеру - как поведёт себя вызов теста в этот момент
        //    // рассмотреть два варианта - вызов теста до появления третьей задачи и после
        //    // по идее в первом варианте третья задача должна остаться проигнорироаанной
        //    // а во втором - тест должен отложиться на 10 секунд и потом задача должна удалиться
        //    // выделить в отдельный метод?
        //    // tempTestOf3rdTaskAdded - from redis key
        //    bool result;
        //    if (tempTestOf3rdTaskAdded)
        //    {
        //        // рассмотреть два варианта - вызов теста после появления третьей задачи и до
        //        if (startTask3beforeTest)
        //        {
        //            // здесь тест должен отложиться на 10 секунд и потом одиночная задача должна удалиться
        //            Logs.Here().Information("Test will call StartTask3beforeTest {0})", startTask3beforeTest);
        //            result = await StartTask3beforeTest(constantsSet);
        //        }
        //        else
        //        {
        //            // здесь третья задача должна остаться проигнорироаанной, а тест выполниться сразу же, без ожидания 10 сек
        //            Logs.Here().Information("Test will call StartTestBeforeTask3 {0})", startTask3beforeTest);
        //            result = await StartTestBeforeTask3(constantsSet);
        //        }

        //        Logs.Here().Information("Temporary test finished with result = {0}.", result);
        //        return result;
        //    }

        //    return false;
        //}

        //private async Task<bool> StartTask3beforeTest(ConstantsSet constantsSet)
        //{
        //    string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
        //    double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime
        //    string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
        //    string eventFileldTest = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test

        //    // сначала создаём третью задачу, а потом даём команду на запуск теста
        //    await _cache.WriteHashedAsync<string>(eventKeyFrom, "count", "testTask", eventKeyFromTestLifeTime);

        //    await _cache.WriteHashedAsync<int>(eventKeyTest, eventFileldTest, 1, eventKeyFromTestLifeTime);

        //    // to read value for awaiting when keys will be written
        //    int checkValue = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

        //    return checkValue == 1;
        //}

        //private async Task<bool> StartTestBeforeTask3(ConstantsSet constantsSet)
        //{
        //    string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
        //    double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime
        //    string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
        //    string eventFileldTest = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test

        //    // сначала даём команду на запуск теста, а потом создаём третью задачу (успеет ли тест её заблокировать)
        //    await _cache.WriteHashedAsync<int>(eventKeyTest, eventFileldTest, 1, eventKeyFromTestLifeTime);

        //    await _cache.WriteHashedAsync<string>(eventKeyFrom, "count", "testTask", eventKeyFromTestLifeTime);

        //    // to read value for awaiting when keys will be written
        //    int checkValue = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

        //    return checkValue == 1;
        //}

        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            return await _aux.RemoveWorkKeyOnStart(key);
        }
    }
}
