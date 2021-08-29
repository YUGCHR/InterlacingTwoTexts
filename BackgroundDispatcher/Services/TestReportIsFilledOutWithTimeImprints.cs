using System.Diagnostics;
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
        public bool SetTestScenarioNumber(int testScenario);
        public Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, TestReport.TestReportStage sendingTestTimingReportStage);
        //public long StopwatchesControlAndRead(bool isRequestedStopWatchTest, bool control);
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
            // надо добавить ещё одну строковую переменную - описание ситуации вокруг
            // будет значение переменной, описание переменной и описание ситуации
            // и ещё добавить bool переменную, чтобы не конвертировать?

            // попробовать разные варианты размещения проверки тест сейчас или нет -
            // в рабочих методах возможны два варианта (тут вряд ли есть смысл проверять)

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
                // отметка времени от старта рабочей цепочки
                TsWork = sendingTestTimingReportStage.TsWork,
                // отметка времени от начала теста
                // можно запросить время (тоже рабочее) прямо отсюда и разница будет временем выполнения записи шага -
                // непонятно, зачем, но один раз интересно посмотреть
                TsTest = sendingTestTimingReportStage.TsTest,
                // имя вызвавшего метода, полученное в параметрах
                MethodNameWhichCalled = sendingTestTimingReportStage.MethodNameWhichCalled,
                // ключевое слово, которым делится вызвавший метод - что-то о его занятиях
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

            await _cache.WriteHashedAsync<int, TestReport.TestReportStage>(currentTestReportKey, count, testTimingReportStage, currentTestReportKeyExistingTime);
            Logs.Here().Information("Method Name Which Called {0} was writen in field {1}.", testTimingReportStage.MethodNameWhichCalled, count);

            int lastCountEnd = Interlocked.Decrement(ref _callingNumOfAddStageToTestTaskProgressReport);
            Logs.Here().Information("AddStageToTestTaskProgressReport ended {0} time.", lastCountEnd);

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
