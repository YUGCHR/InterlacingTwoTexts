﻿using System;
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
        //bool SetTestScenarioNumber(int testScenario);
        Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, TestReport.TestReportStage sendingTestTimingReportStage);
        //Task<bool> ViewComparedReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario, List<TestReport.TestReportStage> testTimingReportStages);
        //Task<(List<TestReport.TestReportStage>, string)> ConvertDictionaryWithReportToList(ConstantsSet constantsSet);
        //Task<bool> ProcessingReportsForReferenceAssignment(ConstantsSet constantsSet, List<TestReport> ReportsListOfTheScenario, TestReport theReportOfTheScenario, TestReport theScenarioReportRef, List<TestReport.TestReportStage> testTimingReportStages, int reportsWOversionsCount, int testScenario, string testReportHash, int tsTest99);


        Task<bool> AssemblingReportsListFromSourceStages(ConstantsSet constantsSet, int testScenario, long tsTest99);


        bool Reset_stageReportFieldCounter();
        //int ExistingReportsComparison(int reportsCountToStartComparison, List<TestReport> ReportsListOfTheScenario, string testReportHash, int reportsWOversionsCount);
        //List<TestReport> FindIdenticalReportsCount(int reportsCountToStartComparison, List<TestReport> ReportsListOfTheScenario, TestReport theScenarioReportRef, int equalReportsCount);
        //Task<string> WriteTestScenarioReportsList(ConstantsSet constantsSet, int testScenario, List<TestReport> theScenarioReports);
        //Task<(List<TestReport>, bool, int)> CreateAssignedSerialNum(int testScenario, string eternalTestTimingStagesReportsLog);
    }

    public class TestTimeImprintsReportIsFilledOut : ITestTimeImprintsReportIsFilledOut
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IEternalLogSupportService _eternal;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public TestTimeImprintsReportIsFilledOut(
            IHostApplicationLifetime applicationLifetime,
            IEternalLogSupportService eternal,
            IAuxiliaryUtilsService aux,
            ICacheManagerService cache)
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _eternal = eternal;
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

        //public bool SetTestScenarioNumber(int testScenario)
        //{
        //    if (testScenario > 0)
        //    {
        //        _currentTestSerialNum = testScenario;
        //        return true;
        //    }
        //    return false;
        //}

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
            //Logs.Here().Information("AddStageToTestTaskProgressReport started {0} time. Stage = {1}.", lastCountStart, count);

            // ещё можно получать и записывать номер потока, в котором выполняется этот метод
            TestReport.TestReportStage testTimingReportStage = new TestReport.TestReportStage()
            {
                // StageId - номер шага с записью отметки времени теста, он же номер поля в ключе записи текущего отчёта
                StageReportFieldCounter = count,
                // серийный номер единичной цепочки теста - обработка одной книги от события From
                ChainSerialNumber = sendingTestTimingReportStage.ChainSerialNumber,
                // Current Test Serial Number for this Scenario - номер теста в пакете тестов по данному сценарию, он же индекс в списке отчётов
                TheScenarioReportsCount = -1,
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
            //Logs.Here().Information("AddStageToTestTaskProgressReport ended {0} time.", lastCountEnd);

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

        //-------------------------------------------------------------------------------------------------------
        //-------------------------------------------------------------------------------------------------------
        //-------------------------------------------------------------------------------------------------------
        // список тестов (каждый элемент - один проход) на поле номера сценария в ключе вечного лога
        // много одинаковых проходов хранить нет смысла -
        // после N одинаковых проходов, N+1 проход копируется в эталон и все (или только N?) одинаковые удаляются

        // MAIN - обработка отчётов, выбор эталона, формирование и запись списка, печать сводной таблицы
        // сборка отчётов из пошаговых списков и сохранение их списка в ключе
        public async Task<bool> AssemblingReportsListFromSourceStages(ConstantsSet constantsSet, int testScenario, long tsTest99)
        {
            // создали список текущих измерений контрольных точек (внутренний список отсчёта)
            (List<TestReport.TestReportStage> testTimingReportStagesList, string testReportHash) = await ConvertDictionaryWithReportToList(constantsSet);

            // получаем список отчётов по данному сценарию, чтобы в конце теста в него дописать текущий отчёт
            // также этот метод устанавливает текущую версию теста в поле класса - для использования рабочими методами
            List<TestReport> reportsListOfTheScenario = await LookAndCreateBaseReport(constantsSet, testScenario);

            bool resView = ViewListOfReportsInConsole(constantsSet, tsTest99, testScenario, reportsListOfTheScenario);


            reportsListOfTheScenario = ReportsAnalysisForReferenceAssigning(constantsSet, testScenario, reportsListOfTheScenario, testTimingReportStagesList, testReportHash);

            


            // создали новый отчёт
            // есть нужное количество больших отчётов без версий
            // проверили на совпадение с новым и сколько всего совпадающих
            // если достаточно, начали создавать эталон
            // эталон создали, присвоили версию, записали, надо удалить все лишние -
            // все без версий, одну (последнюю) с последней версией - если есть, а последнему отчёту присвоить версию эталона
            // при создании эталона рассчитать среднее время
            // версию можно проверять в первом методе, где считаем без версий -
            // сразу проверять признак эталона и версии, найти максимальную версию и её тоже сообщить
            //
            // таблица возможностей -
            // количество без версий (3 или меньше) - можно наружу отдавать только признак - да или нет
            // одинаковых без версий (3 или меньше) - можно там же проверять
            // эталон есть или нет
            // максимальная версия
            // есть ли последний с максимальной (проверить и все остальные - на целостность списка)


            //theReportOfTheScenario = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, -1, testTimingReportStagesList, testReportHash);

            // поменять индексы на 0 и внутри тоже - теперь это автоматически делается при создании итогового (в данном случае эталонного) отчёта
            //List<TestReport.TestReportStage> testTimingReportStagesForRef = theReportOfTheScenario.TestReportStages.ConvertAll(x => { x.TheScenarioReportsCount = theScenarioReportsCountRef; return x; });


            //reportsListOfTheScenario = FindIdenticalReportsCount(reportsCountToStartComparison, reportsListOfTheScenario, theScenarioReportRef, equalReportsCount);


            //reportsListOfTheScenario.Add(theReportOfTheScenario);

            string res = await WriteTestScenarioReportsList(constantsSet, testScenario, reportsListOfTheScenario);

            //-------------- формирование итоговой таблицы с полным списком отчётов по сценарию (возможно временное) --------------

            List<TestReport.TestReportStage> testTimingReportStagesListCurrent = await TheReportsConfluenceForView(constantsSet, testScenario);

            _ = ViewComparedReportInConsole(constantsSet, tsTest99, testScenario, testTimingReportStagesListCurrent);//testTimingReportStagesList);

            return true;
        }

        // получаем список отчётов по данному сценарию, чтобы в конце теста в него дописать текущий отчёт
        // также этот метод устанавливает текущую версию теста в поле класса - нет
        private async Task<List<TestReport>> LookAndCreateBaseReport(ConstantsSet constantsSet, int testScenario)
        {
            int fieldBookIdWithLanguageId = testScenario;
            string eternalTestTimingStagesReportsLog = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog.Value; // key-test-reports-timing-imprints-list

            (List<TestReport> reportsListOfTheScenario, int reportsListOfTheScenarioCount) = await _eternal.EternalLogAccess<TestReport>(eternalTestTimingStagesReportsLog, fieldBookIdWithLanguageId);

            if (reportsListOfTheScenarioCount == 0)
            {
                // надо создать пустой первый элемент (вместо new TestReport()), который потом можно заменить на эталонный
                List<TestReport.TestReportStage> testReportStages = new List<TestReport.TestReportStage>()
                {
                    new TestReport.TestReportStage()
                    {
                        TheScenarioReportsCount = 0
                    }
                };

                TestReport testReportForScenario = CreateNewTestReport(testScenario, 0, false, 0, testReportStages, "");
                // записываем пустышку, только если список пуст
                reportsListOfTheScenario.Add(testReportForScenario);
            }
            return reportsListOfTheScenario;
        }

        // метод создаёт и возвращает новый отчёт TestReport testReportForScenario для списка отчётов, записывая в него -
        // testScenario - номер сценария (он же - поле в ключе списков отчётов),
        // theScenarioReportsCount - номер отчёта в этом сценарии (он же индекс этого нового отчёта в списке),
        // isThisReportTheReference - флаг, является ли этот отчёт эталоном,
        // thisReporVersion - версия отчёта (для эталона и последнего сохраняемого, даже если совпадает с эталоном),
        // testReportStages - собственно свежий список контрольных точек последнего отчёта,
        // thisReportHash - хеш этого списка (без времени точек)
        // кроме того, внутри метода создаётся описание - обычный отчёт или эталонный и
        // номер отчёта (индекс) заносится во все элементы внутреннего списка контрольных точек - возможно, временно - для отображения в таблице сразу всего списка отчётов по сценарию
        // в дальнейшем передать сюда ещё константы для теста описания
        private TestReport CreateNewTestReport(int testScenario, int theScenarioReportsCount, bool isThisReportTheReference, int thisReporVersion, List<TestReport.TestReportStage> testReportStages, string thisReportHash)
        {
            string referenceTestDescription = $"Reference test report for Scenario {testScenario}";
            string currentTestDescription = $"Current test report for Scenario {testScenario}";
            string testDescription(bool isThisReportTheReference) => isThisReportTheReference ? referenceTestDescription : currentTestDescription;

            Logs.Here().Information("Current test report was created - Scenario {0} Index {1}, isRef {2}, Version {3}, Stages {4}, Hash {5}.", testScenario, theScenarioReportsCount, isThisReportTheReference, thisReporVersion, testReportStages.Count, thisReportHash);

            TestReport testReportForScenario = new TestReport()
            {
                TestScenarioNum = testScenario,
                Guid = testDescription(isThisReportTheReference),
                TheScenarioReportsCount = theScenarioReportsCount,
                IsThisReportTheReference = isThisReportTheReference,
                ThisReporVersion = thisReporVersion,
                TestReportStages = testReportStages.ConvertAll(x => { x.TheScenarioReportsCount = theScenarioReportsCount; return x; }),
                ThisReportHash = thisReportHash
            };
            return testReportForScenario;
        }

        // метод сравнивает предыдущие отчёты с текущим (в виде его хеша)
        // и на выходе возвращает инструкции, что делать дальше, варианта два (уже три) -
        // 1 - bool false, int 0 - сформировать и записать текущий отчёт в конец списка
        // 2 - bool true, int any - сформировать из текущего отчёта эталонный, дать ему версию +1 от int, записать его в нулевой индекс,
        // присвоить текущему обычному отчету ту же версию, что и эталонному и записать его в конец списка
        // 3 - bool false, int > 0 - эталон не создавать, присвоить текущему обычному отчету версию int и записать его в конец списка 
        // внутри себя метод удалит лишние отчёты из списка при необходимости
        private List<TestReport> ReportsAnalysisForReferenceAssigning(ConstantsSet constantsSet, int testScenario, List<TestReport> reportsListOfTheScenario, List<TestReport.TestReportStage> testTimingReportStagesList, string testReportHash)
        {
            // 3/5 - взять из констант, назвать типа количество отчётов для начала проведения сравнения - ReportsCountToStartComparison
            int reportsCountToStartComparison = 3;
            // достаточное количество последовательных совпадающих отчётов, чтобы удалить дальнейшие несовпадающие
            int equalReportsCountToRemoveDifferents = 2;
            // флаг (признак), является ли этот отчёт действующим эталоном
            // нужен ли признак, что он был эталоном или это и так будет понятно по номеру версии?
            bool isThisReportTheReference = false;
            // выходное значение метода - надо ли этот отчёт сделать эталонным
            bool thisReportMustBecomeReference = false;
            // счётчик количества отчётов без версий (с нулевой версией) - для определения готовности к спариванию
            int reportsWOversionsCount = 0;
            // счётчик одинаковых отчётов, чтобы определить, что пришло время создавать эталон
            int equalReportsCount = 0;
            bool wereConsistentlyEqual = true;
            int refIndex = 0;
            int maxVersion = 0;
            // если в списке уже что-то есть, кроме добавленной пустышки в пустой, изучаем элементы списка
            int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;
            if (reportsListOfTheScenarioCount > 1)
            {
                // тут проверяем нулевой элемент на эталон и, если да, назначаем окончательную макс версию
                // собственно, остальные версии уже не интересуют, они (должны быть) заведомо меньше

                // на выходе возвращает инструкции, что делать дальше, варианта три -
                // 1 - bool false, int 0 - эталон не создавать, сформировать и записать текущий отчёт в конец списка
                // 2 - bool true, int any - сформировать из текущего отчёта эталонный, дать ему версию +1 от int, записать его в нулевой индекс,
                // присвоить текущему обычному отчету ту же версию, что и эталонному и записать его в конец списка
                // 3 - bool false, int > 0 - эталон не создавать, присвоить текущему обычному отчету версию int и записать его в конец списка
                // фактически, первый и третий вариант одинаковые

                isThisReportTheReference = reportsListOfTheScenario[refIndex].IsThisReportTheReference;
                if (isThisReportTheReference)
                {
                    maxVersion = reportsListOfTheScenario[refIndex].ThisReporVersion;
                    bool theReportsWithRefAreEqual = String.Equals(testReportHash, reportsListOfTheScenario[0].ThisReportHash);
                    if (theReportsWithRefAreEqual)
                    {
                        // действующий эталон устраивает, новый делать не надо, текущий отчёт записать в конец с версией этого эталона
                        TestReport theReportOfTheScenario = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, maxVersion, testTimingReportStagesList, testReportHash);
                        reportsListOfTheScenario.Add(theReportOfTheScenario);

                        return reportsListOfTheScenario;
                    }
                    // здесь находимся, потому что эталон есть, но он устарел - новый отчёт(ы) с ним не совпадает, надо узнавать насчёт создания нового
                }
                // а здесь находимся, потому что эталона нет, надо узнавать насчёт его создания 
                // ситуации одинаковые и будут отличаться на выходе только значением версии -
                // 0, если эталона не было и больше нуля, если эталон устарел

                for (int i = reportsListOfTheScenarioCount - 1; i > 0; i--)
                {
                    // проверяем очередной отчёт из списка на наличие версии, если её нет, идём внутрь (нет, уже не будем - если версия есть, будем искать максимальную)
                    if (reportsListOfTheScenario[i].ThisReporVersion == 0)
                    {
                        // увеличиваем счётчик отчётов без версий (типа, свежих и несравненных)
                        reportsWOversionsCount++;

                        // сравниваем текущий хеш с проверяемым отчётом, если хеши одинаковые, увеличиваем счётчик совпадающих отчётов
                        // тут ещё надо бы искать совпадающие подряд, а если раз не совпало, дальше уже не искать
                        bool reportsInPairAreEqual = String.Equals(testReportHash, reportsListOfTheScenario[i].ThisReportHash);
                        if (reportsInPairAreEqual && wereConsistentlyEqual)
                        {
                            equalReportsCount++;
                        }
                        else
                        {
                            wereConsistentlyEqual = false;
                        }
                    }
                }

                // рассмотрим варианты - 
                // 1 reportsWOversionsCount < reportsCountToStartComparison - ничего не делаем, выходим при возможности
                // 2 reportsWOversionsCount >=, equalReportsCount < reportsCountToStartComparison - проверяем equalReportsCount >= - equalReportsCountToStartRefAssigning - удаляем все без версий, но несовпадающие с двумя (или больше?) последними
                // тут ещё надо бы искать совпадающие подряд, а если раз не совпало, дальше уже не искать
                // 3 оба больше (равны на самом деле) - можно создавать эталон


                // отчётов без версий достаточное количество для обработки (сравнение хешей)
                if (reportsWOversionsCount >= reportsCountToStartComparison)
                {
                    // одинаковых отчётов достаточно для создания эталона
                    if (equalReportsCount >= reportsCountToStartComparison)
                    {
                        // здесь надо создать эталонный отчёт и увеличить текущую версию на 1 для дальнейшего применения
                        maxVersion++;
                        TestReport theScenarioReportRef = CreateNewTestReport(testScenario, 0, true, maxVersion, testTimingReportStagesList, testReportHash);
                        //Logs.Here().Information("Source {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);
                        reportsListOfTheScenario.RemoveAt(0);
                        //Logs.Here().Information("Removed 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);
                        reportsListOfTheScenario.Insert(0, theScenarioReportRef);
                        //Logs.Here().Information("Inserted Ref at 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);
                        
                        // текущий отчёт записать в конец с версией нового эталона
                        TestReport theReportOfTheScenario = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, maxVersion, testTimingReportStagesList, testReportHash);
                        reportsListOfTheScenario.Add(theReportOfTheScenario);

                        return reportsListOfTheScenario;
                    }
                    // 
                    if (equalReportsCount >= equalReportsCountToRemoveDifferents)
                    {
                        // удаляем все без версий, но несовпадающие с equalReportsCountToStartRefAssigning последними
                    }
                }
            }

            // 
            //int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;
            

            // эталонного отчёта нет (иначе сюда бы не попали), сравнивать рано, версий ещё нет - только записать текущий в конец
            thisReportMustBecomeReference = false;
            return reportsListOfTheScenario;
        }

        private async Task<List<TestReport.TestReportStage>> TheReportsConfluenceForView(ConstantsSet constantsSet, int testScenario)
        {
            string eternalTestTimingStagesReportsLogKey = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog.Value; // key-test-reports-timing-imprints-list

            List<TestReport> theScenarioReportsLast = await _cache.FetchHashedAsync<int, List<TestReport>>(eternalTestTimingStagesReportsLogKey, testScenario);

            List<TestReport.TestReportStage> testTimingReportStagesListCurrent = new();
            //Logs.Here().Information("United List - {@R}, Length = {0}.", new { TestTimingReportStages = testTimingReportStagesListCurrent }, testTimingReportStagesListCurrent.Count);

            for (int i = 0; i < theScenarioReportsLast.Count; i++)
            {
                testTimingReportStagesListCurrent.AddRange(theScenarioReportsLast[i].TestReportStages); // theScenarioReports.Count - 1
                Logs.Here().Information("Hash of hashes - {0} in theScenarioReports[{1}] .", theScenarioReportsLast[i].ThisReportHash, i);
            }

            //Logs.Here().Information("United List - {@R}, Length = {0}.", new { TestTimingReportStages = testTimingReportStagesListCurrent }, testTimingReportStagesListCurrent.Count);

            return testTimingReportStagesListCurrent;
        }

        // метод получает только константы, самостоятельно достаёт из них нужный ключ,
        // достаёт сохранённые шаги с засечками времени в словарь,
        // преобразовывает в список (через массив) и собирает хеши всех шагов для вычисления общего хеша отчёта,
        // возвращает (отсортированный список) и хеш всего отчёта
        // дополнительно тут подходящее место отсортировать по номеру цепочки, а зачем по номеру шага
        // 
        private async Task<(List<TestReport.TestReportStage>, string)> ConvertDictionaryWithReportToList(ConstantsSet constantsSet)
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

        // 
        private List<TestReport> FindIdenticalReportsCount(int reportsCountToStartComparison, List<TestReport> reportsListOfTheScenario, TestReport theScenarioReportRef, int equalReportsCount)
        {
            // сравниваем количество одинаковых отчётов с константой и если равно (больше вроде бы не может быть),
            // то сохраняем эталонный отчёт в нулевой индекс
            // предварительно надо проверить, что там сейчас - и, если эталон с другой (меньшей) версией,
            // то вытолкнуть (вставить) его в первый индекс (не затереть первый)

            Logs.Here().Information("Ref to insert {@F}.", new { ReportRef = theScenarioReportRef });

            if (equalReportsCount >= reportsCountToStartComparison)
            {
                Logs.Here().Information("Source {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);

                reportsListOfTheScenario.RemoveAt(0);

                Logs.Here().Information("Removed 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);

                reportsListOfTheScenario.Insert(0, theScenarioReportRef);

                Logs.Here().Information("Inserted Ref at 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);

            }

            return reportsListOfTheScenario;
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

        private async Task<string> WriteTestScenarioReportsList(ConstantsSet constantsSet, int testScenario, List<TestReport> theScenarioReports)
        {
            KeyType eternalTestTimingStagesReportsLog = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog; // key-test-reports-timing-imprints-list

            await _cache.WriteHashedAsync<int, List<TestReport>>(eternalTestTimingStagesReportsLog.Value, testScenario, theScenarioReports, eternalTestTimingStagesReportsLog.LifeTime);

            return eternalTestTimingStagesReportsLog.Value;
        }

        // метод выводит таблицу с результатами текущего отчёта о времени прохождения теста по контрольным точкам
        private async Task<bool> ViewComparedReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario, List<TestReport.TestReportStage> testTimingReportStagesSource)
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
        private bool ViewListOfReportsInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario, List<TestReport> reportsListOfTheScenario)
        {
            //char ttt = '\u2588'; // █ \u2588 ▮ U+25AE ▯ U+25AF 

            int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;

            int screenFullWidthLinesCount = 228;
            char screenFullWidthTopLineChar = '-';
            char screenFullWidthBetweenLineChar = '\u1C79'; // Ol Chiki Gaahlaa Ttuddaag --> ᱹ

            Console.WriteLine($"\n  Timing imprint report List on testScenario No: {testScenario,-3:d} | List report count = {reportsListOfTheScenario.Count,-4:d} | total test time = {(int)tsTest99,5:d} msec."); // \t

            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));
            Console.WriteLine("| {0,5} | {1,5} | {2,5} | {3,5} | {4,30} | {5,5} | {6,5} |", "scnrm", "index", "versn", "isRef", "hash", "stags", "i");
            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));

            for (int i = 0; i < reportsListOfTheScenarioCount; i++) //
            {
                TestReport stage = reportsListOfTheScenario[i];
                int r01 = stage.TestScenarioNum;
                int r02 = stage.TheScenarioReportsCount;
                int r03 = stage.ThisReporVersion;
                bool r04 = stage.IsThisReportTheReference;
                string r05 = stage.ThisReportHash;
                int r06 = stage.TestReportStages.Count;

                Console.WriteLine("| {0,5:d} | {1,5:d} | {2,5:d} | {3,5} | {4,30} | {5,5:d} | {6,5:d} |", r01, r02, r03, r04, r05, r06, i);
                Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthBetweenLineChar));
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
