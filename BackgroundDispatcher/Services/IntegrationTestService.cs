using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BackgroundDispatcher.Services
{
    public interface IIntegrationTestService
    {
        public Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public void SetIsTestInProgress(bool init_isTestInProgress);
        public bool IsTestInProgress();
        public Task<bool> Depth_HandlerCallingDistributore_Reached(ConstantsSet constantsSet, CancellationToken stoppingToken);
    }

    public class IntegrationTestService : IIntegrationTestService
    {
        private readonly ICacheManageService _cache;

        public IntegrationTestService(ICacheManageService cache)
        {
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<IntegrationTestService>();

        private bool _isTestInProgress;

        public async Task<bool> Depth_HandlerCallingDistributore_Reached(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // создаем сообщение об успешном тесте
            string testResultsKey1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.Value; // testResultsKey1
            double testResultsKey1LifeTime = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.LifeTime;
            string testResultsField1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField1.Value; // testResultsField1
            int resultTest1Passed = constantsSet.IntegerConstant.IntegrationTestConstant.ResultTest1Passed.Value; // 1

            // этот ключ можно использовать как счетчик вызовов обработчика (но лучше поле)
            await _cache.WriteHashedAsync<int>(testResultsKey1, testResultsField1, resultTest1Passed, testResultsKey1LifeTime);

            string testSettingKey1 = "testSettingKey1";
            string testSettingField1 = "f1"; // test depth
            string test1Depth = "HandlerCallingDistributore"; // other values - in constants
            string targetTest1Depth = await _cache.FetchHashedAsync<string>(testSettingKey1, testSettingField1);
            bool keyWasDeleted = await _cache.DelFieldAsync(testResultsKey1, testSettingField1);

            // this method result is returned to variable <bool> targetDepthNotReached
            return test1Depth != targetTest1Depth;
        }

        public void SetIsTestInProgress(bool init_isTestInProgress)
        {
            _isTestInProgress = init_isTestInProgress;
        }

        public bool IsTestInProgress()
        {
            return _isTestInProgress;
        }

        public async Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // написать сценарии тестирования и на разные глубины
            // управлять глубиной можно тоже по ключу
            // и в рабочем варианте отключить тестирование одной константой

            Logs.Here().Information("Integration test was started.");
            // поле - отражение такого же поля в классе подписок, формально они не связаны, но по логике меняются вместе
            _isTestInProgress = true;

            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            int countDecisionMaking = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountDecisionMaking.Value; // 6
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value;

            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventFileldTest = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test

            string testSettingKey1 = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.Value; // testSettingKey1
            double testSettingKey1LifeTime = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.LifeTime;
            bool testSettingKey1WasDeleted = await _cache.DeleteKeyIfCancelled(testSettingKey1);
            if (testSettingKey1WasDeleted)
            {
                Logs.Here().Information("Key {0} was deleted successfully.", testSettingKey1);
            }

            string testSettingField1 = constantsSet.Prefix.IntegrationTestPrefix.SettingField1.Value; // f1 (test depth)
            string test1Depth = constantsSet.Prefix.IntegrationTestPrefix.DepthValue1.Value; // HandlerCallingDistributore
            // при дальнейшем углублении теста показывать этапы прохождения
            await _cache.WriteHashedAsync<string>(testSettingKey1, testSettingField1, test1Depth, testSettingKey1LifeTime);

            //string testSettingField2 = "f2"; // 
            //string testSettingField3 = "f3"; //

            string testResultsKey1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.Value; // testResultsKey1
            string testResultsField1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField1.Value; // testResultsField1
            int resultTest1Passed = constantsSet.IntegerConstant.IntegrationTestConstant.ResultTest1Passed.Value; // 1

            bool testResultsKey1WasDeleted = await _cache.DeleteKeyIfCancelled(testResultsKey1);

            if (testResultsKey1WasDeleted)
            {
                Logs.Here().Information("Key {0} was deleted successfully.", testResultsKey1);
            }

            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom lifeTime
            string eventKeyFromTest = $"{eventKeyFrom}:{eventKeyTest}";

            // сделать тестовые книги для загрузки
            string eventFiledFromTest = $"{eventKeyFrom}:{eventKeyTest}"; // field to load plain texts
            string eventValueFromTest = $""; // key to load plain texts

            // можно сделать сценарии в виде листа и вызов конкретного по индексу
            // собирать константы в лист лучше уже в классе теста
            // или в интерфейсе выбора сценария показывать названия полей, а потом брать их значение для вызова теста
            // test scenario selection 
            int testScenario = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

            //int setting2 = await _cache.FetchHashedAsync<int>(testSettingKey1, testSettingField2);
            //int setting3 = await _cache.FetchHashedAsync<int>(testSettingKey1, testSettingField3);

            int testScenario1 = constantsSet.IntegerConstant.IntegrationTestConstant.TestScenario1.Value;
            string testScenario1description = constantsSet.IntegerConstant.IntegrationTestConstant.TestScenario1.Description;

            if (testScenario == testScenario1)
            {
                Logs.Here().Information("Test scenario {0} was selected and started.", testScenario);

                for (int i = 0; i < countTrackingStart; i++)
                {
                    Logs.Here().Information("Event From was created {0} time(s).", i + 1);

                    // запись данных книги в фальшивый (тестовый) ключ обработки плоского текста
                    await _cache.WriteHashedAsync<string>(eventKeyFromTest, "count", Math.Abs(i).ToString(), eventKeyFromTestLifeTime);
                }

                Logs.Here().Information("Test scenario {0} ({1}) was started and is waited the results.", testScenario, testScenario1description);

                int delayTimeForTest1 = constantsSet.IntegerConstant.IntegrationTestConstant.DelayTimeForTest1.Value; // 1000
                bool isTestResultAppeared = false;
                while (!isTestResultAppeared)
                {
                    await Task.Delay(delayTimeForTest1);
                    Logs.Here().Information("Test scenario {0} results are still waiting.", testScenario);
                    isTestResultAppeared = await _cache.IsKeyExist(testResultsKey1);
                }

                Logs.Here().Information("Test scenario {0} finished and the results will come soon.", testScenario);

                // в выходном (окончательном) сообщении указывать глубину теста

                int testResult = await _cache.FetchHashedAsync<int>(testResultsKey1, testResultsField1);

                // удалили ключ запуска теста, в дальнейшем - если полем запуска будет определяться глубина, то удалять только поле
                // но лучше из веб-интерфейса загружать в значение сложный класс - сразу и сценарий и глубину (и ещё что-то)
                bool eventKeyTestWasDeleted = await _cache.DeleteKeyIfCancelled(eventKeyTest);

                if (eventKeyTestWasDeleted && testResult == resultTest1Passed)
                {
                    char separatorUnit = '+';
                    string successTextMessage = $"Test scenario <{testScenario1description}> passed successfully";
                    (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separatorUnit, successTextMessage);
                    Logs.Here().Information("{0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
                }
                else
                {
                    Logs.Here().Warning("Test scenario {0} FAILED.", testScenario);
                }
            }
            // возвращаем состояние _isTestInProgress - тест больше не выполняется
            _isTestInProgress = false;
            return _isTestInProgress;
        }
    }
}
