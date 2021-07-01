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
        public Task IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);
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

        public async Task IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // написать сценарии тестирования и на разные глубины
            // управлять глубиной можно тоже по ключу
            // и в рабочем варианте отключить тестирование одной константой

            Logs.Here().Information("   ------------------ Integration test was started.");

            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            int countDecisionMaking = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountDecisionMaking.Value; // 6
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value;

            string testEventKey = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string testEventFileld = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test

            string testSettingKey1 = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.Value; // testSettingKey1
            double testSettingKey1LifeTime = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.LifeTime;
            bool testSettingKey1WasDeleted = await _cache.DeleteKeyIfCancelled(testSettingKey1);
            if (testSettingKey1WasDeleted)
            {
                Logs.Here().Information("Key {0} was deleted successfully.", testSettingKey1);
            }

            string testSettingField1 = constantsSet.Prefix.IntegrationTestPrefix.SettingField1.Value; // f1 (test depth)
            string test1Depth = constantsSet.Prefix.IntegrationTestPrefix.DepthValue1.Value; // HandlerCallingDistributore
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

            // test scenario selection 
            int testScenario = await _cache.FetchHashedAsync<int>(testEventKey, testEventFileld);

            //int setting2 = await _cache.FetchHashedAsync<int>(testSettingKey1, testSettingField2);
            //int setting3 = await _cache.FetchHashedAsync<int>(testSettingKey1, testSettingField3);

            if (testScenario == 1)
            {
                Logs.Here().Information("Test scenario {0} was selected and started.", testScenario);

                for (int i = 0; i < countTrackingStart; i++)
                {
                    Logs.Here().Information("Event From was created {0} time(s).", i + 1);

                    await _cache.WriteHashedAsync<string>(eventKeyFrom, "count", Math.Abs(i).ToString(), 0.001);
                }

                Logs.Here().Information("Test scenario {0} was started and is waited the results.", testScenario);

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

                string frameSeparator1 = new('+', 39);
                string frameSeparator2 = "+";
                int testResult = await _cache.FetchHashedAsync<int>(testResultsKey1, testResultsField1);
                if (testResult == resultTest1Passed)
                {
                    Logs.Here().Information("{0} \n {1}Test scenario {2} passed successfully{3} \n {4}", frameSeparator1, frameSeparator2, testScenario, frameSeparator2, frameSeparator1);
                }
                else
                {
                    Logs.Here().Warning("Test scenario {0} FAILED.", testScenario);
                }
            }
        }
    }
}
