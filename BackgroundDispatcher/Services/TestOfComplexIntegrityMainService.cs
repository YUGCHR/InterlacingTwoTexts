using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Shared.Library.Models;
using Shared.Library.Services;

// перенести таймеры в TestOfComplexIntegrityMainService и оставить без обёртки
// запрашивать таймеры в рабочих методах непосредственно перед вызовом шага отчёта

namespace BackgroundDispatcher.Services
{
    public interface ITestOfComplexIntegrityMainServicee
    {
        public Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public int FetchAssignedSerialNum();
        public bool FetchIsTestInProgress();
        public Task<bool> RemoveWorkKeyOnStart(string key);
        long FetchWorkStopwatch();
        Task<bool> ViewReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario = 1, string currentTestReportKey = "storage-key-for-current-test-report");
    }

    public class TestOfComplexIntegrityMainService : ITestOfComplexIntegrityMainServicee
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly IConvertArrayToKeyWithIndexFields _convert;
        private readonly ITestScenarioService _scenario;
        private readonly ITestRawBookTextsStorageService _store;
        private readonly ICollectTasksInPackageService _collect;
        private readonly IEternalLogSupportService _eternal;
        private readonly ICacheManagerService _cache;
        private readonly ITestTasksPreparationService _prepare;
        private readonly ITestReportIsFilledOutWithTimeImprints _report;

        public TestOfComplexIntegrityMainService(
            IHostApplicationLifetime applicationLifetime,
            IAuxiliaryUtilsService aux,
            IConvertArrayToKeyWithIndexFields convert,
            ITestScenarioService scenario,
            ITestRawBookTextsStorageService store,
            ICollectTasksInPackageService collect,
            IEternalLogSupportService eternal,
            ICacheManagerService cache,
            ITestTasksPreparationService prepare,
            ITestReportIsFilledOutWithTimeImprints report)
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

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestOfComplexIntegrityMainService>();

        private bool _isTestInProgress;
        private int _stageReportFieldCounter;
        private int _currentChainSerialNum;
        private Stopwatch _stopWatchTest;
        private Stopwatch _stopWatchWork;

        // Report of the test time imprint
        // рабочим методами не нужно ждать возврата из теста - передали, что нужно и забыли
        // кроме первого раза, когда вернут уникальный номер
        // как его потом дальше передавать, надо изучить
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
        private async Task<List<TestReport>> CreateAssignedSerialNum(int testScenario, string eternalTestTimingStagesReportsLog, CancellationToken stoppingToken)
        {
            int fieldBookIdWithLanguageId = testScenario;
            (List<TestReport> theScenarioReports, int theScenarioReportsCount) = await _eternal.EternalLogAccess<TestReport>(eternalTestTimingStagesReportsLog, fieldBookIdWithLanguageId);
            string referenceTestDescription = $"Reference test report for Scenario {testScenario}";
            string currentTestDescription = $"Current test report for Scenario {testScenario}";
            Logs.Here().Information("Test report from Eternal Log for Scenario {0} length = {1}.", testScenario, theScenarioReportsCount);

            if (theScenarioReportsCount == 0)
            {
                // надо создать пустой первый элемент (вместо new TextSentence()), который потом можно заменить на эталонный
                TestReport testReportForScenario = new TestReport() // RemoveTextFromTextSentence(bookPlainText)
                {
                    Guid = referenceTestDescription,
                    // можно использовать для сохранения, сколько тестов совпало для записи этого эталона
                    // если ноль - эталон ещё не создавался
                    //RecordActualityLevel = 0,
                    //LanguageId = 0,
                    // номер теста по порядку - совпадает с индексом списка
                    // нулевой - эталонный, сразу в него ничего не пишем, оставляем заглушку
                    // UploadVersion - в книгах постепенно отказываемся от использования
                    //UploadVersion = theScenarioReportsCount,
                    TestScenarioNum = testScenario
                };
                // записываем пустышку, только если список пуст
                theScenarioReports.Add(testReportForScenario);
                // надо вернуть весь список, чтобы в конце теста в него дописать текущий отчёт
                theScenarioReportsCount = theScenarioReports.Count;
            }

            // и тут еще надо проверить, есть ли эталонный или вместо него пустышка
            // если пустышка, записать вместо неё текущий тест после успешного окончания
            // в дальнейшем можно проверять специальный ключ settings, в котором будет указано, какой номер записать в эталонный
            // или ещё можно проверять группу отчётов на совпадение временного сценария -
            // если больше заданного количества все одинаковые, записывать в эталонный

            // это будет серийный номер текущего теста - начинаться всегда будет с первого, нулевой зарезервирован для эталона
            //_currentTestSerialNum = theScenarioReportsCount;

            return theScenarioReports;
        }

        // этот метод возвращает текущий номер тестовой цепочки - начиная от события From - для маркировки прохода рабочими методами
        // каждый вызов даёт новый серийный номер - больше на 1
        public int FetchAssignedSerialNum()
        {
            int chainSerialNum = Interlocked.Increment(ref _currentChainSerialNum);

            Logs.Here().Debug("The value of _currentTestSerialNum was requested = {0}.", chainSerialNum);
            return chainSerialNum;
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

        // поле "тест запущен" _isTestInProgress ставится в true - его проверяет контроллер при отправке задач
        // устанавливаются необходимые константы
        // записывается ключ глубины теста test1Depth-X - в нём хранится название метода, в котором тест должен закончиться
        // *** потом надо переделать глубину в список контрольных точек, в которых тест будет отчитываться о достижении их
        // удаляются результаты тестов (должны удаляться после теста, но всякое бывает)
        public async Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            _isTestInProgress = true;
            _stageReportFieldCounter = 0;
            _currentChainSerialNum = 0;

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

            string eternalTestTimingStagesReportsLog = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog.Value; // key-test-reports-timing-imprints-list
            string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report

            string assertProcessedBookAreEqualControl = constantsSet.Prefix.IntegrationTestPrefix.AssertProcessedBookAreEqualControl.Value; // assert-that-processed-book-fields-are-equal-to-control-books

            #endregion

            // вариант устарел, надо изменить способ сообщения о прохождении шага теста
            bool testSettingKey1WasDeleted = await _prepare.TestDepthSetting(constantsSet);
            // сделать перегрузку для множества ключей
            bool testResultsKey1WasDeleted = await _aux.RemoveWorkKeyOnStart(assertProcessedBookAreEqualControl);
            bool controlListOfTestBookFieldsKeyWasDeleted = await _aux.RemoveWorkKeyOnStart(controlListOfTestBookFieldsKey);

            if (_aux.SomethingWentWrong(testSettingKey1WasDeleted, testResultsKey1WasDeleted, controlListOfTestBookFieldsKeyWasDeleted))
            {
                return false;
            }

            // test scenario selection - получение номера сценария из ключа запуска теста
            int testScenario = await _cache.FetchHashedAsync<int>(eventKeyTest, eventFileldTest);

            // получаем список отчётов по данному сценарию, чтобы в конце теста в него дописать текущий отчёт
            // также этот метод устанавливает текущую версию теста в поле класса - для использования рабочими методами
            List<TestReport> theScenarioReports = await CreateAssignedSerialNum(testScenario, eternalTestTimingStagesReportsLog, stoppingToken);

            // тут установить номер сценария для AddStageToTestTaskProgressReport
            _ = _report.SetTestScenarioNumber(theScenarioReports.Count);

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

            // сбросить счётчик текущего шага тестового отчёта по таймингу
            int countField = Interlocked.Exchange(ref _stageReportFieldCounter, 0);

            _ = ViewReportInConsole(constantsSet, tsTest99, testScenario, currentTestReportKey);

            return _isTestInProgress;
        }

        // метод выводит таблицу с результатами текущего отчёта о времени прохождения теста по контрольным точкам
        public async Task<bool> ViewReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario = 1, string currentTestReportKey = "storage-key-for-current-test-report")
        {
            //string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report
            
            //char ttt = '\u2588'; // █ \u2588 ▮ U+25AE ▯ U+25AF 
            //int timeScaling = 5;

            int screenFullWidthLinesCount = 228;
            char screenFullWidthTopLineChar = '-';
            char screenFullWidthBetweenLineChar = '\u1C79'; // Ol Chiki Gaahlaa Ttuddaag --> ᱹ

            // проверить наличие ключа
            // проверить наличие словаря
            // проверить, что словарь не нулевой

            IDictionary<int, TestReport.TestReportStage> testTimingReportStages = await _cache.FetchHashedAllAsync<int, TestReport.TestReportStage>(currentTestReportKey);
            int testTimingReportStagesCount = testTimingReportStages.Count;

            TestReport.TestReportStage stage1 = testTimingReportStages[1];
            int r103 = stage1.TheScenarioReportsCount;
            TestReport.TestReportStage stageLast = testTimingReportStages[testTimingReportStagesCount - 1];
            int rL05 = (int)stageLast.TsTest;

            Console.WriteLine($"\n  Timing imprint report on testScenario No: {testScenario,-3:d} | total stages in the report = {testTimingReportStagesCount,-4:d} | total test time = {(int)tsTest99,5:d} msec."); // \t

            // рабочее решение тайм-лайн (часть 1)
            //Console.WriteLine($"Timing imprint report:\t{testScenario,3:d} ({testTimingReportStagesCount})");
            //TestReport.TestReportStage stage1 = testTimingReportStages[1];
            //int r101 = stage1.StageReportFieldCounter;
            //int r102 = stage1.ChainSerialNumber;
            //int r103 = stage1.TheScenarioReportsCount;
            //int r104 = (int)stage1.TsWork;
            //long r105 = stage1.TsTest;
            //string r106 = stage1.MethodNameWhichCalled;
            //Console.WriteLine("{0,3:d} | {1,3:d} || {2,6:d} | {3,6:d} | {4,6:d} - {5}", r101, r102, r104, 0, r104, );

            //int qnty1 = (int)((double)r104 / timeScaling) + 1;
            //string elapsedTimeForLine1 = ("").PadLeft(r104, ttt);
            //Console.WriteLine("{0,3:d} | {1,3:d} || {2,6:d} | {3,6:d} | {4,6:d} - {5}", r101, r102, r104, 0, r104, elapsedTimeForLine1);
            // ----------------------------------------

            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));
            Console.WriteLine("| {0,5} | {1,5} | {2,-42} | {3,8} | {4,8} | {5,8} | {6,5} | {7,8} | {8,-54} | {9,-54} | ", "stage", "chain", "MethodNameWhichCalled-PointNum/CallingNum", "timePrev", "timeWork", "timeDlt", "W-int", "W-bool", "WorkActionName", "WorkActionDescription");
            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));

            for (int i = 1; i <= testTimingReportStagesCount; i++) //
            {
                int r04prev = 0;
                if (i > 1)
                {
                    TestReport.TestReportStage stagePrev = testTimingReportStages[i - 1];
                    r04prev = (int)stagePrev.TsWork;
                }

                TestReport.TestReportStage stage = testTimingReportStages[i];
                int r01 = stage.StageReportFieldCounter;
                int r02 = stage.ChainSerialNumber;
                int r03 = stage.TheScenarioReportsCount;
                int r04 = (int)stage.TsWork;
                int r05 = (int)stage.TsTest;
                string r06 = stage.MethodNameWhichCalled;
                int r07 = stage.WorkActionNum;
                bool r08 = stage.WorkActionVal;
                string r09 = stage.WorkActionName;
                string r10 = stage.WorkActionDescription;
                int r11 = stage.CallingCountOfWorkMethod;
                int r12 = stage.CallingCountOfThisMethod;

                string r06Num = $"{r06}-{i} / {r11}";
                int r04delta = r04 - r04prev;

                Console.WriteLine("| {0,5:d} | {1,5:d} | {2,-42} | {3,8:d} | {4,8:d} | {5,8:d} | {6,5:d} | {7,8:b} | {8,-54} | {9,-54} | ", r01, r02, r06Num, r04prev, r04, r04delta, r07, r08, new string(r09.Take(54).ToArray()), new string(r10.Take(54).ToArray()));
                Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthBetweenLineChar));

                //Logs.Here().Information("Stage {0}, Chain {1}, Name {2}, Time {3}, TimePrev {4}, Delta {5}.", r01, r02, r06, r04, r04a, r04Delta);
                // рабочее решение тайм-лайн (часть 2)
                //int r04Delta = r04 - r04a;
                //string elapsedTimeForLineDot = ("").PadLeft(r04a, '.');
                //string elapsedTimeForLineSqv = ("").PadLeft(r04Delta, ttt);
                //Console.WriteLine("{0,3:d} | {1,3:d} || {2,6:d} | {3,6:d} | {4,6:d} - {5}{6}", r01, r02, r04, r04a, r04Delta, elapsedTimeForLineDot, elapsedTimeForLineSqv);
                // ----------------------------------------
                //int qntyPrev = (int)((double)r04a / timeScaling) - 1;
                //qntyPrevSum += qntyPrev;
                //int qnty = (int)((double)r04Delta / timeScaling) + 1;

                //string elapsedTimeForLineDot = ("").PadLeft(qntyPrevSum, '.');

                //string reportView = String.Format("{0,3:d} | {0,3:d} | {12,-30} | {46,6:d}", r01, r02, r06, r04);
                //string reportView = string.Format("{0,3:d} | {0,3:d} | {12,-30} | {46,6:d} - {56,100}", r01, r02, r06, r04, elapsedTimeForLine);

                //_ = String.Format("{0,-12} {2,12:N0} {1,8:yyyy}");
                // first argument, left align, 12 character wide column
                // second argument, right align, 8 character wide column
                // third argument, right align, 12 character wide column

                //Console.WriteLine("{0,-20} {1,5}\n", "Name", "Hours");
                //Console.WriteLine("{0,-20} {1,5:N1}", "Vasya", 10);

                //Console.WriteLine("{0,3:d} | {1,3:d} | {2,-30} | {3,6:d} | {4,6:d} | {5,6:d} - {6}{7}", r01, r02, r06, r04, qntyPrevSum, r04Delta, elapsedTimeForLineDot, elapsedTimeForLineSqv);
                //Console.WriteLine($"Stage:\t{r01,3:d} | {r02,3:d} | {r03,3:d} | {r04,3:d} | {r05,3:d} | {r06,3:d} | {r07,3:d} | {r08,3:d} | {r09,3:d} | {r10,3:d} | {r11,3:d} | {r12,3:d}");
                // HandlerCallingsDistributor
            }

            return true;
        }

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
