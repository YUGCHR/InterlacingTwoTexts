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

namespace BackgroundDispatcher.Services
{
    public interface IFormTaskPackageFromPlainText
    {
        public Task<bool> HandlerCallingsDistributor(ConstantsSet constantsSet, CancellationToken stoppingToken);
    }

    public class FormTaskPackageFromPlainText : IFormTaskPackageFromPlainText
    {
        private readonly ILogger<FormTaskPackageFromPlainText> _logger;
        private readonly IIntegrationTestService _test;
        private readonly ICacheManageService _cache;

        public FormTaskPackageFromPlainText(
            ILogger<FormTaskPackageFromPlainText> logger,
            IIntegrationTestService test,
            ICacheManageService cache)
        {
            _logger = logger;
            _test = test;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<FormTaskPackageFromPlainText>();

        public async Task<bool> HandlerCallingsDistributor(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // добавить счётчик потоков и проверить при большом количестве вызовов
            // 

            // получить в строку название метода, чтобы сообщить тесту
            string currentMethodName = FetchCurrentMethodName();
            Logs.Here().Information("{0} started.", currentMethodName);

            // можно добавить задержку для тестирования

            // уже обработанное поле сразу удалить, чтобы не накапливались

            // в случае теста проверяем, достигнута ли глубина тестирования и заодно сообщаем о ходе теста - достигнутой контрольной точки
            // можно перенести отчёт о тестировании в следующий метод и сделать только одну глубину - окончательную
            bool isTestInProgress = _test.IsTestInProgress();
            if (isTestInProgress)
            {
                // сообщаем тесту, что глубина достигнута и проверяем, идти ли дальше
                // если дальше идти не надо, то return прямо здесь
                // передаем в параметрах название метода, чтобы там определили, из какого места вызвали
                // название метода из переменной - currentMethodName
                // инвертировать возврат и переименовать переменную результата в targetDepthReached
                bool targetDepthNotReached = await _test.IsPreassignedDepthReached(constantsSet, "HandlerCallingDistributore", stoppingToken);
                Logs.Here().Information("Test reached HandlerCallingDistributor and will move on - {0}.", targetDepthNotReached);
                if (!targetDepthNotReached)
                {
                    return true;
                }
            }
            // тут еще можно определить, надо ли обновить константы
            // хотя константы лучше проверять дальше
            // тут быстрый вызов без ожидания, чтобы быстрее освободить распределитель для второго потока
            // в тестировании проверить запуск второго потока - и добавить счётчик потоков в обработчик

            _ = HandlerCallings(constantsSet, stoppingToken);

            Logs.Here().Information("{0} is returned true.", currentMethodName);
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

        public async Task<int> HandlerCallings(ConstantsSet constantsSet, CancellationToken stoppingToken)
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

            string currentMethodName = FetchCurrentMethodName();
            Logs.Here().Information("{0} started.", currentMethodName);

            // достать ключ и поля (List) плоских текстов из события подписки subscribeOnFrom
            (List<string> fieldsKeyFromDataList, string sourceKeyWithPlainTexts) = await ProcessDataOfSubscribeOnFrom(constantsSet, stoppingToken);

            // ключ пакета задач (новый гуид) и складываем тексты в новый ключ
            string taskPackageGuid = await _test.CreateTaskPackage(constantsSet, sourceKeyWithPlainTexts, fieldsKeyFromDataList);
            // вот тут, если вернётся null, то можно пройти сразу на выход и ничего не создавать - 
            if (taskPackageGuid != null)
            {
                // записываем ключ пакета задач в ключ eventKeyFrontGivesTask
                bool isCafeKeyCreated = await DistributeTaskPackageInCafee(constantsSet, taskPackageGuid);

                if (isCafeKeyCreated) // && test is processing now
                {
                    // вызвать метод теста для сообщения об окончании выполнения
                }
            }
            // никакого возврата никто не ждёт, но на всякий случай вернём ?
            return 0;
        }

        private async Task<(List<string>, string)> ProcessDataOfSubscribeOnFrom(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // название (назначение) метода - достать ключ и поля плоских текстов из события подписки subscribeOnFrom

            // если тест, надо смотреть тестовый ключ, а не рабочий
            // теперь ключ всегда одинаковый - рабочий
            // убрать все присваивания в отдельный метод, чтобы не путаться (уже одно осталось, нечего убирать)
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            //string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            //string eventKeyFromTest = $"{eventKeyFrom}:{eventKeyTest}"; // subscribeOnFrom:test
            //string eventKey = eventKeyFrom;

            //bool isTestInProgress = _test.IsTestInProgress();
            //if (isTestInProgress)
            //{
            //    eventKey = eventKeyFromTest; // subscribeOnFrom:test
            //}

            IDictionary<string, string> keyFromDataList = await _cache.FetchHashedAllAsync<string>(eventKeyFrom);
            int keyFromDataListCount = keyFromDataList.Count;
            List<string> fieldsKeyFromDataList = new();
            string sourceKeyWithPlainTests = null;

            foreach (var d in keyFromDataList)
            {
                (var f, var v) = d;
                Logs.Here().Information("Dictionary element is {@F} {@V}.", new { Filed = f }, new { Value = v });

                // удаляем текущее поле (для точности и скорости перед удалением можно проверить существование? и, если есть, то удалять)
                bool isFieldRemovedSuccessful = await _cache.DelFieldAsync(eventKeyFrom, f);
                Logs.Here().Information("{@F} in {@K} was removed with result {0}.", new { Filed = f }, new { Key = eventKeyFrom });

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
                    // можно каждый раз проверять, что ключ одинаковый - если больше нечего делать
                    sourceKeyWithPlainTests = v;
                    Logs.Here().Information("Future {@K} with {@F} with plain text.", new { Key = v }, new { Filed = f });
                }
            }

            int fieldsKeyFromDataListCount = fieldsKeyFromDataList.Count;
            Logs.Here().Information("{0} tasks were found, {1} tasks were proceeded.", keyFromDataListCount, fieldsKeyFromDataListCount);

            return (fieldsKeyFromDataList, sourceKeyWithPlainTests);
        }

        private async Task<bool> DistributeTaskPackageInCafee(ConstantsSet constantsSet, string taskPackageGuid)
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
