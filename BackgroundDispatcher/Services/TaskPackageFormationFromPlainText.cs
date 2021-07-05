using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

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
            Logs.Here().Information("HandlerCallingDistributor started.");
            // можно добавить задержку для тестирования

            // сходим в тесты, узнаем, они это сейчас или не они
            // если они не вызывались, может быть не определено, проверить на null и присвоить false, если что
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
                targetDepthNotReached = await _test.Depth_HandlerCallingDistributore_Reached(constantsSet, stoppingToken);
                Logs.Here().Information("Test reached HandlerCallingDistributor and will {0} move on.", targetDepthNotReached);
            }

            // если глубина текста не достигнута, то идём дальше по цепочке вызовов
            // только как идти дальше при штатной работе, без теста?
            // можно добавить переменную workOrTest, true - Work, false - Test и поставить первой в условие с ИЛИ
            
            bool isWorkInProgress = !isTestInProgress;
            // должно быть true - если работа, а не тест или тест, но глубина теста не достигнута
            // должно быть false - если текст, а не работа и глубина теста достигнута (тогда обработчик вызовов не надо вызывать)
            if (isWorkInProgress || targetDepthNotReached)
            {
                // тут еще можно определить, надо ли обновить константы
                // хотя константы лучше проверять дальше
                // тут быстрый вызов без ожидания, чтобы быстрее освободить распределитель для второго потока
                // в тестировании проверить запуск второго потока - и добавить счётчик потоков в обработчик
                _ = HandlerCalling(constantsSet, stoppingToken);
            }

            Logs.Here().Information("HandlerCallingDistributor will return true.");
            return true;
        }

        public async Task<int> HandlerCalling(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            int tasksPackagesCount = await FetchBookPlainText(constantsSet.EventKeyFrom.Value, constantsSet.EventFieldFrom.Value);

            // начинаем цикл создания и размещения пакетов задач
            //_logger.LogInformation(30010, " - Creation cycle of key EventKeyFrontGivesTask fields started with {1} steps.", tasksPackagesCount);
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
                int inPackageTaskCount = await FrontServerSetTasks(constantsSet, taskPackage, taskPackagePrefixGuid);
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

        private async Task<int> FrontServerSetTasks(ConstantsSet constantsSet, Dictionary<string, TaskDescriptionAndProgress> taskPackage, string taskPackageGuid)
        {
            int inPackageTaskCount = 0;
            foreach (KeyValuePair<string, TaskDescriptionAndProgress> t in taskPackage)
            {
                (string guid, TaskDescriptionAndProgress cycleCount) = t;
                // записываем пакет задач в ключ пакета задач
                // потом здесь записывать в значение класс с условием и ходом выполнения задач
                // или условия и выполнение это разные ключи (префиксы)?
                await _cache.WriteHashedAsync(taskPackageGuid, guid, cycleCount, constantsSet.EventKeyFrom.LifeTime);
                inPackageTaskCount++;
                _logger.LogInformation(30050, "TaskPackage No. {0}, with Task No. {1} with {2} cycles was set.", taskPackageGuid, guid, cycleCount);
            }

            // только после того, как создан ключ с пакетом задач, можно положить этот ключ в подписной ключ eventKeyFrontGivesTask
            // записываем ключ пакета задач в ключ eventKeyFrontGivesTask, а в поле и в значение - ключ пакета задач

            await _cache.WriteHashedAsync(constantsSet.EventKeyFrontGivesTask.Value, taskPackageGuid, taskPackageGuid, constantsSet.EventKeyFrontGivesTask.LifeTime);
            // сервера подписаны на ключ eventKeyFrontGivesTask и пойдут забирать задачи, на этом тут всё
            return inPackageTaskCount;
        }
    }
}
