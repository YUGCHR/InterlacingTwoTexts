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
    public interface ITestTimeImprintsReportIsFilledOut
    {
        bool SetTestScenarioNumber(int testScenario);
        Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, TestReport.TestReportStage sendingTestTimingReportStage);
        Task<bool> ViewComparedReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario, List<TestReport.TestReportStage> testTimingReportStages);
        Task<(List<TestReport.TestReportStage>, string)> ConvertDictionaryWithReportToList(ConstantsSet constantsSet);
        Task<(List<TestReport>, int)> ProcessingReportsForReferenceAssignment(ConstantsSet constantsSet, List<TestReport> theScenarioReports, List<TestReport.TestReportStage> testTimingReportStages, int reportsWOversionsCount, int testScenario, string testReportHash);
        bool Reset_stageReportFieldCounter();
    }

    public class TestTimeImprintsReportIsFilledOut : ITestTimeImprintsReportIsFilledOut
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public TestTimeImprintsReportIsFilledOut(
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

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestTimeImprintsReportIsFilledOut>();

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

        public bool Reset_stageReportFieldCounter()
        {
            // сбросить счётчик текущего шага тестового отчёта по таймингу - только он в другом классе
            _stageReportFieldCounter = 0;

            if (_stageReportFieldCounter == 0)
            {
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

            // определяем собственно номер шага текущего отчёта
            int count = Interlocked.Increment(ref _stageReportFieldCounter);

            // ещё полезно иметь счётчик вызовов - чтобы определить многопоточность
            int lastCountStart = Interlocked.Increment(ref _callingNumOfAddStageToTestTaskProgressReport);
            Logs.Here().Information("AddStageToTestTaskProgressReport started {0} time. Stage = {1}.", lastCountStart, count);

            // ещё можно получать и записывать номер потока, в котором выполняется этот метод
            TestReport.TestReportStage testTimingReportStage = new TestReport.TestReportStage()
            {
                // StageId - номер шага с записью отметки времени теста, он же номер поля в ключе записи текущего отчёта
                StageReportFieldCounter = count,
                // серийный номер единичной цепочки теста - обработка одной книги от события From
                ChainSerialNumber = sendingTestTimingReportStage.ChainSerialNumber,
                // Current Test Serial Number for this Scenario - номер теста в пакете тестов по данному сценарию, он же индекс в списке отчётов
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
                // номер контрольной точки в методе, где считывается засечка времени
                ControlPointNum = sendingTestTimingReportStage.ControlPointNum,
                // количество одновременных вызовов принимаемого рабочего метода
                CallingCountOfWorkMethod = sendingTestTimingReportStage.CallingCountOfWorkMethod,
                // количество одновременных вызовов этого метода (AddStageToTestTaskProgressReport)
                CallingCountOfThisMethod = lastCountStart
            };

            // посчитаем хеш данных шага отчета для последующего сравнения версий 
            KeyType currentTestReportKeyTime = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey; // storage-key-for-current-test-report / 0.01
            bool result = await AddHashToStageAndWrite(testTimingReportStage, currentTestReportKeyTime);

            int lastCountEnd = Interlocked.Decrement(ref _callingNumOfAddStageToTestTaskProgressReport);
            Logs.Here().Information("AddStageToTestTaskProgressReport ended {0} time.", lastCountEnd);

            return result;
        }

        private async Task<bool> AddHashToStageAndWrite(TestReport.TestReportStage testTimingReportStage, KeyType currentTestReportKeyTime)
        {
            string currentTestReportKey = currentTestReportKeyTime.Value; // storage-key-for-current-test-report
            //double currentTestReportKeyExistingTime = currentTestReportKeyTime.LifeTime; // 0.01
            double currentTestReportKeyExistingTime = 1;
            int count = testTimingReportStage.StageReportFieldCounter;
            Logs.Here().Information("AddStageToTestTaskProgressReport was called by {0} on chain {1}.", testTimingReportStage.MethodNameWhichCalled, testTimingReportStage.ChainSerialNumber);

            string s1 = testTimingReportStage.ChainSerialNumber.ToString();
            //string s2 = testTimingReportStage.TheScenarioReportsCount.ToString(); // ?
            string s3 = $"{testTimingReportStage.MethodNameWhichCalled}-{testTimingReportStage.ControlPointNum}";
            //string s4 = testTimingReportStage.WorkActionNum.ToString();
            string s5 = $"{testTimingReportStage.WorkActionName}-{testTimingReportStage.WorkActionNum} ({testTimingReportStage.WorkActionVal})";
            //string s6 = testTimingReportStage.WorkActionName;
            //string s7 = testTimingReportStage.ControlPointNum;
            string unitedStageData = $"{s1}_{s3}_{s5}";

            testTimingReportStage.StageReportHash = AuxiliaryUtilsService.CreateMD5(unitedStageData);

            //string stageReportHash = AuxiliaryUtilsService.CreateMD5(unitedStageData);
            //testTimingReportStage.StageReportHash = stageReportHash;
            //Console.WriteLine(("").PadRight(150, '*'));
            //Logs.Here().Information("{0}). unitedStageData {1} has hash {2}.", count, unitedStageData, stageReportHash);
            //Console.WriteLine(("").PadRight(150, '+'));

            await _cache.WriteHashedAsync<int, TestReport.TestReportStage>(currentTestReportKey, count, testTimingReportStage, currentTestReportKeyExistingTime);
            Logs.Here().Information("Method Name Which Called {0} was writen in field {1}.", testTimingReportStage.MethodNameWhichCalled, count);

            return true;
        }

        // метод получает только константы, самостоятельно достаёт из них нужный ключ,
        // достаёт сохранённые шаги с засечками времени в словарь,
        // преобразовывает в список (через массив) и собирает хеши всех шагов для вычисления общего хеша отчёта,
        // возвращает (отсортированный список) и хеш всего отчёта
        // дополнительно тут подходящее место отсортировать по номеру цепочки, а зачем по номеру шага
        // 
        public async Task<(List<TestReport.TestReportStage>, string)> ConvertDictionaryWithReportToList(ConstantsSet constantsSet)
        {
            string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report

            // нумерация ключей словаря начинается с 1 (так случилось)
            IDictionary<int, TestReport.TestReportStage> testTimingReportStages = await _cache.FetchHashedAllAsync<int, TestReport.TestReportStage>(currentTestReportKey);
            int testTimingReportStagesCount = testTimingReportStages.Count;

            //Logs.Here().Information("testTimingReportStages {@D} length {0}.", new { IDictionary = testTimingReportStages }, testTimingReportStagesCount);

            TestReport.TestReportStage[] testTimingReportStagesArray = new TestReport.TestReportStage[testTimingReportStagesCount];
            string[] stageHash = new string[testTimingReportStagesCount];

            // или <= testTimingReportStagesCount - индексы начинаются с 1
            for (int i = 1; i < testTimingReportStagesCount + 1; i++) //
            {
                // записать сумму хешей или хеш хешей в базовый класс
                // сдвигаем индексы к нулю
                stageHash[i - 1] = testTimingReportStages[i].StageReportHash;
                testTimingReportStagesArray[i - 1] = testTimingReportStages[i];
                //Logs.Here().Information("for i = {0} to end {1} - List = {2}, Array[i] = {3}, stageHash = {4}.", i, testTimingReportStagesCount, testTimingReportStages[i].StageReportFieldCounter, testTimingReportStagesArray[i-1].StageReportFieldCounter, stageHash[i-1]);
            }

            // будем сортировать массив хешей перед слиянием и вычислением общего хеша
            Array.Sort(stageHash);

            List<TestReport.TestReportStage> testTimingReportStagesList = testTimingReportStagesArray.OrderBy(x => x.ChainSerialNumber).ThenBy(x => x.StageReportFieldCounter).ToList();
            //Logs.Here().Information("testTimingReportStagesList {@D} length {0}.", new { List = testTimingReportStagesList }, testTimingReportStagesList.Count);
            // важно отсортировать до вычисления общего хеша
            string testReportForHash = String.Join("_", stageHash);
            string testReportHash = AuxiliaryUtilsService.CreateMD5(testReportForHash);

            return (testTimingReportStagesList, testReportHash);
        }

        // WriteTestScenarioReportsList
        public async Task<(List<TestReport>, int)> ProcessingReportsForReferenceAssignment(ConstantsSet constantsSet, List<TestReport> ReportsListOfTheScenario, List<TestReport.TestReportStage> testTimingReportStages, int reportsWOversionsCount, int testScenario, string testReportHash)
        {
            // 3/5 - взять из констант, назвать типа количество отчётов для начала проведения сравнения - ReportsCountToStartComparison
            int reportsCountToStartComparison = 3;

            string currentTestDescription = $"Current test report for Scenario {testScenario}";

            int equalReportsCount = ExistingReportsComparison(reportsCountToStartComparison, ReportsListOfTheScenario, testReportHash, reportsWOversionsCount);

            TestReport theReportOfTheScenario = new TestReport()
            {
                TestScenarioNum = testScenario,
                Guid = currentTestDescription,
                TheScenarioReportsCount = testTimingReportStages[0].TheScenarioReportsCount,
                TestReportStages = testTimingReportStages,
                ThisReportHash = testReportHash
            };


            //считать сколько именно совпало, чтобы понять, какие удалять - тоже с конца, перед добавлением нового
            //ещё же версии завести и сравнивать -нужен список версий или где-то хранить максимальную?
            //наверное, максимальная будет всегда в текущем эталоне
            //когда появляется новый эталон, со старой версией ещё есть один отчёт, кроме эталона, то есть, старый эталон сдвинуть вперёд, а второй отчёт найти и удалить
            //этот отчёт будет последним с версией
            //добавить серийный номер теста -или считывать номер последнего теста в списке и прибавлять единицу или гуид?
            //серийный номер хорошо выводить в таблице, чтобы было заметно, как располагаются отчёты в списке


            // в любом случае ставим последний отчёт в конец списка
            // не в любом, а если отчётов мало вообще или не хватает одинаковых
            ReportsListOfTheScenario.Add(theReportOfTheScenario);

            // сравниваем количество одинаковых отчётов с константой и если равно (больше вроде бы не может быть),
            // то сохраняем эталонный отчёт в нулевой индекс
            // предварительно надо проверить, что там сейчас - и, если эталон с другой (меньшей) версией,
            // то вытолкнуть (вставить) его в первый индекс (не затереть первый)
            if (equalReportsCount >= reportsCountToStartComparison)
            {
                // поменять индексы на 0 и внутри тоже
                List<TestReport.TestReportStage> testTimingReportStagesForRef = theReportOfTheScenario.TestReportStages.ConvertAll(x => { x.TheScenarioReportsCount = 0; return x; });
                // создать новый TestReport theScenarioRefReport
                TestReport theScenarioReportRef = new TestReport()
                {
                    TestScenarioNum = testScenario,
                    Guid = currentTestDescription,
                    TheScenarioReportsCount = testTimingReportStagesForRef[0].TheScenarioReportsCount,
                    TestReportStages = testTimingReportStagesForRef,
                    ThisReportHash = testReportHash
                };
                ReportsListOfTheScenario.RemoveAt(0);
                ReportsListOfTheScenario.Insert(0, theReportOfTheScenario);
            }

            bool res = await WriteTestScenarioReportsList(constantsSet, testScenario, ReportsListOfTheScenario);

            // надо поменять на 0 номера в каждом элементе во внутреннем списке - данные в таблицу берутся из него

            return (ReportsListOfTheScenario, equalReportsCount);
        }

        private int ExistingReportsComparison(int reportsCountToStartComparison, List<TestReport> ReportsListOfTheScenario, string testReportHash, int reportsWOversionsCount)
        {
            // прежде чем записывать новый отчёт в список, надо узнать, не требуется ли сравнение            
            int theScenarioReportsCount = ReportsListOfTheScenario.Count;
            int equalReportsCount = 0;

            // сравниваем количество отчётов без версии, полученное в начале теста с константой
            // если оно больше, то начинаем цикл сравнения хешей отчётов, чтобы понять, сколько их набралось одинаковых
            if (reportsWOversionsCount >= reportsCountToStartComparison)
            {
                for (int i = theScenarioReportsCount - 1; i > 0; i--)
                {
                    bool reportsInPairAreEqual = String.Equals(testReportHash, ReportsListOfTheScenario[i].ThisReportHash);

                    if (reportsInPairAreEqual)
                    {
                        equalReportsCount++;
                    }
                    else
                    {
                        // если встретился отчёт с другим хешем, сразу можно выйти из цикла с проверкой - потом оформить в отдельный метод
                        // но надо посмотреть на счётчик - если нет даже двух одинаковых, то выходить совсем, иначе - уже описано
                        // return
                    }
                }
            }

            return equalReportsCount;
        }

        private async Task<bool> WriteTestScenarioReportsList(ConstantsSet constantsSet, int testScenario, List<TestReport> theScenarioReports)
        {
            KeyType eternalTestTimingStagesReportsLog = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog; // key-test-reports-timing-imprints-list

            await _cache.WriteHashedAsync<int, List<TestReport>>(eternalTestTimingStagesReportsLog.Value, testScenario, theScenarioReports, eternalTestTimingStagesReportsLog.LifeTime);

            return true;
        }

        // метод выводит таблицу с результатами текущего отчёта о времени прохождения теста по контрольным точкам
        public async Task<bool> ViewComparedReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario, List<TestReport.TestReportStage> testTimingReportStagesSource)
        {
            //List<TestReport.TestReportStage> testTimingReportStages = (from u in testTimingReportStagesSource
            //                                                           orderby u.StageReportFieldCounter
            //                                                           select u).ToList();
            List<TestReport.TestReportStage> testTimingReportStages = testTimingReportStagesSource.OrderBy(x => x.StageReportFieldCounter).ThenBy(y => y.ChainSerialNumber).ThenBy(z => z.TheScenarioReportsCount).ToList();

            // сначала перегнать все данные в печатный массив, определить максимумы,
            // сделать нормировку масштабирования для заданной ширины печати тайм - лайна
            // привязать названия и значения к свободным местам тайм-лайн
            // попробовать выводить в две строки - одна данные, вторая тайм-лайн
            // событие таймера выделять отдельной строкой и начинать отсчёт времени заново

            // изменить названия точек методов на номера точек по порядку - можно синтезировать при печати имяМетода-1
            // сделать сначала текстовую таблицу, без тайм - лайн
            // для тайм-лайн надо синтезировать времена выполнения всего метода с данным номером задачи - начало, важный узел и конец метода
            // тогда будет понятно, зачем нужен тайм - лайн

            //char ttt = '\u2588'; // █ \u2588 ▮ U+25AE ▯ U+25AF 
            //int timeScaling = 5;

            int screenFullWidthLinesCount = 228;
            char screenFullWidthTopLineChar = '-';
            char screenLineChar1C79 = '\u1C79'; // Ol Chiki Gaahlaa Ttuddaag --> ᱹ

            // проверить наличие ключа
            // проверить наличие словаря
            // проверить, что словарь не нулевой

            //IDictionary<int, TestReport.TestReportStage> testTimingReportStages = await _cache.FetchHashedAllAsync<int, TestReport.TestReportStage>(currentTestReportKey);
            int testTimingReportStagesCount = testTimingReportStages.Count;

            TestReport.TestReportStage stage1 = testTimingReportStages[1];
            int r103 = stage1.TheScenarioReportsCount;
            TestReport.TestReportStage stageLast = testTimingReportStages[testTimingReportStagesCount - 2];
            int rL05 = (int)stageLast.TsTest;

            Console.WriteLine($"\n  Timing imprint report on testScenario No: {testScenario,-3:d} | total stages in the report = {testTimingReportStagesCount,-4:d} | total test time = {(int)tsTest99,5:d} msec."); // \t

            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));
            Console.WriteLine("|{0,5}|{1,5}|{2,5}| {3,-37} | {4,8} | {5,8} | {6,8} | {7,5} | {8,8} | {9,-40} | {10,-33} |", "stage", "chain", "index", "CallingMethod-PointNum/CallingNum", "timePrev", "timeWork", "timeDlt", "W-int", "W-bool", "WorkActionName", "StageReportHash");
            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));

            int r01Prev = 1;
            int r04prev = 0;

            for (int i = 0; i <= testTimingReportStagesCount; i++) //
            {
                //if (i > 1)
                //{
                //    TestReport.TestReportStage stagePrev = testTimingReportStages[i - 1];
                //    r04prev = (int)stagePrev.TsWork;
                //}

                TestReport.TestReportStage stage = testTimingReportStages[i];
                int r01 = stage.StageReportFieldCounter;
                int r02 = stage.ChainSerialNumber;
                int r03 = stage.TheScenarioReportsCount;
                int r04 = (int)stage.TsWork;
                int r05 = (int)stage.TsTest;
                string r06 = stage.MethodNameWhichCalled;
                int r07 = stage.WorkActionNum;
                bool r08 = stage.WorkActionVal;

                static string r07Cor(int r07) => r07 < 0 ? " (N/A)" : $"- {r07}";
                //string GetWeatherDisplay(double tempInCelsius) => tempInCelsius < 20.0 ? "Cold." : "Perfect!";

                string r09 = $"{stage.WorkActionName} {r07Cor(r07)} ({r08})";
                int r10 = stage.ControlPointNum;
                int r11 = stage.CallingCountOfWorkMethod;
                int r12 = stage.CallingCountOfThisMethod;
                string r13 = stage.StageReportHash;

                string r06Num = $"{r06}-{r10} / {r11}";
                int r04delta = 0; // r04 - r04prev;

                if (r01 > r01Prev)
                {
                    Console.WriteLine(("").PadRight(180, screenLineChar1C79)); //screenFullWidthLinesCount
                }
                r01Prev = r01;

                Console.WriteLine("| {0,3:d} | {1,3:d} | {2,3:d} | {3,-37} | {4,8:d} | {5,8:d} | {6,8:d} | {7,5:d} | {8,8:b} | {9,-40} | {10,33} |", r01, r02, r03, r06Num, r04prev, r04, r04delta, r07, r08, new string(r09.Take(40).ToArray()), r13);
            }
            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, '*'));

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
                int r10 = stage.ControlPointNum;
                int r11 = stage.CallingCountOfWorkMethod;
                int r12 = stage.CallingCountOfThisMethod;

                string r06Num = $"{r06}-{i} / {r11}";
                int r04delta = r04 - r04prev;

                Console.WriteLine("| {0,5:d} | {1,5:d} | {2,-42} | {3,8:d} | {4,8:d} | {5,8:d} | {6,5:d} | {7,8:b} | {8,-54} | {9,-54} | ", r01, r02, r06Num, r04prev, r04, r04delta, r07, r08, new string(r09.Take(54).ToArray()));
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
