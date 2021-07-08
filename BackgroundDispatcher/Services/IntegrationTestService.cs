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
// организовать тестовый ключ с плоским текстом
// key - bookPlainTexts:bookSplitGuid:f0c17236-3d50-4bce-9843-15fc9ee79bbd:test
// field_1 - bookText:bookGuid:0622f50c-d1d7-4dac-af14-b2a936fa750a - LanguageId:0, UploadVersion:30, BookId:79
// field_2 - bookText:bookGuid:99e02275-c842-426c-8369-3ee72b668845 - LanguageId:1, UploadVersion:30, BookId:79
// field_3 - bookText:bookGuid:a97346d4-1506-4b63-8f6d-4ff7afd217f4 - LanguageId:0, UploadVersion:30, BookId:78
// field_4 - bookText:bookGuid:2d4e3513-ee43-4ff9-8993-2eb0bff53aed - LanguageId:1, UploadVersion:30, BookId:78
// из этого ключа-хранилища создавать ключ со стандартным названием (без test в конце) и с одним из полей по очереди
// на каждый ключ генерировать subscribeFrom field_1 key(w/o test)
// 



namespace BackgroundDispatcher.Services
{
    public interface IIntegrationTestService
    {
        public Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public Task<bool> TestKeysCreationInQuantityWithDelay(int keysCount, int delayBetweenMsec, string key, string[] field, string[] value, double lifeTime);
        public void SetIsTestInProgress(bool init_isTestInProgress);
        public bool IsTestInProgress();
        public Task<bool> RemoveWorkKeyOnStart(string key);
        public Task<bool> Depth_HandlerCallingDistributore_Reached(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public Task<bool> TempTestOf3rdTaskAdded(ConstantsSet constantsSet, bool tempTestOf3rdTaskAdded, bool startTask3beforeTest);
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

        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            // can use Task RemoveAsync(string[] keys, CommandFlags flags = CommandFlags.None);
            bool result = await _cache.DeleteKeyIfCancelled(key);
            return result;
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
            int test1IsPassed = constantsSet.IntegerConstant.IntegrationTestConstant.ResultTest1Passed.Value; // 1

            bool testResultsKey1WasDeleted = await _cache.DeleteKeyIfCancelled(testResultsKey1);

            if (testResultsKey1WasDeleted)
            {
                Logs.Here().Information("Key {0} was deleted successfully.", testResultsKey1);
            }

            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime
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


                // выделить в метод и дать внешний доступ с регулировкой количества - вызвать из временных тестов
                int keysCount = countTrackingStart;
                int delayBetweenMsec = 10;
                string key = eventKeyFromTest;
                string[] field = new string[2] { "count", "count" };
                string[] value = new string[2] { "1", "2" };
                double lifeTime = eventKeyFromTestLifeTime;
                bool result = await TestKeysCreationInQuantityWithDelay(keysCount, delayBetweenMsec, key, field, value, lifeTime);


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
                bool testResultIsAsserted = testResult == test1IsPassed;
                if (eventKeyTestWasDeleted && testResultIsAsserted)
                {
                    char separatorUnit = '+';
                    string successTextMessage = $"Test scenario <{testScenario1description}> passed successfully";
                    (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separatorUnit, successTextMessage);
                    Logs.Here().Information("{0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
                }
                else
                {
                    char separatorUnit = 'X';
                    string successTextMessage = $"Test scenario <{testScenario1description}> is FAILED";
                    (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separatorUnit, successTextMessage);
                    Logs.Here().Information("{0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
                    //Logs.Here().Warning("Test scenario {0} FAILED.", testScenario);
                }
            }
            // возвращаем состояние _isTestInProgress - тест больше не выполняется
            _isTestInProgress = false;
            return _isTestInProgress;
        }

        public async Task<bool> TestKeysCreationInQuantityWithDelay(int keysCount, int delayBetweenMsec, string key, string[] field, string[] value, double lifeTime)
        {
            bool lifeTimeCanCreateKey = lifeTime > 0;
            if (!lifeTimeCanCreateKey)
            {
                Logs.Here().Warning("{@K} with {@T} cannot be created.", new { Key = key }, new { LifeTime = lifeTime });
                return false;
            }

            int fieldLength = field.Length;
            int valueLength = value.Length;

            if (fieldLength == valueLength && keysCount == fieldLength)
            {
                // выделить в метод и дать внешний доступ с регулировкой количества - вызвать из временных тестов
                for (int i = 0; i < keysCount; i++)
                {
                    Logs.Here().Information("Event {0} was created {1} time(s).", key, i + 1);

                    // запись данных книги в фальшивый (тестовый) ключ обработки плоского текста
                    await _cache.WriteHashedAsync<string>(key, field[i], value[i], lifeTime);
                    if (delayBetweenMsec > 0)
                    {
                        await Task.Delay(delayBetweenMsec);
                        Logs.Here().Information("Delay between events is {0} msec.", delayBetweenMsec);

                    }
                }

                return true;
            }

            Logs.Here().Warning("{@Q} is mismatched with array(s) length - {@F} {@V}.", new { KeysQuantity = keysCount }, new { FieldLength = fieldLength }, new { ValueLength = valueLength });
            return false;
        }

        public async Task<bool> TempTestOf3rdTaskAdded(ConstantsSet constantsSet, bool tempTestOf3rdTaskAdded, bool startTask3beforeTest)
        {
            // создание третьей задачи, когда две только уехали на обработку по таймеру - как поведёт себя вызов теста в этот момент
            // рассмотреть два варианта - вызов теста до появления третьей задачи и после
            // по идее в первом варианте третья задача должна остаться проигнорироаанной
            // а во втором - тест должен отложиться на 10 секунд и потом задача должна удалиться
            // выделить в отдельный метод?
            // tempTestOf3rdTaskAdded - from redis key
            bool result;
            if (tempTestOf3rdTaskAdded)
            {
                // рассмотреть два варианта - вызов теста после появления третьей задачи и до
                if (startTask3beforeTest)
                {
                    // здесь тест должен отложиться на 10 секунд и потом одиночная задача должна удалиться
                    Logs.Here().Information("Test will call StartTask3beforeTest {0})", startTask3beforeTest);
                    result = await StartTask3beforeTest(constantsSet);
                }
                else
                {
                    // здесь третья задача должна остаться проигнорироаанной, а тест выполниться сразу же, без ожидания 10 сек
                    Logs.Here().Information("Test will call StartTestBeforeTask3 {0})", startTask3beforeTest);
                    result = await StartTestBeforeTask3(constantsSet);
                }

                Logs.Here().Information("Temporary test finished with result = {0}.", result);
                return result;
            }

            return false;
        }

        public async Task<bool> StartTask3beforeTest(ConstantsSet constantsSet)
        {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventFileldTest = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test

            // сначала создаём третью задачу, а потом даём команду на запуск теста
            await _cache.WriteHashedAsync<string>(eventKeyFrom, "count", "testTask", eventKeyFromTestLifeTime);

            await _cache.WriteHashedAsync<int>(eventKeyTest, eventFileldTest, 1, eventKeyFromTestLifeTime);

            // to read value for awaiting when keys will be written
            int checkValue = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

            return checkValue == 1;
        }

        public async Task<bool> StartTestBeforeTask3(ConstantsSet constantsSet)
        {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventFileldTest = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test

            // сначала даём команду на запуск теста, а потом создаём третью задачу (успеет ли тест её заблокировать)
            await _cache.WriteHashedAsync<int>(eventKeyTest, eventFileldTest, 1, eventKeyFromTestLifeTime);

            await _cache.WriteHashedAsync<string>(eventKeyFrom, "count", "testTask", eventKeyFromTestLifeTime);

            // to read value for awaiting when keys will be written
            int checkValue = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

            return checkValue == 1;
        }

    }
}
