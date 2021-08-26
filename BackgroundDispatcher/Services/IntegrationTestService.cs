using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Models;
using BooksTextsSplit.Library.Services;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

// *****************************************
// DONE ещё же имя вызывающего метода получаем, его надо добавить в модель и ключ
// DONE выводить в рамочку количество выполненных задач - для ручного контроля
// выяснить кто создаёт ключи типа bookPlainTexts:bookSplitGuid:84865514-6dc9-4599-9a75-06373bc3d3fa и когда их можно удалить
// DONE посмотреть счётчик многопоточности
// DONE наверное, надо в стартовый метод передавать не номер сценария - он и так известен - а номер цепочки
// DONE тогда получается каждый старт от события From имеет свой серийный номер
// потом на диаграмме можно выстроить всю цепочку в линию, а по времени совместить с другими цепочками ниже
// можно генерировать выходной отчёт в формате диаграммы - более реально - тайм-лайн для веба
// *****************************************

namespace BackgroundDispatcher.Services
{
    public interface IIntegrationTestService
    {
        public Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public Task<bool> EventCafeOccurred(ConstantsSet constantsSet, CancellationToken stoppingToken);
        public Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, int currentChainSerialNum, string workActionName, CancellationToken stoppingToken, [CallerMemberName] string currentMethodName = "");
        
        //public void SetIsTestInProgress(bool init_isTestInProgress);

        public int FetchAssignedSerialNum();
        public bool FetchIsTestInProgress();
        public Task<bool> RemoveWorkKeyOnStart(string key);
    }

    public class IntegrationTestService : IIntegrationTestService
    {
        private readonly IAuxiliaryUtilsService _aux;
        private readonly IConvertArrayToKeyWithIndexFields _convert;
        private readonly ITestScenarioService _scenario;
        private readonly ITestRawBookTextsStorageService _store;
        private readonly ICollectTasksInPackageService _collect;
        private readonly IEternalLogSupportService _eternal;
        private readonly ICacheManagerService _cache;
        private readonly ITestTasksPreparationService _prepare;

        public IntegrationTestService(
         IAuxiliaryUtilsService aux,
         IConvertArrayToKeyWithIndexFields convert,
         ITestScenarioService scenario,
         ITestRawBookTextsStorageService store,
         ICollectTasksInPackageService collect,
         IEternalLogSupportService eternal,
         ICacheManagerService cache,
         ITestTasksPreparationService prepare)
        {
            _aux = aux;
            _convert = convert;
            _scenario = scenario;
            _store = store;
            _collect = collect;
            _eternal = eternal;
            _cache = cache;
            _prepare = prepare;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<IntegrationTestService>();

        private bool _isTestInProgress;
        private int _stageReportFieldCounter;
        private int _currentTestSerialNum;
        private int _currentChainSerialNum;
        private int _callingNumOfAddStageToTestTaskProgressReport;
        private Stopwatch _stopWatchTest;
        private Stopwatch _stopWatchWork;


        // Report of the test time imprint
        // можно из очередного метода по ходу работы вызвать контрольный метод из тестов,
        // он проверит тест сейчас или нет и, если надо, запишет имя вызвавшего метода в ключ результатов с полем имени метода
        // а в конце текста проверять значение в определённом имени
        // в качестве значения можно передавать что-то целое, что есть в текущем методе
        // счётчик вызовов, например
        // записывать в поле времени или в поле по порядку записывать сложный класс с местом и временем, а последний номер хранить в нулевом поле
        // после теста посчитать все временные промежутки и сравнить с эталоном
        // из каждого метода вызывать метод теста типа отчёт о ходе выполнения задачи, он проверит поле класса и если не тест, сразу же вернётся
        // этому методу передавать название
        // в классе тестов хранить поля -
        // bool тест или нет (уже есть)
        // int счётчик - номер поля (доступ к номеру через Interlocked)
        // ещё нужен номер всего теста, но его можно хранить проще - где-то в ключе или классе теста, он будет нужен в конце, при записи в вечный лог
        // при вызове метода AddStageToTestTaskProgressReport передать ему класс TextSentence или что будет доступно в этой точке
        // внутри метода добавить текущее время (потом можно засечку секундомера)
        // взять инкремент текущего номера и это будет имя поля в ключе отчёта
        // дописать всё в класс текст и записать его в ключ
        // по окончанию теста показать ход его выполнения с отклонениями от эталона
        // и потом добавить в список и записать в вечный лог с нулевым номером (bookId)
        // расписать ход выполнения по шагам
        // порядок работы -
        // в начале integration test start инициализовать счётчик шагов
        // очистить ключ отчёта
        // или лучше записать в него нулевое поле с начальным временем
        // а, сначала одно, потом другое
        // отделить время подготовки теста и время выполнения - виртуально, на два блока в отчёте
        // отдельно - решить вопрос с первым элементом в списке для значения в вечном логе
        // можно хранить там эталонный, но вряд ли он будет постоянный
        // можно текущий признанный эталонным переписывать в нулевой элемент,
        // сохраняя на прежнем месте (можно с указанием, откуда взят - Text Sentence большой, места для всего хватит)
        // для переписывания можно запустить тест с нулевым сценарием - работа с утилитами или можно использовать специальный ключ настроек
        // при работе с утилитами можно управлять с консоли или ключами, но ключи активировать опять нулевым сценарием
        // вывести список существующих отчётов с кратким описанием и предложить выбрать эталонный
        // скорее всего последний
        // и ещё надо чистить список - скажем при превышении длины удалять первые или ещё как
        // при проведении теста самом начале присваивать потоку уникальный номер - он же номер поля отчёта? похоже, нет
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

        // всё не так
        // получается весь список - это один тест
        // всё же нет - список тестов (каждый элемент - один проход) на поле номера сценария в ключе вечного лога
        // много одинаковых проходов хранить нет смысла -
        // после N одинаковых проходов, N+1 проход копируется в эталон и все (или только N?) одинаковые удаляются
        // получаем список отчётов по данному сценарию, чтобы в конце теста в него дописать текущий отчёт
        // также этот метод устанавливает текущую версию теста в поле класса - для использования рабочими методами
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
            _currentTestSerialNum = theScenarioReportsCount;

            return theScenarioReports;
        }

        // этот метод возвращает текущий номер тестовой цепочки - начиная от события From - для маркировки прохода рабочими методами
        // каждый вызов даёт новый серийный номер - больше на 1
        public int FetchAssignedSerialNum()
        {
            int chainSerialNum = Interlocked.Increment(ref _currentChainSerialNum);

            Logs.Here().Information("The value of _currentTestSerialNum was requested = {0}.", chainSerialNum);
            return chainSerialNum;
        }

        // этот метод возвращает состояние _isTestInProgress - для быстрого определения наличия теста рабочими методами
        public bool FetchIsTestInProgress()
        {
            Logs.Here().Information("The state of _isTestInProgress was requested. It is {0}.", _isTestInProgress);
            return _isTestInProgress;
        }

        // этот метод возвращает состояние _isTestInProgress - для быстрого определения наличия теста рабочими методами
        private long StopwatchesControlAndRead(Stopwatch stopWatch, bool control, string stopWatchName = "name is not defined")
        {
            // надо проверять текущее состояние секундомера перед его изменением
            bool currentState = stopWatch.IsRunning;

            string stopWatchState;

            //// если надо запустить и он остановлен (запускаем)
            //if (control && !currentState)
            //{
            //    _stopWatchTest.Start();
            //    stopWatchState = "was started";
            //}
            //// если надо остановить и он запущен (останавливаем)
            //if (!control && currentState)
            //{
            //    _stopWatchTest.Stop();
            //    stopWatchState = "was stopped";
            //}
            //// если надо запустить и он уже запущен (показываем текущее время)
            //if (control && currentState)
            //{
            //    stopWatchState = "has beed started already";
            //}
            //// если надо остановить и он уже остановлен (сбрасываем)
            //if (!control && !currentState)
            //{
            //    stopWatchState = "had been already stopped and was just reset");
            //    _stopWatchTest.Reset();
            //}

            // требуется запустить секундомер - прислали true
            if (control)
            {
                // если надо запустить и он уже запущен (показываем текущее время)
                if (currentState)
                {
                    stopWatchState = "has beed started already";
                }
                // если надо запустить и он остановлен (запускаем)
                else
                {
                    stopWatch.Start();
                    stopWatchState = "was started";
                }
            }
            // требуется остановить секундомер - прислали false
            else
            {
                // если надо остановить и он запущен (останавливаем)
                if (currentState)
                {
                    stopWatch.Stop();
                    stopWatchState = "was stopped";
                }
                // если надо остановить и он уже остановлен (сбрасываем)
                else
                {
                    stopWatch.Reset();
                    stopWatchState = "had been already stopped and was just reset";
                }
            }

            //TimeSpan tsControl = stopWatch.Elapsed;
            long stopwatchMeasuredTime = stopWatch.ElapsedMilliseconds; // double Elapsed.TotalMilliseconds
            Logs.Here().Information("Stopwatch {0} {1}. It shows {2} msec.", stopWatchName, stopWatchState, stopwatchMeasuredTime);

            return stopwatchMeasuredTime;
        }

        // этот метод вызывается только из рабочих методов других классов
        // этот метод получает имя рабочего метода currentMethodName, выполняющего тест в данный момент и что-то из описания его работы
        // 
        public async Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, int currentChainSerialNum, string workActionName, CancellationToken stoppingToken, [CallerMemberName] string currentMethodName = "")
        {
            if (_isTestInProgress)
            {
                string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report
                double currentTestReportKeyExistingTime = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.LifeTime; // ?
                Logs.Here().Debug("AddStageToTestTaskProgressReport was called by {0}.", currentMethodName);

                // определяем собственно номер шага текущего отчёта
                int count = Interlocked.Increment(ref _stageReportFieldCounter);

                // ещё полезно иметь счётчик вызовов - чтобы определить многопоточность
                int lastCountStart = Interlocked.Increment(ref _callingNumOfAddStageToTestTaskProgressReport);
                Logs.Here().Information("AddStageToTestTaskProgressReport started {0} time. Stage = {1}.", lastCountStart, count);

                long tsWork = StopwatchesControlAndRead(_stopWatchWork, true, nameof(_stopWatchWork));
                long tsTest = StopwatchesControlAndRead(_stopWatchTest, true, nameof(_stopWatchTest));

                // ещё можно получать и записывать номер потока, в котором выполняется этот метод

                TestReport.TestReportStage testTimingReportStage = new TestReport.TestReportStage()
                {
                    // номер шага с записью отметки времени теста, он же номер поля в ключе записи текущего отчёта
                    StageReportFieldCounter = count,
                    // серийный номер единичной цепочки теста - обработка одной книги от события Fro
                    ChainSerialNumber = currentChainSerialNum,
                    // номер теста в пакете тестов по данному сценарию, он же индекс в списке отчётов
                    TheScenarioReportsCount = _currentTestSerialNum,
                    // отметка времени от старта рабочей цепочки
                    TsWork = tsWork,
                    // отметка времени от начала теста
                    TsTest = tsTest,
                    // имя вызвавшего метода, полученное в параметрах
                    MethodNameWhichCalled = currentMethodName,
                    // ключевое слово, которым делится вызвавший метод - что-то о его занятиях
                    WorkActionName = workActionName,
                    // количество одновременных вызовов этого метода (AddStageToTestTaskProgressReport)
                    CallingNumOfAddStageToTestTaskProgressReport = lastCountStart
                };

                await _cache.WriteHashedAsync<int, TestReport.TestReportStage>(currentTestReportKey, count, testTimingReportStage, currentTestReportKeyExistingTime);
                Logs.Here().Information("testTimingReportStage time {0} was writen in field {1}.", testTimingReportStage.TsWork, count);

                int lastCountEnd = Interlocked.Decrement(ref _callingNumOfAddStageToTestTaskProgressReport);
                Logs.Here().Information("AddStageToTestTaskProgressReport ended {0} time.", lastCountEnd);

                return true;
            }
            return false;
        }

        // поле "тест запущен" _isTestInProgress ставится в true - его проверяет контроллер при отправке задач
        // устанавливаются необходимые константы
        // записывается ключ глубины теста test1Depth-X - в нём хранится название метода, в котором тест должен закончиться
        // *** потом надо переделать глубину в список контрольных точек, в которых тест будет отчитываться о достижении их
        // удаляются результаты тестов (должны удаляться после теста, но всякое бывает)
        public async Task<bool> IntegrationTestStart(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            _stopWatchTest = new Stopwatch();
            _stopWatchWork = new Stopwatch();
            long tsTest01 = StopwatchesControlAndRead(_stopWatchTest, true, nameof(_stopWatchTest));
            Logs.Here().Information("Integration test was started. Stopwatch shows {0}", tsTest01);

            _isTestInProgress = true;
            _stageReportFieldCounter = 0;
            _currentChainSerialNum = 0;
            _callingNumOfAddStageToTestTaskProgressReport = 0;

            // назначить версию 1 по умолчанию
            _currentTestSerialNum = 1;

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
            List<TestReport> theScenarioReportsCount = await CreateAssignedSerialNum(testScenario, eternalTestTimingStagesReportsLog, stoppingToken);

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
            if (!(result1 > 0))
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
            string taskPackageGuid = await _collect.CreateTaskPackageAndSaveLog(constantsSet, storageKeyBookPlainTexts, guidFieldsFromStorageKey);
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
            if (!(result2 > 0))
            {
                _aux.SomethingWentWrong(true);
            }

            // вот тут создать ключ для проверки теста - с входящими полями книг
            int resultStartTest = await SetKeyWithControlListOfTestBookFields(constantsSet, storageKeyBookPlainTexts, rawPlainTextFields, stoppingToken);

            // создать из полей временного хранилища тестовую задачу, загрузить её и создать ключ оповещения о приходе задачи
            (List<string> uploadedBookGuids, int timeOfAllDelays) = await _prepare.CreateScenarioTasksAndEvents(constantsSet, storageKeyBookPlainTexts, rawPlainTextFields, delayList);

            // тут запустить секундомер рабочих процессов (true means start)
            long tsWork00 = StopwatchesControlAndRead(_stopWatchWork, true, nameof(_stopWatchWork));
            Logs.Here().Information("CreateScenarioTasksAndEvents finished. Work Stopwatch has been started and it is showing {0}", tsWork00);

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

            long tsTest99 = StopwatchesControlAndRead(_stopWatchTest, false, nameof(_stopWatchTest));
            Logs.Here().Information("Integration test finished. Stopwatch has been stopped and it is showing {0}", tsTest99);
            _ = StopwatchesControlAndRead(_stopWatchTest, false, nameof(_stopWatchTest));

            // сбрасывать особого смысла нет, всё равно они обнуляются в начале теста

            // сбросить счётчик текущего номера тестовой цепочки 
            int countChain = Interlocked.Exchange(ref _currentChainSerialNum, 0);

            // сбросить счётчик текущего шага тестового отчёта по таймингу
            int countField = Interlocked.Exchange(ref _stageReportFieldCounter, 0);

            return _isTestInProgress;
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

        public async Task<bool> EventCafeOccurred(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // получен ключ кафе, секундомер рабочих процессов пора остановить
            long tsWork99 = StopwatchesControlAndRead(_stopWatchWork, false, nameof(_stopWatchWork));
            Logs.Here().Information("Books processing were finished. Work Stopwatch has been stopped and it is showing {0}", tsWork99);
            // to reset
            _ = StopwatchesControlAndRead(_stopWatchWork, false, nameof(_stopWatchWork));

            string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package

            // выгрузить содержимое ключа кафе и сразу вернуть true в подписку, чтобы освободить место для следующего вызова
            // и всё равно можно прозевать вызов
            // для надёжности надо вернуть true, а потом сразу выгрузить ключ кафе, тогда точно не пропустить второй вызов
            // для точности сделать eventCafeIsNotExisted полем класса и отсюда её поставить в true - а потом не спеша делать всё остальное
            IDictionary<string, string> taskPackageGuids = await _cache.FetchHashedAllAsync<string>(cafeKey);

            _ = AssertProcessedBookFieldsAreEqualToControl(constantsSet, taskPackageGuids, stoppingToken);

            return true;
        }

        // метод предварительного сбора результатов теста
        // получает ключ пакета, достаёт бук и что с ним делает?
        // можно создать ключ типа список книг теста (важен порядок или нет?) при старте теста
        // и при проверке прохождения теста удалять из него поля, предварительно сравнивая bookId, bookGuid и bookHash
        // как все поля исчезнут, так тест прошёл нормально - если, конечно, не осталось лишних на проверку после теста
        // в смысле, ненайденных в стартовом списке теста
        private async Task<bool> AssertProcessedBookFieldsAreEqualToControl(ConstantsSet constantsSet, IDictionary<string, string> taskPackageGuids, CancellationToken stoppingToken)
        {
            // добавить счётчик потоков и проверить при большом количестве вызовов
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            string assertProcessedBookAreEqualControl = constantsSet.Prefix.IntegrationTestPrefix.AssertProcessedBookAreEqualControl.Value; // assert-that-processed-book-fields-are-equal-to-control-books
            double keyExistingTime = constantsSet.Prefix.IntegrationTestPrefix.AssertProcessedBookAreEqualControl.LifeTime; // 0.007
            int remaindedFieldsCount = constantsSet.Prefix.IntegrationTestPrefix.RemaindedFieldsCount.ValueInt; // 1
            int testResultsField2 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField2.ValueInt; // 2
            int testResultsField3 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField3.ValueInt; // 3
            int remaindedFields = -1;

            foreach (var g in taskPackageGuids)
            {
                (string taskPackageGuid, string vG) = g;

                Logs.Here().Information("taskPackageGuid {0} was fetched.", taskPackageGuid);

                IDictionary<string, TextSentence> fieldValuesControl = await _cache.FetchHashedAllAsync<TextSentence>(controlListOfTestBookFieldsKey);
                int fieldValuesControlCount = fieldValuesControl.Count;

                IDictionary<string, TextSentence> fieldValuesResult = await _cache.FetchHashedAllAsync<TextSentence>(taskPackageGuid);
                int fieldValuesResultCount = fieldValuesResult.Count;
                int deletedFields = 0;
                Logs.Here().Information("fieldValuesResult with count {0} was fetched from taskPackageGuid.", fieldValuesResultCount);

                // write test asserted results in the report key

                foreach (KeyValuePair<string, TextSentence> p in fieldValuesResult)
                {
                    (string fP, TextSentence vP) = p;
                    Logs.Here().Debug("Field {0} was found in taskPackageGuid and will be deleted in key {1}.", fP, controlListOfTestBookFieldsKey);

                    bool result0 = await CheckAssertFieldsAreEqualToControlAndEternal(constantsSet, fP, vP, stoppingToken);

                    if (result0)
                    {
                        bool result1 = await _cache.DelFieldAsync(controlListOfTestBookFieldsKey, fP);
                        if (result1)
                        {
                            deletedFields++;
                            Logs.Here().Debug("The comparison returned {0} and field {1} / value {2} was sucessfully deleted in key {3}.", result0, fP, vP.BookId, controlListOfTestBookFieldsKey);
                        }
                    }
                }

                remaindedFields = fieldValuesResultCount - deletedFields;

                bool result2 = await _cache.IsKeyExist(controlListOfTestBookFieldsKey);
                if (!result2)
                {
                    Logs.Here().Information("There are no remained fields in key {0}. Test is completed (but does not know about it).", controlListOfTestBookFieldsKey);

                    // исчезнувший ключ - не вполне надёжное средство оповещения,
                    // поэтому надо записать ещё ключ testResultsKey1 и тест дополнительно проверит его
                    // WriteHashedAsync<TK, TV>(string key, IEnumerable<KeyValuePair<TK, TV>> fieldValues, double ttl)

                    IDictionary<int, int> fieldValues = new Dictionary<int, int>();

                    fieldValues.Add(remaindedFieldsCount, remaindedFields);
                    fieldValues.Add(testResultsField2, fieldValuesResultCount);
                    fieldValues.Add(testResultsField3, fieldValuesControlCount);

                    await _cache.WriteHashedAsync<int, int>(assertProcessedBookAreEqualControl, fieldValues, keyExistingTime);

                    return true;
                }
            }

            bool assertedResult = false;
            if (remaindedFields == 0)
            {
                assertedResult = true;
            }
            return assertedResult;
        }

        // 
        private async Task<bool> CheckAssertFieldsAreEqualToControlAndEternal(ConstantsSet constantsSet, string fP, TextSentence vP, CancellationToken stoppingToken)
        {
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.EternalBookPlainTextHashesLog.Value; // key-book-plain-texts-hashes-versions-list
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000

            // здесь сравнить bookId, bookGuid и bookHash книг 3
            int bookId = vP.BookId;
            int languageId = vP.LanguageId;
            int bookHashVersion = vP.HashVersion;

            TextSentence bookPlainFromControl = await _cache.FetchHashedAsync<TextSentence>(controlListOfTestBookFieldsKey, fP);
            //Logs.Here().Information("{@C}.", new { Value = bookPlainFromControl });
            bool bookIdComparingWithControl = bookPlainFromControl.BookId == vP.BookId;
            bool bookGuidComparingWithControl = String.Equals(bookPlainFromControl.BookGuid, vP.BookGuid);

            // здесь ещё посмотреть и сравнить в вечном логе
            // здесь надо перевести bookId в вид со сдвигом
            int fieldBookIdWithLanguageId = bookId + languageId * chapterFieldsShiftFactor;
            Logs.Here().Debug("Check FetchHashedAsync<int, List<TextSentence>> - key {0}, field {1}, element {2}.", keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, bookHashVersion);
            List<TextSentence> bookPlainTextsVersions = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId);
            TextSentence bookPlainFromEternalLog = bookPlainTextsVersions[bookHashVersion];
            //Logs.Here().Information("{@E} is bookPlainTextsVersions[{1}].", new { BookPlainFromEternalLog = bookPlainFromEternalLog }, bookHashVersion);

            bool bookIdComparingWithEternal = bookPlainFromEternalLog.BookId == vP.BookId;
            bool bookGuidComparingWithEternal = String.Equals(bookPlainFromEternalLog.BookGuid, vP.BookGuid);
            bool bookHashComparingWithEternal = String.Equals(bookPlainFromEternalLog.BookPlainTextHash, vP.BookPlainTextHash);
            bool bookHashVersionComparingWithEternal = bookPlainFromEternalLog.HashVersion == vP.HashVersion;

            bool result0 = bookIdComparingWithControl && bookGuidComparingWithControl && bookIdComparingWithEternal && bookGuidComparingWithEternal && bookHashComparingWithEternal && bookHashVersionComparingWithEternal;

            return result0;
        }


        //public void SetIsTestInProgress(bool init_isTestInProgress)
        //{
        //    Logs.Here().Information("SetIsTestInProgress will changed _isTestInProgress {0} on {1}.", _isTestInProgress, init_isTestInProgress);
        //    _isTestInProgress = init_isTestInProgress;
        //    Logs.Here().Information("New state of _isTestInProgress is {0}.", _isTestInProgress);
        //}


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
