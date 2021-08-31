using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Shared.Library.Models;
using Shared.Library.Services;

// потом на диаграмме можно выстроить всю цепочку в линию, а по времени совместить с другими цепочками ниже
// можно генерировать выходной отчёт в формате диаграммы - более реально - тайм-лайн для веба

// сделать вывод результатов отчёта - пока что текстом в консоли, но чтобы можно было легко посмотреть

namespace BackgroundDispatcher.Services
{
    public interface ITestReportIsFilledOutWithTimeImprints
    {
        bool SetTestScenarioNumber(int testScenario);
        Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, TestReport.TestReportStage sendingTestTimingReportStage);
        Task<bool> ViewReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario, List<TestReport.TestReportStage> testTimingReportStages);
        Task<(List<TestReport.TestReportStage>, string)> ConvertDictionaryWithReportToList(ConstantsSet constantsSet, long tsTest99, int testScenario);
        Task<bool> WriteTestScenarioReportsList(KeyType eternalTestTimingStagesReportsLog, List<TestReport> theScenarioReports, List<TestReport.TestReportStage> testTimingReportStages, long tsTest99, int testScenario, string testReportHash);
    }

    public class TestReportIsFilledOutWithTimeImprints : ITestReportIsFilledOutWithTimeImprints
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public TestReportIsFilledOutWithTimeImprints(
            IHostApplicationLifetime applicationLifetime,
            IAuxiliaryUtilsService aux,
            ICacheManagerService cache)
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _aux = aux;
            _cache = cache;
            //_stopWatchTest = new Stopwatch();
            //_stopWatchWork = new Stopwatch();
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestReportIsFilledOutWithTimeImprints>();

        private int _stageReportFieldCounter;
        private int _currentTestSerialNum;
        private int _callingNumOfAddStageToTestTaskProgressReport;
        //private Stopwatch _stopWatchTest;
        //private Stopwatch _stopWatchWork;

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

        public bool SetTestScenarioNumber(int testScenario)
        {
            if (testScenario > 0)
            {
                _currentTestSerialNum = testScenario;
                return true;
            }
            return false;
        }

        // этот метод вызывается только из рабочих методов других классов
        // он получает имя рабочего метода currentMethodName, выполняющего тест в данный момент, номер тестовой цепочки и остальное в TestReport.TestReportStage testTimingReportStage
        // ещё он собирает собственный счётчик вызовов, определяет номер шага отчёта и делает засечки с двух таймеров
        public async Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, TestReport.TestReportStage sendingTestTimingReportStage)
        {
            // изменить названия точек методов на номера точек по порядку - можно синтезировать при печати имяМетода-1
            // сделать сначала текстовую таблицу, без тайм - лайн
            // для тайм-лайн надо синтезировать времена выполнения всего метода с данным номером задачи - начало, важный узел и конец метода

            string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report
            //double currentTestReportKeyExistingTime = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.LifeTime; // ?
            double currentTestReportKeyExistingTime = 1;
            Logs.Here().Information("AddStageToTestTaskProgressReport was called by {0}.", sendingTestTimingReportStage.MethodNameWhichCalled);

            // определяем собственно номер шага текущего отчёта
            int count = Interlocked.Increment(ref _stageReportFieldCounter);

            // ещё полезно иметь счётчик вызовов - чтобы определить многопоточность
            int lastCountStart = Interlocked.Increment(ref _callingNumOfAddStageToTestTaskProgressReport);
            Logs.Here().Information("AddStageToTestTaskProgressReport started {0} time. Stage = {1}.", lastCountStart, count);

            // первый параметр - isRequestForTestStopWatch = true, Work - false
            // второй параметр - запустить/прочитать = true, остановить/сбросить = false
            // оба секундомера запускаются и останавливаются в классе TestOfComplexIntegrityMainService, здесь только считываются
            // возвращается засечка времени в мсек, без остановки секундомера
            //long tsWork = StopwatchesControlAndRead(false, true);
            //long tsTest = StopwatchesControlAndRead(true, true);

            // ещё можно получать и записывать номер потока, в котором выполняется этот метод
            TestReport.TestReportStage testTimingReportStage = new TestReport.TestReportStage()
            {
                // номер шага с записью отметки времени теста, он же номер поля в ключе записи текущего отчёта
                StageReportFieldCounter = count,
                // серийный номер единичной цепочки теста - обработка одной книги от события From
                ChainSerialNumber = sendingTestTimingReportStage.ChainSerialNumber,
                // номер теста в пакете тестов по данному сценарию, он же индекс в списке отчётов
                TheScenarioReportsCount = _currentTestSerialNum,
                // хеш шага отчёта
                StageReportHash = "",
                // отметка времени от старта рабочей цепочки
                TsWork = sendingTestTimingReportStage.TsWork,
                // отметка времени от начала теста
                // можно запросить время (тоже рабочее) прямо отсюда и разница будет временем выполнения записи шага -
                // непонятно, зачем, но один раз интересно посмотреть
                TsTest = sendingTestTimingReportStage.TsTest,
                // имя вызвавшего метода, полученное в параметрах
                MethodNameWhichCalled = sendingTestTimingReportStage.MethodNameWhichCalled,
                // ключевое значение метода
                WorkActionNum = sendingTestTimingReportStage.WorkActionNum,
                // ключевое значение bool
                WorkActionVal = sendingTestTimingReportStage.WorkActionVal,
                // название ключевого значения метода
                WorkActionName = sendingTestTimingReportStage.WorkActionName,
                // описание действий метода или текущей ситуации
                WorkActionDescription = sendingTestTimingReportStage.WorkActionDescription,
                // количество одновременных вызовов принимаемого рабочего метода
                CallingCountOfWorkMethod = sendingTestTimingReportStage.CallingCountOfWorkMethod,
                // количество одновременных вызовов этого метода (AddStageToTestTaskProgressReport)
                CallingCountOfThisMethod = lastCountStart
            };

            // посчитаем хеш данных шага отчета для последующего сравнения версий
            string s0 = testTimingReportStage.StageReportFieldCounter.ToString();
            string s1 = testTimingReportStage.ChainSerialNumber.ToString();
            string s2 = testTimingReportStage.TheScenarioReportsCount.ToString(); // ?
            string s3 = testTimingReportStage.MethodNameWhichCalled;
            string s4 = testTimingReportStage.WorkActionNum.ToString();
            string s5 = testTimingReportStage.WorkActionVal.ToString();
            string s6 = testTimingReportStage.WorkActionName;
            string s7 = testTimingReportStage.WorkActionDescription;
            string s8 = "reserved1";
            string s9 = "reserved2";
            string unaitedStageData = $"{s0}-{s1}-{s2}-{s3}-{s4}-{s5}-{s6}-{s7}-{s8}-{s9}";
            testTimingReportStage.StageReportHash = AuxiliaryUtilsService.CreateMD5(unaitedStageData);

            await _cache.WriteHashedAsync<int, TestReport.TestReportStage>(currentTestReportKey, count, testTimingReportStage, currentTestReportKeyExistingTime);
            Logs.Here().Information("Method Name Which Called {0} was writen in field {1}.", testTimingReportStage.MethodNameWhichCalled, count);

            int lastCountEnd = Interlocked.Decrement(ref _callingNumOfAddStageToTestTaskProgressReport);
            Logs.Here().Information("AddStageToTestTaskProgressReport ended {0} time.", lastCountEnd);

            return true;
        }

        public async Task<(List<TestReport.TestReportStage>, string)> ConvertDictionaryWithReportToList(ConstantsSet constantsSet, long tsTest99, int testScenario)
        {
            string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report

            IDictionary<int, TestReport.TestReportStage> testTimingReportStages = await _cache.FetchHashedAllAsync<int, TestReport.TestReportStage>(currentTestReportKey);
            int testTimingReportStagesCount = testTimingReportStages.Count;

            TestReport.TestReportStage[] testTimingReportStagesArray = new TestReport.TestReportStage[testTimingReportStagesCount + 1];
            string[] stageHash = new string[testTimingReportStagesCount + 1];

            testTimingReportStagesArray[0] = new();
            //testTimingReportStagesList.Add(new());
            stageHash[0] = "";

            for (int i = 1; i < testTimingReportStagesCount + 1; i++) //
            {
                // записать сумму хешей или хеш хешей в базовый класс
                stageHash[i] = testTimingReportStages[i].StageReportHash;
                testTimingReportStagesArray[i] = testTimingReportStages[i];
            }

            string testReportForHash = String.Join("-", stageHash);
            string testReportHash = AuxiliaryUtilsService.CreateMD5(testReportForHash);
            return (testTimingReportStagesArray.ToList(), testReportHash);
        }

        public async Task<bool> WriteTestScenarioReportsList(KeyType eternalTestTimingStagesReportsLog, List<TestReport> theScenarioReports, List<TestReport.TestReportStage> testTimingReportStages, long tsTest99, int testScenario, string testReportHash)
        {
            string currentTestDescription = $"Current test report for Scenario {testScenario}";

            TestReport theScenarioReport = new TestReport()
            {
                TestScenarioNum = testScenario,
                Guid = currentTestDescription,
                TestId = testScenario,
                TestReportStages = testTimingReportStages,
                TestReportHash = testReportHash
            };

            theScenarioReports.Add(theScenarioReport);

            await _cache.WriteHashedAsync<int, List<TestReport>>(eternalTestTimingStagesReportsLog.Value, testScenario, theScenarioReports, eternalTestTimingStagesReportsLog.LifeTime);

            return true;
        }

        // метод выводит таблицу с результатами текущего отчёта о времени прохождения теста по контрольным точкам
        public async Task<bool> ViewReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario, List<TestReport.TestReportStage> testTimingReportStages)
        {
            // вынести в отдельный метод и преобразовать словарь в список
            // и ещё метод в список базового класса

            // сначала перегнать все данные в печатный массив, определить максимумы,
            // сделать нормировку масштабирования для заданной ширины печати тайм - лайна
            // привязать названия и значения к свободным местам тайм-лайн
            // попробовать выводить в две строки - одна данные, вторая тайм-лайн
            // событие таймера выделять отдельной строкой и начинать отсчёт времени заново

            // изменить названия точек методов на номера точек по порядку - можно синтезировать при печати имяМетода-1
            // сделать сначала текстовую таблицу, без тайм - лайн
            // для тайм-лайн надо синтезировать времена выполнения всего метода с данным номером задачи - начало, важный узел и конец метода
            // тогда будет понятно, зачем нужен тайм - лайн

            //string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report

            //char ttt = '\u2588'; // █ \u2588 ▮ U+25AE ▯ U+25AF 
            //int timeScaling = 5;

            int screenFullWidthLinesCount = 228;
            char screenFullWidthTopLineChar = '-';
            char screenFullWidthBetweenLineChar = '\u1C79'; // Ol Chiki Gaahlaa Ttuddaag --> ᱹ

            // проверить наличие ключа
            // проверить наличие словаря
            // проверить, что словарь не нулевой

            //IDictionary<int, TestReport.TestReportStage> testTimingReportStages = await _cache.FetchHashedAllAsync<int, TestReport.TestReportStage>(currentTestReportKey);
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


        // первый параметр - isRequestForTestStopWatch = true, Work - false
        // второй параметр - запустить/прочитать = true, остановить/сбросить = false
        // возвращается засечка времени в мсек, без остановки секундомера
        //public long StopwatchesControlAndRead(bool isRequestedStopWatchTest, bool control)
        //{
        //    Stopwatch stopWatch;
        //    if (isRequestedStopWatchTest)
        //    {
        //        stopWatch = _stopWatchTest;
        //    }
        //    else
        //    {
        //        stopWatch = _stopWatchWork;

        //    }
        //    // надо проверять текущее состояние секундомера перед его изменением
        //    bool currentState = stopWatch.IsRunning;

        //    string stopWatchState;

        //    //// если надо запустить и он остановлен (запускаем)
        //    //if (control && !currentState)
        //    //{
        //    //    _stopWatchTest.Start();
        //    //    stopWatchState = "was started";
        //    //}
        //    //// если надо остановить и он запущен (останавливаем)
        //    //if (!control && currentState)
        //    //{
        //    //    _stopWatchTest.Stop();
        //    //    stopWatchState = "was stopped";
        //    //}
        //    //// если надо запустить и он уже запущен (показываем текущее время)
        //    //if (control && currentState)
        //    //{
        //    //    stopWatchState = "has beed started already";
        //    //}
        //    //// если надо остановить и он уже остановлен (сбрасываем)
        //    //if (!control && !currentState)
        //    //{
        //    //    stopWatchState = "had been already stopped and was just reset");
        //    //    _stopWatchTest.Reset();
        //    //}

        //    // требуется запустить секундомер - прислали true
        //    if (control)
        //    {
        //        // если надо запустить и он уже запущен (показываем текущее время)
        //        if (currentState)
        //        {
        //            stopWatchState = "has beed started already";
        //        }
        //        // если надо запустить и он остановлен (запускаем)
        //        else
        //        {
        //            //stopWatch = Stopwatch.StartNew();
        //            stopWatch.Start();
        //            stopWatchState = "has been started";
        //        }
        //    }
        //    // требуется остановить секундомер - прислали false
        //    else
        //    {
        //        // если надо остановить и он запущен (останавливаем)
        //        if (currentState)
        //        {
        //            stopWatch.Stop();
        //            stopWatchState = "has been stopped";
        //        }
        //        // если надо остановить и он уже остановлен (сбрасываем)
        //        else
        //        {
        //            stopWatch.Reset();
        //            stopWatchState = "had been already stopped and was just reset";
        //        }
        //    }

        //    //TimeSpan tsControl = stopWatch.Elapsed;
        //    long stopwatchMeasuredTime = stopWatch.ElapsedMilliseconds; // double Elapsed.TotalMilliseconds
        //    Logs.Here().Debug("Stopwatch {0} {1}. It shows {2} msec.", nameof(stopWatch), stopWatchState, stopwatchMeasuredTime);

        //    return stopwatchMeasuredTime;
        //}

    }
}
