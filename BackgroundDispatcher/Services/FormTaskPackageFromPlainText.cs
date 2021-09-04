using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;
using BooksTextsSplit.Library.Models;
using System.Runtime.CompilerServices;

#region FormTaskPackageFromPlainText description

//
// план работ -
// в конце теста проверять ключи и поля на совпадение (в отдельном методе?)
// отмечать пройденную глубину теста
// сделать нормальное включение ветки теста с третьим ключом
// добавить второй сценарий теста - с шестью событиями
// и сделать больше тестовых ключей с текстами
// можно добавить в конце названий полей приметные номера - например с указанием, что тест, номер книги и язык
// и в ключ тоже что-то добавить (нет, в ключ не надо, он никуда не идёт)
// можно в ключ пакета в кафе добавлять контрольное слово для тестов и потом его находить для определения прохождения теста
// но вообще это всё лишнее - надо чтобы тест сам определял правильность прохождения
// вынести всё (что можно) из главного класса тестов в дополнительные
// 
// те поля, которые не получилось удалить (сами исчезли) надо сложить в отдельный список
// и отдать в специальный метод - он за ними присмотрит
//
// по кругу пока не ходить - в тестах проверить, захватываются ли все вызовы или что-то пропадает
// 
// 
// namespace CachingFramework.Redis.Providers
// public IEnumerable<KeyValuePair<string, T>> ScanHashed<T>(string key, string pattern, int pageSize = 10, ...)
// 
// 
// По умолчанию для .Net Core данные хранятся как Json с использованием System.Text.Json.
// Сериализацию можно настроить с помощью JsonSerializerOptions 
// Пакет NuGet CachingFramework.Redis.NewtonsoftJson
// Данные хранятся как Json с использованием Newtonsoft.Json.
// Сериализацию можно настроить с помощью JsonSerializerSettings
// var context = new RedisContext("localhost:6379", new JsonSerializer());
// RedisContext.DefaultSerializer = new JsonSerializer();
// 

#endregion

namespace BackgroundDispatcher.Services
{
    public interface IFormTaskPackageFromPlainText
    {
        public bool HandlerCallingsDistributor(ConstantsSet constantsSet, int currentChainSerialNum, CancellationToken stoppingToken);
    }

    public class FormTaskPackageFromPlainText : IFormTaskPackageFromPlainText
    {
        private readonly IEternalLogSupportService _eternal;
        private readonly ICollectTasksInPackageService _collect;
        private readonly ITestOfComplexIntegrityMainServicee _test;
        private readonly ITestReportIsFilledOutWithTimeImprints _report;
        private readonly ICacheManagerService _cache;

        public FormTaskPackageFromPlainText(
            IEternalLogSupportService eternal,
            ICollectTasksInPackageService collect,
            ITestOfComplexIntegrityMainServicee test,
            ITestReportIsFilledOutWithTimeImprints report,
            ICacheManagerService cache)
        {
            _eternal = eternal;
            _collect = collect;
            _test = test;
            _report = report;
            _cache = cache;
            _callingNumOfHandlerCallingsDistributor = 0;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<FormTaskPackageFromPlainText>();

        private int _callingNumOfHandlerCallingsDistributor;
        private int _currentChainSerialNum;

        // 
        private bool AddStageToProgressReport(ConstantsSet constantsSet, int currentChainSerialNum, long currentWorkStopwatch, int workActionNum = -1, bool workActionVal = false, string workActionName = "", int controlPointNum = 0, int callingCountOfTheMethod = -1, [CallerMemberName] string currentMethodName = "")
        {
            bool isTestInProgress = _test.FetchIsTestInProgress();
            if (isTestInProgress)
            {
                TestReport.TestReportStage sendingTestTimingReportStage = new TestReport.TestReportStage()
                {
                    ChainSerialNumber = currentChainSerialNum,
                    TsWork = currentWorkStopwatch,
                    MethodNameWhichCalled = currentMethodName,
                    WorkActionNum = workActionNum,
                    WorkActionVal = workActionVal,
                    WorkActionName = workActionName,
                    ControlPointNum = controlPointNum,
                    CallingCountOfWorkMethod = callingCountOfTheMethod
                };
                _ = _report.AddStageToTestTaskProgressReport(constantsSet, sendingTestTimingReportStage);
            }
            return isTestInProgress;
        }

        public bool HandlerCallingsDistributor(ConstantsSet constantsSet, int currentChainSerialNum, CancellationToken stoppingToken)
        {
            _currentChainSerialNum = currentChainSerialNum;

            // добавить счётчик потоков и проверить при большом количестве вызовов
            // (это надо быстро (без задержек) сформировать две группы по 6 книг всего 6 пар)
            int lastCountStart = Interlocked.Increment(ref _callingNumOfHandlerCallingsDistributor);
            Logs.Here().Information("HandlerCallingsDistributor started {0} time.", lastCountStart);

            //int count = Volatile.Read(ref _callingNumOfHandlerCallingsDistributor);

            int controlPointNum1 = 1;
            _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), - 1, false, "", controlPointNum1, lastCountStart);
            _ = HandlerCallings(constantsSet, currentChainSerialNum, stoppingToken);

            int lastCountEnd = Interlocked.Decrement(ref _callingNumOfHandlerCallingsDistributor);
            Logs.Here().Information("HandlerCallingsDistributor ended {0} time.", lastCountEnd);

            return true;
        }

        // можно перенести во вспомогательную библиотеку
        public string FetchCurrentMethodName(bool showLogMethodStarted = false, [CallerMemberName] string currentMethodName = "")
        {
            if (showLogMethodStarted)
            {
                Logs.Here().Debug("{0} started.", currentMethodName);
            }
            return currentMethodName;
        }

        public async Task<int> HandlerCallings(ConstantsSet constantsSet, int currentChainSerialNum, CancellationToken stoppingToken)
        {
            // обработчик вызовов - что делает (надо переименовать - не вызовов, а событий подписки или как-то так)
            // получает сообщение о сформированном вызове по поводу subscribeOnFrom
            // собирает из subscribeOnFrom все данные и формирует пакет задач с плоским текстом (формирование пакета можно отдать в следующий метод)
            // удаляет обработанные поля subscribeOnFrom
            // проверяет наличие ключа subscribeOnFrom, если остался, ещё раз достаёт поля - и далее по кругу, пока ключ не исчезнет

            // решить, как лучше - ждать, когда ключ исчезнет и только потом формировать пакет задач или формировать новый пакет на каждом круге
            // первый вариант - пакет на каждом круге
            // а если ключ не исчез, подождать стандартные 5 секунд - скорее всего, новые поля заберёт следующие поток

            // вообще, в этом обработчике надо только достать список полей и сразу же удалить поля по списку
            // только успешно удалённые поля будут считаться полученными и пригодными для дальнейшей обработки
            // потом отдать данные в следующий метод

            // те поля, которые не получилось удалить (сами исчезли) надо сложить в отдельный список
            // и отдать в специальный метод - он за ними присмотрит

            // по кругу пока не ходить - в тестах проверить, захватываются ли все вызовы или что-то пропадает

            // вообще-то это всё - разместить пакет задач для бэк-сервера и дальше только контролировать выполнение

            // получить в строку название метода, чтобы сообщить тесту
            //string currentMethodName = FetchCurrentMethodName();
            //Logs.Here().Information("{0} started.", currentMethodName);

            // достать ключ и поля (List) плоских текстов из события подписки subscribeOnFrom
            (List<string> fieldsKeyFromDataList, string sourceKeyWithPlainTexts) = await ProcessDataOfSubscribeOnFrom(constantsSet, currentChainSerialNum, stoppingToken);
            int controlPointNum1 = 1;
            _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), - 1, false, sourceKeyWithPlainTexts, controlPointNum1, -1);

            // ключ пакета задач (новый гуид) и складываем тексты в новый ключ
            string taskPackageGuid = await _collect.CreateTaskPackageAndSaveLog(constantsSet, currentChainSerialNum, sourceKeyWithPlainTexts, fieldsKeyFromDataList);
            int controlPointNum2 = 2;
            _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), - 1, false, taskPackageGuid, controlPointNum2, -1);

            // вот тут, если вернётся null, то можно пройти сразу на выход и ничего не создавать - 
            if (taskPackageGuid != "")
            {
                // вот тут подходяще проверить/вызвать тест, отдать ему ключ пакета и пусть сравнивает с тем, что он отдавал на тест
                // подписка на кафе достаёт словарь и отдаёт его тому же методу
                // не надо проверять тест или нет, тест сам разберётся

                // здесь может быть нужна небольшая задержка, чтобы тест уверенно успел считать пакет задач
                // (проверить, его удалят сразу, как схватят или нет)

                // записываем ключ пакета задач в ключ eventKeyFrontGivesTask
                bool isCafeKeyCreated = await DistributeTaskPackageInCafee(constantsSet, currentChainSerialNum, taskPackageGuid);
                int controlPointNum3 = 3;
                _ = AddStageToProgressReport(constantsSet, currentChainSerialNum , _test.FetchWorkStopwatch(), - 1, isCafeKeyCreated, "isCafeKeyCreated", controlPointNum3, -1);

                //if (isCafeKeyCreated) // && test is processing now
                //{
                //    // вызвать метод теста для сообщения об окончании выполнения
                //}
            }
            // никакого возврата никто не ждёт, но на всякий случай вернём ?
            return 0;
        }

        private async Task<(List<string>, string)> ProcessDataOfSubscribeOnFrom(ConstantsSet constantsSet, int currentChainSerialNum, CancellationToken stoppingToken)
        {
            // название (назначение) метода - достать ключ и поля плоских текстов из события подписки subscribeOnFrom

            // если тест, надо смотреть тестовый ключ, а не рабочий
            // теперь ключ всегда одинаковый - рабочий
            // убрать все присваивания в отдельный метод, чтобы не путаться (уже одно осталось, нечего убирать)
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;

            IDictionary<string, string> keyFromDataList = await _cache.FetchHashedAllAsync<string>(eventKeyFrom);
            int keyFromDataListCount = keyFromDataList.Count;
            List<string> fieldsKeyFromDataList = new();
            string sourceKeyWithPlainTests = null;

            foreach (var d in keyFromDataList)
            {
                (var f, var v) = d;
                Logs.Here().Debug("Dictionary element is {@F} {@V}.", new { Filed = f }, new { Value = v });

                // удаляем текущее поле (для точности и скорости перед удалением можно проверить существование? и, если есть, то удалять)
                bool isFieldRemovedSuccessful = await _cache.DelFieldAsync(eventKeyFrom, f);
                Logs.Here().Debug("{@F} in {@K} was removed with result {0}.", new { Filed = f }, new { Key = eventKeyFrom }, isFieldRemovedSuccessful);

                // если не удалилось - и фиг с ним, удаляем его из словаря
                // можно убрать, всё равно словарь больше не используется
                //if (!isFieldRemovedSuccessful)
                //{
                //    keyFromDataList.Remove(f);
                //    // а вот не фиг - тут сохраняем его куда-то (потом)
                //}
                // если удалилось, то переписываем в лист
                // кстати, из словаря тогда можно не удалять - он уже никому неинтересен
                // и инвертировать логику и без else
                if (isFieldRemovedSuccessful)
                {
                    fieldsKeyFromDataList.Add(f);
                    // можно каждый раз проверять, что ключ одинаковый - если больше нечего делать (должен быть всегда одинаковый)
                    sourceKeyWithPlainTests = v;
                    Logs.Here().Debug("Future {@K} with {@F} with plain text.", new { Key = v }, new { Filed = f });
                }
            }

            int fieldsKeyFromDataListCount = fieldsKeyFromDataList.Count;
            Logs.Here().Information("{0} tasks were found, {1} tasks were proceeded.", keyFromDataListCount, fieldsKeyFromDataListCount);

            return (fieldsKeyFromDataList, sourceKeyWithPlainTests);
        }

        private async Task<bool> DistributeTaskPackageInCafee(ConstantsSet constantsSet, int currentChainSerialNum, string taskPackageGuid)
        {
            // только после того, как создан ключ с пакетом задач, можно положить этот ключ в подписной ключ eventKeyFrontGivesTask
            // записываем ключ пакета задач в ключ eventKeyFrontGivesTask, а в поле и в значение - ключ пакета задач
            // сервера подписаны на ключ eventKeyFrontGivesTask и пойдут забирать задачи, на этом тут всё
            // сделать подписку на ключ кафе и по событию пакет в кафе сообщать тестам -
            // вызывать финальный метод проверки результатов теста (не отсюда вызывать, а по подписке)
            // в дальнейшем будет частью постоянной самопроверки
            // возвращать true из метода DistributeTaskPackageInCafee только после проверки этим постоянным тестом
            // только надо как-то узнать, какие результаты у этого теста - можно вызвать метод из класса теста, который проверит какой-нибудь флаг
            // или более приземлённый способ с проверкой ключа (с флагом выглядит лучше - там сразу можно и подождать положенное время)

            // и подписка на ключ кафе - дело шаткое, бэк-сервер быстро схватит этот ключ и удалит
            // надо или делать задержку на сервере или вызывать тест не по подписке
            // точнее, сначала не по подписке, а потом проконтролировать появление и исчезновение ключа кафе
            // ну и далее регистрацию пакета на бэк-сервере

            string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package
            double cafeKeyLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.LifeTime;

            await _cache.WriteHashedAsync(cafeKey, taskPackageGuid, taskPackageGuid, cafeKeyLifeTime);
            Logs.Here().Information("{@T} was placed in {@C}.", new { Task = taskPackageGuid }, new { Cafe = cafeKey });

            return true;
        }
    }
}
