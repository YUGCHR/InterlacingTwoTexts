using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Models;
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

namespace BackgroundDispatcher.Services
{
    public interface IIntegrationTestService
    {
        public Task<bool> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0);
        public bool SomethingWentWrong(bool result0, bool result1 = true, bool result2 = true, bool result3 = true, bool result4 = true, [CallerMemberName] string currentMethodName = "");
        public Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);

        // create key with field/value/lifetime one or many times (with possible delay after each key has been created)
        public Task<bool> TestKeysCreationInQuantityWithDelay(int keysCount, int delayBetweenMsec, string key, string field, string value, double lifeTime);

        // create one key with many fields/values (with possible delay after each key has been created)
        public Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string[] key, string[] field, string[] value, double[] lifeTime);

        // create many keys with field/value/lifetime each (with possible delay after each key has been created)
        public Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string key, string[] field, string[] value, double lifeTime);
        public void SetIsTestInProgress(bool init_isTestInProgress);
        public bool IsTestInProgress();
        public Task<bool> RemoveWorkKeyOnStart(string key);
        public Task<bool> IsPreassignedDepthReached(ConstantsSet constantsSet, string currentDepth, CancellationToken stoppingToken);
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

        public async Task<bool> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0)
        {
            string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:f0c17236-3d50-4bce-9843-15fc9ee79bbd:test";
            string field_79_ENG = "bookText:bookGuid:0622f50c-d1d7-4dac-af14-b2a936fa750a";
            string field_79_RUS = "bookText:bookGuid:99e02275-c842-426c-8369-3ee72b668845";
            string field_78_ENG = "bookText:bookGuid:a97346d4-1506-4b63-8f6d-4ff7afd217f4";
            string field_78_RUS = "bookText:bookGuid:2d4e3513-ee43-4ff9-8993-2eb0bff53aed";
            string testKeBookPlainTexts = "bookPlainTexts:bookSplitGuid:f0c17236-3d50-4bce-9843-15fc9ee79bbd";
            string[] fields = new string[4] { field_79_ENG, field_79_RUS, field_78_ENG, field_78_RUS };

            // Value Type is TextSentence

            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime
            string eventKeyFromTest = $"{eventKeyFrom}:{eventKeyTest}"; // subscribeOnFrom:test

            // проверить ключи плоского текста и тестового оповещения и, если нужно, удалить их
            // пока что удаляем при старте
            bool resultPlainText = await RemoveWorkKeyOnStart(testKeBookPlainTexts);
            bool resultFromTest = await RemoveWorkKeyOnStart(eventKeyFromTest);

            Logs.Here().Information("Test plain text keys creation is started");

            if (resultPlainText && resultFromTest)
            {
                // 1 считать ключ-хранилище тестовых плоских текстов
                // 2 создать лист модели из ключа, поля и значения плоского текста и к нему сигнального
                // (значение теста в лист не писать, доставать из кэша в момент записи нового ключа)

                // выделить for в отдельный метод и уменьшить слоистость?
                for (int i = 0; i < testPairsCount * 2; i++)
                {
                    // прочитать первое поле хранилища
                    TextSentence bookPlainText = await _cache.FetchHashedAsync<TextSentence>(storageKeyBookPlainTexts, fields[i]);
                    Logs.Here().Information("Test plain text was read from key-storage");

                    // создать тестовый ключ плоского текста
                    resultPlainText = await WriteHashedAsyncWithDelayAfter<TextSentence>(testKeBookPlainTexts, fields[i], bookPlainText, eventKeyFromTestLifeTime, stoppingToken, delayAfter);
                    Logs.Here().Information("Test plain text was write in {@K} / {@F}", new { Key = testKeBookPlainTexts }, new { Field = fields[i] });

                    // создать тестовый ключ оповещения 
                    resultFromTest = await WriteHashedAsyncWithDelayAfter<string>(eventKeyFromTest, fields[i], testKeBookPlainTexts, eventKeyFromTestLifeTime, stoppingToken, delayAfter);
                    Logs.Here().Information("Test subscribeOnFrom was write in {@K} / {@F} / {@V}", new { Key = eventKeyFromTest }, new { Field = fields[i] }, new { Value = testKeBookPlainTexts });

                    if (SomethingWentWrong(resultPlainText, resultFromTest))
                    {
                        return false;
                    }
                }
                Logs.Here().Information("Test pair(s) was created in quantity {0}.", testPairsCount);
                return true;
            }
            return !SomethingWentWrong(resultPlainText, resultFromTest);
        }

        // можно сделать перегрузку с массивом на вход
        public bool SomethingWentWrong(bool result0, bool result1 = true, bool result2 = true, bool result3 = true, bool result4 = true, [CallerMemberName] string currentMethodName = "")
        { // return true if something went wrong!
            const int resultCount = 5;
            bool[] results = new bool[resultCount] { result0, result1, result2, result3, result4 };

            for (int i = 0; i < resultCount; i++)
            {
                if (!results[i])
                {
                    Logs.Here().Error("Situation in {0} where something went unexpectedly wrong is appeared - result No. {1} is {2}", currentMethodName, results[i], i);
                    return true;
                }
            }
            return false;
        }

        private async Task<bool> WriteHashedAsyncWithDelayAfter<T>(string key, string field, T value, double lifeTime, CancellationToken stoppingToken, int delayAfter = 0)
        {
            if (lifeTime > 0)
            {
                Logs.Here().Information("Event {@K} {@F} will be created.", new { Key = key }, new { Field = field });
                await _cache.WriteHashedAsync<T>(key, field, value, lifeTime);

                if (delayAfter > 0)
                {
                    Logs.Here().Information("Delay after key writing  will be {0} msec.", delayAfter);
                    await Task.Delay(delayAfter);
                }
                return true;
            }
            Logs.Here().Warning("{@K} with {@T} cannot be created.", new { Key = key }, new { LifeTime = lifeTime });
            return false;
        }

        public async Task<bool> IsPreassignedDepthReached(ConstantsSet constantsSet, string currentDepth, CancellationToken stoppingToken)
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

            // получать в параметрах, чтобы определить, из какого места вызвали
            string test1Depth1 = "HandlerCallingDistributore"; // other values - in constants
            string test1Depth2 = "DistributeTaskPackageInCafee";

            // здесь задана нужная глубина теста
            string targetTest1Depth = await _cache.FetchHashedAsync<string>(testSettingKey1, testSettingField1);
            bool keyWasDeleted = await _cache.DelFieldAsync(testResultsKey1, testSettingField1);

            // this method result is returned to variable <bool> targetDepthNotReached
            return currentDepth != targetTest1Depth;
        }

        public void SetIsTestInProgress(bool init_isTestInProgress)
        {
            Logs.Here().Information("SetIsTestInProgress will changed _isTestInProgress {0} on {1}.", _isTestInProgress, init_isTestInProgress);
            _isTestInProgress = init_isTestInProgress;
            Logs.Here().Information("New state of _isTestInProgress is {0}.", _isTestInProgress);
        }

        public bool IsTestInProgress()
        {
            Logs.Here().Information("The state of _isTestInProgress was requested. It is {0}.", _isTestInProgress);
            return _isTestInProgress;
        }

        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            // can use Task RemoveAsync(string[] keys, CommandFlags flags = CommandFlags.None);
            bool result = await _cache.IsKeyExist(key);
            if (result)
            {
                result = await _cache.DeleteKeyIfCancelled(key);
                Logs.Here().Information("{@K} was removed with result {0}.", new { Key = key }, result);
                return result;
            }
            Logs.Here().Information("{@K} does not exist.", new { Key = key });
            return !result;
        }

        public async Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // написать сценарии тестирования и на разные глубины
            // управлять глубиной можно тоже по ключу
            // и в рабочем варианте отключить тестирование одной константой

            Logs.Here().Information("Integration test was started.");
            // поле - отражение такого же поля в классе подписок, формально они не связаны, но по логике меняются вместе
            _isTestInProgress = true;

            #region Constants preparation
            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            int countDecisionMaking = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountDecisionMaking.Value; // 6
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value;

            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventFileldTest = constantsSet.Prefix.IntegrationTestPrefix.FieldStartTest.Value; // test

            string testSettingKey1 = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.Value; // testSettingKey1
            double testSettingKey1LifeTime = constantsSet.Prefix.IntegrationTestPrefix.SettingKey1.LifeTime;

            bool testSettingKey1WasDeleted = await RemoveWorkKeyOnStart(testSettingKey1);

            string testSettingField1 = constantsSet.Prefix.IntegrationTestPrefix.SettingField1.Value; // f1 (test depth)
            string test1Depth1 = constantsSet.Prefix.IntegrationTestPrefix.DepthValue1.Value; // HandlerCallingDistributore
            string test1Depth2 = constantsSet.Prefix.IntegrationTestPrefix.DepthValue2.Value; // DistributeTaskPackageInCafee

            // здесь задаётся глубина теста - название метода, в котором надо закончить тест
            // при дальнейшем углублении теста показывать этапы прохождения
            await _cache.WriteHashedAsync<string>(testSettingKey1, testSettingField1, test1Depth2, testSettingKey1LifeTime);

            //string testSettingField2 = "f2"; // 
            //string testSettingField3 = "f3"; //

            string testResultsKey1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsKey1.Value; // testResultsKey1
            string testResultsField1 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField1.Value; // testResultsField1
            int test1IsPassed = constantsSet.IntegerConstant.IntegrationTestConstant.ResultTest1Passed.Value; // 1

            // not used {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime
            string eventKeyFromTest = $"{eventKeyFrom}:{eventKeyTest}";

            // сделать тестовые книги для загрузки
            string eventFiledFromTest = $"{eventKeyFrom}:{eventKeyTest}"; // field to load plain texts
            string eventValueFromTest = $""; // key to load plain texts
            // } not used

            int testScenario1 = constantsSet.IntegerConstant.IntegrationTestConstant.TestScenario1.Value;
            string testScenario1description = constantsSet.IntegerConstant.IntegrationTestConstant.TestScenario1.Description;

            int delayTimeForTest1 = constantsSet.IntegerConstant.IntegrationTestConstant.DelayTimeForTest1.Value; // 1000

            bool testResultsKey1WasDeleted = await RemoveWorkKeyOnStart(testResultsKey1);
            if (SomethingWentWrong(testSettingKey1WasDeleted, testResultsKey1WasDeleted))
            {
                return false;
            }

            #endregion


            // можно сделать сценарии в виде листа и вызов конкретного по индексу
            // собирать константы в лист лучше уже в классе теста
            // или в интерфейсе выбора сценария показывать названия полей, а потом брать их значение для вызова теста
            // test scenario selection 
            int testScenario = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

            //int setting2 = await _cache.FetchHashedAsync<int>(testSettingKey1, testSettingField2);
            //int setting3 = await _cache.FetchHashedAsync<int>(testSettingKey1, testSettingField3);


            // узнаём сценарий теста, заданный в стартовом ключе
            // наверное, какой номер сценария, такой номер результата ждём
            // или номер результата - это глубина теста, надо разобраться
            if (testScenario == testScenario1)
            {
                Logs.Here().Information("Test scenario {0} was selected and started.", testScenario);


                // выделить в метод и дать внешний доступ с регулировкой количества - вызвать из временных тестов
                int testPairsCount = countTrackingStart / 2;
                int delayAfter = 10;
                //string key = eventKeyFromTest;
                //string[] field = new string[2] { "count", "count" };
                //string[] value = new string[2] { "1", "2" };
                //double lifeTime = eventKeyFromTestLifeTime;
                //bool result = await TestKeysCreationInQuantityWithDelay(delayBetweenMsec, key, field, value, lifeTime);

                // загрузка тестовых плоских текстов и ключа оповещения
                bool testStartedWithResult = await CreateBookPlainTextsForTests(constantsSet, stoppingToken, testPairsCount, delayAfter);

                Logs.Here().Information("Test scenario {0} ({1}) was started with {@S} and is waited the results.", testScenario, testScenario1description, new { TestStartedWith = testStartedWithResult });


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
                bool finalResult = eventKeyTestWasDeleted && testResultIsAsserted;

                // все константы или убрать в константы и/или перенести в метод DisplayResultInFrame
                string testDescription = $"Test scenario <{testScenario1description}>";
                char separTrue = '+';
                string textTrue = $"passed successfully";
                char separFalse = 'X';
                string textFalse = $"is FAILED";
                DisplayResultInFrame(finalResult, testDescription, separTrue, textTrue, separFalse, textFalse);
            }
            // возвращаем состояние _isTestInProgress - тест больше не выполняется
            _isTestInProgress = false;
            return _isTestInProgress;
        }

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
        public async Task<bool> TestKeysCreationInQuantityWithDelay(int keysCount, int delayBetweenMsec, string key, string field, string value, double lifeTime)
        {// create key with field/value/lifetime one or many times (with possible delay after each key has been created)
            if (keysCount > 0 && lifeTime > 0)
            {
                for (int i = 0; i < keysCount; i++)
                {
                    Logs.Here().Information("Event {@K} {@F} will be created.", new { Key = key[i] }, new { Field = field[i] });
                    await _cache.WriteHashedAsync<string>(key, field, value, lifeTime);

                    if (delayBetweenMsec > 0)
                    {
                        await Task.Delay(delayBetweenMsec);
                        Logs.Here().Information("Delay between events is {0} msec.", delayBetweenMsec);
                    }
                }
                return true;
            }
            Logs.Here().Warning("{@K} with {@T} in {@C} cannot be created.", new { Key = key }, new { LifeTime = lifeTime }, new { KeysCount = keysCount });
            return false;
        }

        public async Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string[] key, string[] field, string[] value, double[] lifeTime)
        {// create one key with many fields/values (with possible delay after each key has been created)
            int keyLength = key.Length;
            int fieldLength = field.Length;
            int valueLength = value.Length;
            int lifeTimeLength = lifeTime.Length;

            if (keyLength == fieldLength && fieldLength == valueLength && valueLength == lifeTimeLength)
            {
                for (int i = 0; i < keyLength; i++)
                {
                    bool createKeyI = await TestKeysCreationInQuantityWithDelay(1, delayBetweenMsec, key[i], field[i], value[i], lifeTime[i]);
                    if (!createKeyI)
                    {
                        return false;
                    }
                }
                return true;
            }
            Logs.Here().Warning("Some arrays lengths are mismatched.");
            return false;
        }

        public async Task<bool> TestKeysCreationInQuantityWithDelay(int delayBetweenMsec, string key, string[] field, string[] value, double lifeTime)
        {// create many keys with field/value/lifetime each (with possible delay after each key has been created)
            int fieldLength = field.Length;
            int valueLength = value.Length;

            if (fieldLength == valueLength)
            {
                for (int i = 0; i < fieldLength; i++)
                {
                    bool createKeyI = await TestKeysCreationInQuantityWithDelay(1, delayBetweenMsec, key, field[i], value[i], lifeTime);
                    if (!createKeyI)
                    {
                        return false;
                    }
                }
                return true;
            }
            Logs.Here().Warning("Some arrays lengths are mismatched.");
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

        private async Task<bool> StartTask3beforeTest(ConstantsSet constantsSet)
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

        private async Task<bool> StartTestBeforeTask3(ConstantsSet constantsSet)
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
