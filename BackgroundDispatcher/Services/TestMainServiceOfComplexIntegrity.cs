using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Shared.Library.Models;
using Shared.Library.Services;

// перенести таймеры в TestOfComplexIntegrityMainService и оставить без обёртки
// запрашивать таймеры в рабочих методах непосредственно перед вызовом шага отчёта

// можно поставить тест навсегда, увеличить время ожидания и посмотреть на поведение выгрузки книг на фронте -
// заведётся второй процесс или нет
// добавить счётчик, логи и отключение теста по команде

// вариант одновременной обработки рабочих и тестовых задач нерационален - тесты будет искажать лишняя случайная нагрузка

namespace BackgroundDispatcher.Services
{
    public interface ITestMainServiceOfComplexIntegrity
    {
        public Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public int FetchAssignedChainSerialNum(int lastCountStart, [CallerMemberName] string currentMethodName = "");
        public bool FetchIsTestInProgress();
        public Task<bool> RemoveWorkKeyOnStart(string key);
        long FetchWorkStopwatch();
        int FetchAssignedTestSerialNum();
    }

    public class TestMainServiceOfComplexIntegrity : ITestMainServiceOfComplexIntegrity
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly IConvertArrayToKeyWithIndexFields _convert;
        private readonly ITestScenarioService _scenario;
        private readonly ITestStorageServiceOfRawBookTexts _store;
        private readonly ICollectTasksInPackageService _collect;
        private readonly IEternalLogSupportService _eternal;
        private readonly ICacheManagerService _cache;
        private readonly ITestTasksPreparationService _prepare;
        private readonly ITestTimeImprintsReportIsFilledOut _report;

        public TestMainServiceOfComplexIntegrity(
            IHostApplicationLifetime applicationLifetime,
            IAuxiliaryUtilsService aux,
            IConvertArrayToKeyWithIndexFields convert,
            ITestScenarioService scenario,
            ITestStorageServiceOfRawBookTexts store,
            ICollectTasksInPackageService collect,
            IEternalLogSupportService eternal,
            ICacheManagerService cache,
            ITestTasksPreparationService prepare,
            ITestTimeImprintsReportIsFilledOut report)
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _aux = aux;
            _convert = convert;
            _scenario = scenario;
            _store = store;
            _collect = collect;
            _eternal = eternal;
            _cache = cache;
            _prepare = prepare;
            _report = report;
            _stopWatchTest = new Stopwatch();
            _stopWatchWork = new Stopwatch();
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestMainServiceOfComplexIntegrity>();

        private bool _isTestInProgress;
        private int _currentTestSerialNum;
        private int _currentChainSerialNum;
        private int _callingNumOfAssignedChainNum;
        private Stopwatch _stopWatchTest;
        private Stopwatch _stopWatchWork;

        // Report of the test time imprint
        // рабочим методами не нужно ждать возврата из теста - передали, что нужно и забыли
        // кроме первого раза, когда вернут уникальный номер
        // под этим уникальным номером отчёт о тестах хранится в вечном логе некоторое время
        // можно использовать номера в определённом диапазоне, не пересекающимся с книгами
        // но там же, возможно, делают сплошную выборку
        // и для уникального номера брать предыдущий из вечного лога, если нет, то 1
        // неудобно, надо делать сплошную выборку, проще хранить предыдущий номер в каком-то ключе
        // а, в нулевом поле будет храниться список всех тестов, можно просто измерять длину списка
        // ещё же номер сценария влияет на время выполнения
        // в вечном логе будет номер сценария в качестве bookId - получить номер сценария
        // приготовить все номера в другом методе и сохранить их в поля класса
        // за серийным номером ходить в другой метод FetchAssignedSerialNum - где он нужен
        // надо попробовать создать две шестёрки задач в два потока

        // список тестов (каждый элемент - один проход) на поле номера сценария в ключе вечного лога
        // много одинаковых проходов хранить нет смысла -
        // после N одинаковых проходов, N+1 проход копируется в эталон и все (или только N?) одинаковые удаляются
        // получаем список отчётов по данному сценарию, чтобы в конце теста в него дописать текущий отчёт
        // также этот метод устанавливает текущую версию теста в поле класса
        private async Task<(List<TestReport>, bool, int)> CreateAssignedSerialNum(int testScenario, string eternalTestTimingStagesReportsLog, CancellationToken stoppingToken)
        {
            int fieldBookIdWithLanguageId = testScenario;
            (List<TestReport> theScenarioReports, int theScenarioReportsCount) = await _eternal.EternalLogAccess<TestReport>(eternalTestTimingStagesReportsLog, fieldBookIdWithLanguageId);
            string referenceTestDescription = $"Reference test report for Scenario {testScenario}";
            string currentTestDescription = $"Current test report for Scenario {testScenario}";
            //Logs.Here().Information("Test report from Eternal Log for Scenario {0} length = {1}.", testScenario, theScenarioReportsCount);

            if (theScenarioReportsCount == 0)
            {
                // надо создать пустой первый элемент (вместо new TestReport()), который потом можно заменить на эталонный
                TestReport testReportForScenario = new TestReport() // RemoveTextFromTextSentence(bookPlainText)
                {
                    Guid = referenceTestDescription,
                    IsThisReportTheReference = false,
                    ThisReporVersion = 0,
                    TestScenarioNum = testScenario
                };
                // записываем пустышку, только если список пуст
                theScenarioReports.Add(testReportForScenario);
                // надо вернуть весь список, чтобы в конце теста в него дописать текущий отчёт
                //theScenarioReportsCount = theScenarioReports.Count;

                return (theScenarioReports, false, 0);
            }

            // возможные ситуации(по мере возникновения) -
            // 1 пустой список с пустышкой в нуле
            // 2 пустышка в нуле и от одного до пяти отчётов без версии
            // ---- -
            //тут ещё ситуация, что пять отчётов без версии, но они разные, только здесь это не волнует, это в конце текста будут разбираться
            // ситуация --пять разных отчётов без версии-- решается следующим образом -
            // в конце полученный отчёт сравнивается с предыдущим, если не совпадает, сообщается оператору и удаляется самый старый
            // таким образом, больше пяти отчётов не скапливается
            // если совпал с предыдущим, сравнивается со следующим и так далее
            // ---- -
            // 3 есть эталон с версией и один элемент той же версии
            // 4 есть эталон с версией и от одного до пяти отчётов без версии
            // 5 есть эталон с новой версией, бывший эталон со старой версией и один элемент новой версии
            // 6 опять добавляются отчёты без версии
            // и так далее, вроде бы все варианты
            // опять же, если не совпадает со следующим, то самый старый выкидывается, а новый сохраняется в конец
            // и так, пока все пять не совпадут, тогда создаётся эталон

            // флаг (признак), является ли этот отчёт действующим эталоном
            // нужен ли признак, что он был эталоном или это и так будет понятно по номеру версии?
            bool isThisReportTheReference = false;
            // счётчик количества отчётов без версий (с нулевой версией) - для определения готовности к спариванию
            int reportsWOversionsCount = 0;

            if (theScenarioReportsCount > 1)
            {
                // если эталон, проверять с конца остальные элементы, считая те, что без версии,
                // как только встретится с версией, дальше неинтересно
                // если не эталон, делать то же самое - то есть, наличие эталона в этот момент вроде бы никого не волнует
                isThisReportTheReference = theScenarioReports[0].IsThisReportTheReference;

                for (int i = theScenarioReportsCount - 1; i > 0; i--)
                {
                    if (theScenarioReports[i].ThisReporVersion == 0)
                    {
                        reportsWOversionsCount++;
                    }
                    else
                    {
                        // если встретился отчёт с версией, сразу можно выйти из цикла с проверкой - потом оформить в отдельный метод
                        // return
                    }
                }
            }
            //Logs.Here().Information("List theScenarioReports - Reference in [0] = {0}, length = {1}, without versions q-ty = {2}.", isThisReportTheReference, theScenarioReportsCount, reportsWOversionsCount);

            return (theScenarioReports, isThisReportTheReference, reportsWOversionsCount);
        }

        // этот метод возвращает текущий номер тестовой цепочки - начиная от события From - для маркировки прохода рабочими методами
        // каждый вызов даёт новый серийный номер - больше на 1
        public int FetchAssignedChainSerialNum(int eventFromCountNum, [CallerMemberName] string currentMethodName = "")
        {
            Logs.Here().Information("--- 180 Step 1 - FetchAssignedChainSerialNum was called by {0} instance No: {1} at time {2}.", currentMethodName, eventFromCountNum, _stopWatchWork.ElapsedMilliseconds);

            int lastCountStart = Interlocked.Increment(ref _callingNumOfAssignedChainNum);
            
            Logs.Here().Information("--- 184 Step 2 - Number of this FetchAssignedChainSerialNum = {0} at time {1}.", lastCountStart, _stopWatchWork.ElapsedMilliseconds);

            int chainSerialNum = Interlocked.Increment(ref _currentChainSerialNum);

            Logs.Here().Information("--- 188 Step 3 - Chain SerialNum was created - {0} at time {1}.", chainSerialNum, _stopWatchWork.ElapsedMilliseconds);
                    
            int lastCountEnd = Interlocked.Decrement(ref _callingNumOfAssignedChainNum);

            Logs.Here().Information("--- 192 Step 4 - In this instance Interlocked.Decrement = {0} and chain No: is still {1} at time {2}.", lastCountEnd, chainSerialNum, _stopWatchWork.ElapsedMilliseconds);

            return chainSerialNum;
        }

        // этот метод возвращает 
        public int FetchAssignedTestSerialNum()
        {
            int testSerialNum = _currentTestSerialNum;

            Logs.Here().Debug("The value of testSerialNum was requested = {0}.", testSerialNum);
            return testSerialNum;
        }

        // этот метод возвращает состояние _isTestInProgress - для быстрого определения наличия теста рабочими методами
        public bool FetchIsTestInProgress()
        {
            Logs.Here().Information("The state of _isTestInProgress was requested. It is {0}.", _isTestInProgress);
            return _isTestInProgress;
        }

        // этот метод возвращает состояние _stopWatchWork
        // надо хотя бы проверять, что секундомер запущен
        // но сначала померять время, чтобы понять разницу
        public long FetchWorkStopwatch()
        {
            return _stopWatchWork.ElapsedMilliseconds;
        }

        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------
        // **********************************************************************************************************
        // **********************************************************************************************************
        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------

        // поле "тест запущен" _isTestInProgress ставится в true - его проверяет контроллер при отправке задач
        // устанавливаются необходимые константы
        // записывается ключ глубины теста test1Depth-X - в нём хранится название метода, в котором тест должен закончиться
        // *** потом надо переделать глубину в список контрольных точек, в которых тест будет отчитываться о достижении их
        // удаляются результаты тестов (должны удаляться после теста, но всякое бывает)
        public async Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            _isTestInProgress = true;
            _currentChainSerialNum = 0;
            _callingNumOfAssignedChainNum = 0;
            // сбросить счётчик текущего шага тестового отчёта по таймингу
            bool resultResetOnStart = _report.Reset_stageReportFieldCounter();

            //_stopWatchTest = new Stopwatch();
            _stopWatchTest.Start();
            //_stopWatchWork = new Stopwatch();

            // первый параметр - isRequestForTestStopWatch = true, Work - false
            // второй параметр - запустить/прочитать = true, остановить/сбросить = false
            // возвращается засечка времени в мсек, без остановки секундомера
            //bool isRequestedStopWatchTest = true;
            //long tsTest01 = _report.StopwatchesControlAndRead(isRequestedStopWatchTest, true);
            long tsTest01 = _stopWatchTest.ElapsedMilliseconds; // double Elapsed.TotalMilliseconds
            Logs.Here().Information("Integration test was started. Stopwatch shows {0}", tsTest01);

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
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.EternalBookPlainTextHashesLog.Value; // key-book-plain-texts-hashes-versions-list

            KeyType eternalTestTimingStagesReportsLog = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog; // key-test-reports-timing-imprints-list
            string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report

            string assertProcessedBookAreEqualControl = constantsSet.Prefix.IntegrationTestPrefix.AssertProcessedBookAreEqualControl.Value; // assert-that-processed-book-fields-are-equal-to-control-books

            #endregion

            // вариант устарел, надо изменить способ сообщения о прохождении шага теста
            bool testSettingKey1WasDeleted = await _prepare.TestDepthSetting(constantsSet);
            // сделать перегрузку для множества ключей
            bool testResultsKey1WasDeleted = await _aux.RemoveWorkKeyOnStart(assertProcessedBookAreEqualControl);
            bool testResultsKey2WasDeleted = await _aux.RemoveWorkKeyOnStart(currentTestReportKey);
            bool controlListOfTestBookFieldsKeyWasDeleted = await _aux.RemoveWorkKeyOnStart(controlListOfTestBookFieldsKey);

            if (_aux.SomethingWentWrong(testSettingKey1WasDeleted, testResultsKey1WasDeleted, controlListOfTestBookFieldsKeyWasDeleted))
            {
                return false;
            }

            // test scenario selection - получение номера сценария из ключа запуска теста
            int testScenario = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

            // получаем список отчётов по данному сценарию, чтобы в конце теста в него дописать текущий отчёт
            // также этот метод устанавливает текущую версию теста в поле класса - для использования рабочими методами
            (List<TestReport> theScenarioReports, bool isThisReportTheReference, int reportsWOversionsCount) = await CreateAssignedSerialNum(testScenario, eternalTestTimingStagesReportsLog.Value, stoppingToken);

            // это будет серийный номер текущего теста - начинаться всегда будет с первого, нулевой зарезервирован для эталона
            int currentTestSerialNum = theScenarioReports.Count;

            // тут установить номер сценария для AddStageToTestTaskProgressReport
            _ = _report.SetTestScenarioNumber(currentTestSerialNum);

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
            if (result1 <= 0)
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
            string taskPackageGuid = await _collect.CreateTaskPackageAndSaveLog(constantsSet, -1, storageKeyBookPlainTexts, guidFieldsFromStorageKey);
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
            if (result2 <= 0)
            {
                _aux.SomethingWentWrong(true);
            }

            // вот тут создать ключ для проверки теста - с входящими полями книг
            int resultStartTest = await SetKeyWithControlListOfTestBookFields(constantsSet, storageKeyBookPlainTexts, rawPlainTextFields, stoppingToken);

            // тут запустить секундомер рабочих процессов (true means start)
            //isRequestedStopWatchTest = false;
            //long tsWork00 = _report.StopwatchesControlAndRead(isRequestedStopWatchTest, true);
            _stopWatchWork.Start();
            long tsWork01 = _stopWatchWork.ElapsedMilliseconds;
            Logs.Here().Information("CreateScenarioTasksAndEvents will start next. Work Stopwatch has been started and it is showing {0}", tsWork01);

            // создать из полей временного хранилища тестовую задачу, загрузить её и создать ключ оповещения о приходе задачи
            (List<string> uploadedBookGuids, int timeOfAllDelays) = await _prepare.CreateScenarioTasksAndEvents(constantsSet, storageKeyBookPlainTexts, rawPlainTextFields, delayList);

            bool testWasSucceeded = await WaitForTestFinishingTags(constantsSet, timeOfAllDelays, controlListOfTestBookFieldsKey, stoppingToken);

            // удалили ключ запуска теста, в дальнейшем - если полем запуска будет определяться глубина, то удалять только поле
            // но лучше из веб-интерфейса загружать в значение сложный класс - сразу и сценарий и глубину (и ещё что-то)
            bool eventKeyTestWasDeleted = await _cache.DeleteKeyIfCancelled(eventKeyTest);
            //bool testResultIsAsserted = testResult == test1IsPassed;
            bool finalResult = testWasSucceeded;

            // все константы или убрать в константы и/или перенести в метод DisplayResultInFrame
            string testDescription = $"Test scenario <{testScenario1description}>";
            const char separTrue = '+';
            const string textTrue = "passed successfully";
            const char separFalse = 'X';
            const string textFalse = "is FAILED";
            DisplayResultInFrame(finalResult, testDescription, separTrue, textTrue, separFalse, textFalse);

            // а потом удалить их третий раз - после завершения теста и проверки его результатов
            int result3 = await _prepare.RemoveTestBookIdFieldsFromEternalLog(constantsSet, "", uniqueBookIdsFromStorageKey);
            if (!(result3 > 0))
            {
                _aux.SomethingWentWrong(true);
            }

            // возвращаем состояние _isTestInProgress - тест больше не выполняется
            _isTestInProgress = false;

            //isRequestedStopWatchTest = true;
            //long tsTest99 = _report.StopwatchesControlAndRead(isRequestedStopWatchTest, false);
            long tsTest99 = _stopWatchTest.ElapsedMilliseconds;
            _stopWatchTest.Reset();
            Logs.Here().Debug("Integration test finished. Stopwatch has been stopped and it is showing {0}", tsTest99);
            _stopWatchWork.Reset();
            //_ = _report.StopwatchesControlAndRead(isRequestedStopWatchTest, false);
            // сбрасывать особого смысла нет, всё равно они обнуляются в начале теста

            // сбросить счётчик текущего номера тестовой цепочки 
            int countChain = Interlocked.Exchange(ref _currentChainSerialNum, 0);

            // сбросить счётчик текущего шага тестового отчёта по таймингу - сделать reset в другом классе
            bool resultResetOnEnd = _report.Reset_stageReportFieldCounter();

            (List<TestReport.TestReportStage> testTimingReportStagesList, string testReportHash) = await _report.ConvertDictionaryWithReportToList(constantsSet);

            (List<TestReport> theScenarioReportsLast, int equalReportsCount) = await _report.WriteTestScenarioReportsList(eternalTestTimingStagesReportsLog, theScenarioReports, testTimingReportStagesList, reportsWOversionsCount, testScenario, testReportHash);

            List<TestReport.TestReportStage> testTimingReportStagesListCurrent = new();
            //Logs.Here().Information("United List - {@R}, Length = {0}.", new { TestTimingReportStages = testTimingReportStagesListCurrent }, testTimingReportStagesListCurrent.Count);

            for (int i = 1; i < theScenarioReports.Count; i++)
            {
                testTimingReportStagesListCurrent.AddRange(theScenarioReportsLast[i].TestReportStages); // theScenarioReports.Count - 1
                Logs.Here().Information("Hash of hashes - {0} in theScenarioReports[{1}] .", theScenarioReportsLast[i].ThisReportHash, i);
            }

            //Logs.Here().Information("United List - {@R}, Length = {0}.", new { TestTimingReportStages = testTimingReportStagesListCurrent }, testTimingReportStagesListCurrent.Count);

            _ = _report.ViewComparedReportInConsole(constantsSet, tsTest99, testScenario, testTimingReportStagesListCurrent);//testTimingReportStagesList);
            
            return _isTestInProgress;
        }

        // key-test-reports-timing-imprints-list storage-key-for-current-test-report
        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------
        // **********************************************************************************************************
        // **********************************************************************************************************
        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------
        // ----------------------------------------------------------------------------------------------------------
        // storage-key-for-current-test-report

        private async Task<bool> WaitForTestFinishingTags(ConstantsSet constantsSet, int timeOfAllDelays, string controlListOfTestBookFieldsKey, CancellationToken stoppingToken)
        {
            string assertProcessedBookAreEqualControl = constantsSet.Prefix.IntegrationTestPrefix.AssertProcessedBookAreEqualControl.Value; // assert-that-processed-book-fields-are-equal-to-control-books
            int remaindedFieldsCount = constantsSet.Prefix.IntegrationTestPrefix.RemaindedFieldsCount.ValueInt; // 1
            int resultsField2 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField2.ValueInt; // 2
            int resultsField3 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField3.ValueInt; // 3
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

            // при нормальном завершении теста isControlListOfTestBookFieldsKeyExist = false и пройдет мимо сообщения об ошибке
            if (isControlListOfTestBookFieldsKeyExist)
            {
                Logs.Here().Error("Tests finished abnormally - Control List Key existing is {0}.", isControlListOfTestBookFieldsKeyExist);
            }
            else
            {
                // исчезнувший ключ - не вполне надёжное средство оповещения, поэтому надо записать ещё ключ testResultsKey1 и тест дополнительно проверит его
                // надо осветить ситуацию, что ключ не исчез, а количество полей равно нулю
                // скажем, за время проверки в ключе добавились поля -
                // первоначальные поля удалены, счётчик ноль, а в ключе поля остались - сообщить об ошибке
                IDictionary<int, int> assertReport = await _cache.FetchHashedAllAsync<int, int>(assertProcessedBookAreEqualControl);
                int remaindedFields = assertReport[remaindedFieldsCount];//await _cache.FetchHashedAsync<int, int>(assertProcessedBookAreEqualControl, remaindedFieldsCount);
                int fieldValuesResult = assertReport[resultsField2];
                int fieldValuesControlCount = assertReport[resultsField3];
                Logs.Here().Information("Control comparing is showing - remainded = {0}, result = {1}, control = {2}", remaindedFields, fieldValuesResult, fieldValuesControlCount);

                bool testWasSucceeded = remaindedFields == 0;
                if (testWasSucceeded)
                {
                    Logs.Here().Debug("Tests finished - Control List Key existing - {0}, Remainded Fields == 0 - {1}.", isControlListOfTestBookFieldsKeyExist, testWasSucceeded);
                    return true;
                }
            }

            return false;
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

        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            return await _aux.RemoveWorkKeyOnStart(key);
        }
    }
}
