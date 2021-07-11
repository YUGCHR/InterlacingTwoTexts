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

// план работ -
// организовать тестовый ключ с плоским текстом и тестировать следующий этап
// 

namespace BackgroundDispatcher.Services
{
    public interface ITaskPackageFormationFromPlainText
    {
        public Task FrontServerEmulationCreateGuidField(string eventKeyRun, string eventFieldRun, double ttl);
        public Task<bool> HandlerCallingDistributore(ConstantsSet constantsSet, CancellationToken stoppingToken);
    }

    public class TaskPackageFormationFromPlainText : ITaskPackageFormationFromPlainText
    {
        private readonly ILogger<TaskPackageFormationFromPlainText> _logger;
        private readonly IIntegrationTestService _test;
        private readonly ICacheManageService _cache;

        public TaskPackageFormationFromPlainText(
            ILogger<TaskPackageFormationFromPlainText> logger,
            IIntegrationTestService test,
            ICacheManageService cache)
        {
            _logger = logger;
            _test = test;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TaskPackageFormationFromPlainText>();

        public async Task FrontServerEmulationCreateGuidField(string eventKeyRun, string eventFieldRun, double ttl) // not used
        {
            string eventGuidFieldRun = Guid.NewGuid().ToString(); // 

            await _cache.WriteHashedAsync<string>(eventKeyRun, eventFieldRun, eventGuidFieldRun, ttl); // создаём ключ ("task:run"), на который подписана очередь и в значении передаём имя ключа, содержащего пакет задач

            Logs.Here().Information("Guid Field {0} for key {1} was created and set.\n", eventGuidFieldRun, eventKeyRun);
        }

        public async Task<bool> HandlerCallingDistributore(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // добавить счётчик потоков и проверить при большом количестве вызовов
            // 

            // получить в строку название метода, чтобы сообщить тесту
            string currentMethodName = FetchCurrentMethodName();
            Logs.Here().Information("{0} started.", currentMethodName);

            // можно добавить задержку для тестирования

            // сходим в тесты, узнаем, они это сейчас или не они
            // если они не вызывались, может быть не определено, проверить на null и присвоить false, если что
            // уже всё заранее инициализировано
            bool isTestInProgress = _test.IsTestInProgress();

            // CollectSourceDataAndCreateTaskPackageForBackgroundProcessing
            // TakeBookTextAndCreateTask
            // TaskPackageFormationFromPlainText
            // получаем условия задач по стартовому ключу
            // записываем то же самое поле в ключ subscribeOnFrom, а в значение (везде одинаковое) - ключ всех исходников книг
            // на стороне диспетчера всё достать в словарь и найти новое (если приедет много сразу из нескольких клиентов)
            // уже обработанное поле сразу удалить, чтобы не накапливались

            bool targetDepthNotReached = true;
            // спрятать под if
            if (isTestInProgress)
            {
                // сообщаем тесту, что глубина достигнута и проверяем, идти ли дальше
                // передаем в параметрах название метода, чтобы там определили, из какого места вызвали
                //string test1Depth1 = "HandlerCallingDistributore"; // other values - in constants
                //string test1Depth2 = "DistributeTaskPackageInCafee";
                targetDepthNotReached = await _test.IsPreassignedDepthReached(constantsSet, "HandlerCallingDistributore", stoppingToken); // currentMethodName
                Logs.Here().Information("Test reached HandlerCallingDistributor and will {0} move on.", targetDepthNotReached);
            }

            // если глубина текста не достигнута, то идём дальше по цепочке вызовов
            // только как идти дальше при штатной работе, без теста?
            // можно добавить переменную workOrTest, true - Work, false - Test и поставить первой в условие с ИЛИ

            bool isWorkInProgress = !isTestInProgress;
            // должно быть true - если работа, а не тест или тест, но глубина теста не достигнута
            // должно быть false - если текст, а не работа и глубина теста достигнута (тогда обработчик вызовов не надо вызывать)
            bool nextMethodWasCalled;
            if (isWorkInProgress || targetDepthNotReached || true) // true - temporary ()
            {
                // надо как-то возвращать true на предыдущие точки глубин
                // например, можно несколько полей глубины и поля номерные по порядку

                // тут еще можно определить, надо ли обновить константы
                // хотя константы лучше проверять дальше
                // тут быстрый вызов без ожидания, чтобы быстрее освободить распределитель для второго потока
                // в тестировании проверить запуск второго потока - и добавить счётчик потоков в обработчик
                nextMethodWasCalled = true;
                _ = HandlerCalling(constantsSet, stoppingToken);
            }

            Logs.Here().Information("{0} is returned {1}.", currentMethodName, nextMethodWasCalled);
            return nextMethodWasCalled;
        }

        public string FetchCurrentMethodName([CallerMemberName] string currentMethodName = "")
        {
            return currentMethodName;
        }

        public async Task<int> HandlerCalling(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // обработчик вызовов - что делает
            // получает сообщение о сформированном вызове по поводу subscribeOnFrom
            // собирает из subscribeOnFrom все данные и формирует пакет задач с плоским текстом (формирование пакета можно отдать в следующий метод)
            // удаляет обработанные поля subscribeOnFrom
            // проверяет наличие ключа subscribeOnFrom, если остался, ещё раз достаёт поля - и далее по кругу, пока ключ не исчезнет
            // 
            // решить, как лучше - ждать, когда ключ исчезнет и только потом формировать пакет задач или формировать новый пакет на каждом круге
            // первый вариант - пакет на каждом круге
            // а если ключ не исчез, подождать стандартные 5 секунд - скорее всего, новые поля заберёт следующие поток
            // 
            // вообще, в этом обработчике надо только достать список полей и сразу же удалить поля по списку
            // только успешно удалённые поля будут считаться полученными и пригодными для дальнейшей обработки
            // потом отдать данные в следующий метод
            // те поля, которые не получилось удалить (сами исчезли) надо сложить в отдельный список
            // и отдать в специальный метод - он за ними присмотрит
            //
            // по кругу пока не ходить - в тестах проверить, захватываются ли все вызовы или что-то пропадает
            // 
            // вообще-то это всё - разместить пакет задач для бэк-сервера и дальше только контролировать выполнение

            string currentMethodName = FetchCurrentMethodName();
            Logs.Here().Information("{0} started.", currentMethodName);

            // если тест, надо смотреть тестовый ключ, а не рабочий
            // убрать все присваивания в отдельный метод, чтобы не путаться
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventKeyFromTest = $"{eventKeyFrom}:{eventKeyTest}"; // subscribeOnFrom:test
            string eventKey = eventKeyFrom;

            bool isTestInProgress = _test.IsTestInProgress();
            if (isTestInProgress)
            {
                eventKey = eventKeyFromTest; // subscribeOnFrom:test
            }
            
            IDictionary<string, string> keyFromDataList = await _cache.FetchHashedAllAsync<string>(eventKey);

            List<string> fieldsKeyFromDataList = new();
            string sourceKeyWithPlainTests = null;

            foreach (var d in keyFromDataList)
            {
                (var f, var v) = d;
                Logs.Here().Information("Dictionary element is {@F} {@V}.", new { Filed = f }, new { Value = v });

                // удаляем текущее поле (для точности и скорости перед удалением можно проверить существование? и, если есть, то удалять)
                bool isFieldRemovedSuccessful = await _cache.DelFieldAsync(eventKey, f);
                Logs.Here().Information("{@F} in {@K} was removed with result {0}.", new { Filed = f }, new { Key = eventKey });

                // если не удалилось - и фиг с ним, удаляем его из словаря
                if (!isFieldRemovedSuccessful)
                {
                    keyFromDataList.Remove(f);
                    // а вот не фиг - тут сохраняем его куда-то (потом)
                }
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

            // получили заверенный словарь с полями и ключом (в значении), отдать в следующий метод

            // что интересно, словарь тут уже не нужен, а достаточно простого списка (List) полей
            // ключ везде одинаковый и его можно передать строкой

            int result = await BackgroundDispatcherCreateTasks(constantsSet, sourceKeyWithPlainTests, fieldsKeyFromDataList);

            // никакого возврата никто не ждёт, но на всякий случай вернём количество полученных полей
            return keyFromDataList.Count;
        }

        private async Task<int> BackgroundDispatcherCreateTasks(ConstantsSet constantsSet, string sourceKeyWithPlainTests, List<string> taskPackageFileds)
        {
            // получили ключ-гуид и список полей, по сути, это уже готовый пакет
            // сам ключ уже сформирован и ждёт - можно получить плоские тесты
            // похоже, ключ лучше бы заменить - потому что бэк-сервер будет вычерпывать ключ весь
            // а он там постоянный на все время сессии BooksTextsSplit
            // а уникальный ключ текущего запроса за сохранение книги - он в поле
            // надо уточнить, как там с языковой парой - у них одинаковый ключ или разный
            // каждая книга заезжает отдельно, не парой и имеет уникальный гуид, созданный контроллером в момент отправки книги
            // ключ общий у всех книг, можно было бы заменить его на уникальный
            // но это всё равно ничего не даёт - книги (тексты) придётся переписывать в пакет задач в любом варианте
            // так что получили не готовый пакет, а только заготовку
            // план действий метода -
            // генерируем новый гуид - это будет ключ пакета задач
            // достаём по одному тексты и складываем в новый ключ
            // гуид пакета отдаём в следующий метод

            if (sourceKeyWithPlainTests == null)
            {
                _test.SomethingWentWrong(false);
                return -1;
            }

            string taskPackage = constantsSet.Prefix.BackgroundDispatcherPrefix.TaskPackage.Value; // taskPackage
            double taskPackageGuidLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.TaskPackage.LifeTime; // 0.001
            string currentPackageGuid = Guid.NewGuid().ToString();
            string taskPackageGuid = $"{taskPackage}:{currentPackageGuid}"; // taskPackage:guid

            //List<bool> resultPlainText = new();
            int inPackageTaskCount = 0;

            foreach (var f in taskPackageFileds)
            {
                inPackageTaskCount++;

                // прочитать первое поле хранилища
                TextSentence bookPlainText = await _cache.FetchHashedAsync<TextSentence>(sourceKeyWithPlainTests, f);
                Logs.Here().Information("Test plain text was read from key-storage");

                // создать поле плоского текста
                await _cache.WriteHashedAsync<TextSentence>(taskPackageGuid, f, bookPlainText, taskPackageGuidLifeTime);

                Logs.Here().Information("Plain text {@F} No. {0} was created in {@K}.", new { Filed = f }, inPackageTaskCount, new { Key = taskPackageGuid });
            }

            await DistributeTaskPackageInCafee(constantsSet, taskPackageGuid);

            return inPackageTaskCount;
        }

        private async Task<bool> DistributeTaskPackageInCafee(ConstantsSet constantsSet, string taskPackageGuid)
        {
            // только после того, как создан ключ с пакетом задач, можно положить этот ключ в подписной ключ eventKeyFrontGivesTask
            // записываем ключ пакета задач в ключ eventKeyFrontGivesTask, а в поле и в значение - ключ пакета задач
            // сервера подписаны на ключ eventKeyFrontGivesTask и пойдут забирать задачи, на этом тут всё

            string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package
            double cafeKeyLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.LifeTime;
            
            await _cache.WriteHashedAsync(cafeKey, taskPackageGuid, taskPackageGuid, cafeKeyLifeTime);
            Logs.Here().Information("{@T} was placed in {@C}.", new { Task = taskPackageGuid }, new { Cafe = cafeKey });

            return true;
        }









        // not used below

        public async Task<int> Ttttt(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {

            int tasksPackagesCount = await FetchBookPlainText(constantsSet.EventKeyFrom.Value, constantsSet.EventFieldFrom.Value);

            // начинаем цикл создания и размещения пакетов задач
            Logs.Here().Information(" - Creation cycle of key EventKeyFrontGivesTask fields started with {1} steps.\n", tasksPackagesCount);

            for (int i = 0; i < tasksPackagesCount; i++)
            {
                // guid - главный номер задания, используемый в дальнейшем для доступа к результатам
                string taskPackageGuid = Guid.NewGuid().ToString();
                int tasksCount = Math.Abs(taskPackageGuid.GetHashCode()) % 10; // просто (псевдо)случайное число
                if (tasksCount < 3)
                {
                    tasksCount += 3;
                }
                // задаём время выполнения цикла - как если бы получили его от контроллера
                // на самом деле для эмуляции пока берём из константы
                int taskDelayTimeSpanFromSeconds = constantsSet.TaskEmulatorDelayTimeInMilliseconds.Value;
                // создаём пакет задач (в реальности, опять же, пакет задач положил отдельный контроллер)
                Dictionary<string, TaskDescriptionAndProgress> taskPackage = FrontServerCreateTasks(constantsSet, tasksCount, taskDelayTimeSpanFromSeconds);

                // при создании пакета сначала создаётся пакет задач в ключе, а потом этот номер создаётся в виде поля в подписном ключе

                // создаем ключ taskPackageGuid и кладем в него пакет 
                // записываем ключ taskPackageGuid пакета задач в поле ключа eventKeyFrontGivesTask и в значение ключа - тоже taskPackageGuid
                // дополняем taskPackageGuid префиксом PrefixPackage
                string taskPackagePrefixGuid = $"{constantsSet.PrefixPackage.Value}:{taskPackageGuid}";
                //int inPackageTaskCount = await FrontServerSetTasks(constantsSet, taskPackage, taskPackagePrefixGuid);
                // можно возвращать количество созданных задач и проверять, что не нуль - но это чтобы хоть что-то проверять (или проверять наличие созданных ключей)
                // на создание ключа с пакетом задач уйдёт заметное время, поэтому промежуточный ключ оправдан (наверное)
            }
            return tasksPackagesCount;
        }

        private async Task<int> FetchBookPlainText(string eventKeyFrom, string eventFieldFrom) // CollectBookPlainText
        {
            // в словарь получаем список полей, а в значение везде одинаковое - ключ, где эти поля лежат (ключ из префикса BookTextFieldPrefix и bookTextSplit server Guid)
            // поле представляет собой префикс bookText:bookGuid: + bookGuid и хранит в значении плоский текст книги с полным описанием
            // eventKeyFrom, bookPlainText_FieldPrefix + BookGuid, bookPlainText_KeyPrefix + ServerGuid

            IDictionary<string, string> tasksCount = await _cache.FetchHashedAllAsync<string>(eventKeyFrom);

            // выгруженные поля сразу удалить

            Logs.Here().Information("FetchBookPlainText fetched all fields in {@D}.", new { Tasks = tasksCount });

            //_logger.LogInformation(30020, "TaskCount = {TasksCount} from key {Key} was fetched.", tasksCount, eventKeyFrom);

            return tasksCount.Count;
        }

        private Dictionary<string, TaskDescriptionAndProgress> FrontServerCreateTasks(ConstantsSet constantsSet, int tasksCount, int taskDelayTimeSpanFromSeconds)
        {
            Dictionary<string, TaskDescriptionAndProgress> taskPackage = new Dictionary<string, TaskDescriptionAndProgress>();

            for (int i = 0; i < tasksCount; i++)
            {
                string guid = Guid.NewGuid().ToString();

                // инициализовать весь класс отдельным методом
                // найти, как передать сюда TasksPackageGuid
                TaskDescriptionAndProgress descriptor = DescriptorInit(tasksCount, taskDelayTimeSpanFromSeconds, guid);

                int currentCycleCount = descriptor.TaskDescription.CycleCount;

                // дополняем taskPackageGuid префиксом PrefixPackage
                string taskPackagePrefixGuid = $"{constantsSet.PrefixTask.Value}:{guid}";
                taskPackage.Add(taskPackagePrefixGuid, descriptor);

                _logger.LogInformation(30030, "Task {I} from {TasksCount} with ID {Guid} and {CycleCount} cycles was added to Dictionary.", i, tasksCount, taskPackagePrefixGuid, currentCycleCount);
                //_logger.LogInformation(30033, "TaskDescriptionAndProgress descriptor TaskCompletedOnPercent = {0}.", descriptor.TaskState.TaskCompletedOnPercent);
            }
            return taskPackage;
        }

        private TaskDescriptionAndProgress DescriptorInit(int tasksCount, int taskDelayTimeSpanFromSeconds, string guid)
        {
            TaskDescriptionAndProgress.TaskComplicatedDescription cycleCount = new()
            {
                TaskGuid = guid,
                CycleCount = Math.Abs(guid.GetHashCode()) % 10, // получать 10 из констант
                TaskDelayTimeFromMilliSeconds = taskDelayTimeSpanFromSeconds
            };

            TaskDescriptionAndProgress.TaskProgressState init = new()
            {
                IsTaskRunning = false,
                TaskCompletedOnPercent = -1
            };

            // получать max (3) из констант
            if (cycleCount.CycleCount < 3)
            {
                cycleCount.CycleCount += 3;
            }

            TaskDescriptionAndProgress descriptor = new()
            {
                // передать сюда TasksPackageGuid
                TasksCountInPackage = tasksCount,
                TaskDescription = cycleCount,
                TaskState = init
            };

            return descriptor;
        }

    }
}
