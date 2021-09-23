using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackgroundDispatcher.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Library.Models;

namespace BackgroundDispatcher.Services.Tests
{
    [TestClass()]
    public class TestTimeImprintsReportIsFilledOutTests
    {
        [TestMethod()]
        // 
        [DataRow(new int[] { 71, 71, 71 }, new int[] { 63, 63, 63 }, new int[] { 62, 62, 62 }, new int[] { 58, 58, 58 }, new int[] { 77, 77, 77 }, new int[] { 68, 68, 68 }, new int[] { 67, 67, 67 }, new int[] { 59, 59, 59 }, new int[] { 57, 57, 57 }, new int[] { 51, 51, 51 })]

        public void CalculateAverageVarianceDeviationsTest(int[] r0, int[] r1, int[] r2, int[] r3, int[] r4, int[] r5, int[] r6, int[] r7, int[] r8, int[] r9)
        {
            double[] average = new double[] { 63.3, 62.4444, 62.375, 62.4286, 63.1667, 60.4, 58.5, 55.67, 54, 51 };
            double[] variance = new double[] { 522.1, 456.22, 455.875, 455.7143, 432.833, 203.2, 131, 34.67, 18, 0 };
            string hashesInList = "ff";

            List<TestReport> reportsListOfTheScenario = new();
            List<TestReport> referencesList = new();
            List<TestReport.TestReportStage> testTimingReportStagesList = new();
            List<TestReport.TestReportStage> refernceStagesList = new();

            //int[,] tsData = new int[3, 10];
            int averageLength = average.Length;
            // i - количество отчётов в списке, количество массивов
            for (int i = 0; i < averageLength; i++)
            {

                int[] selectedArray = SwitchArraySelect(i, r0, r1, r2, r3, r4, r5, r6, r7, r8, r9);
                int selectedArrayLength = selectedArray.Length;
                testTimingReportStagesList = new();
                refernceStagesList = new();

                //Console.WriteLine($"i = {i} from {averageLength}, jLength = {selectedArrayLength}");

                // j - количество шагов в отчёте, длина массивов
                for (int j = 0; j < selectedArrayLength; j++)
                {
                    TestReport.TestReportStage testTimingReportStage = new()
                    {
                        TsWork = (long)selectedArray[j],
                        SlidingAverageWork = 0,
                        SlidingVarianceWork = 0
                    };
                    testTimingReportStagesList.Add(testTimingReportStage);

                    // сюда надо записывать пошаговые вычисления (потом)
                    TestReport.TestReportStage refernceStage = new()
                    {
                        TsWork = (long)selectedArray[j],
                        SlidingAverageWork = 0,
                        SlidingVarianceWork = 0
                    };
                    refernceStagesList.Add(refernceStage);
                }

                TestReport reportOfTheScenario = new()
                {
                    ThisReportHash = hashesInList,
                    ThisReporVersion = -1,
                    TestReportStages = testTimingReportStagesList
                };
                reportsListOfTheScenario.Add(reportOfTheScenario);

                TestReport referenceList = new()
                {
                    ThisReportHash = hashesInList,
                    ThisReporVersion = -1,
                    TestReportStages = refernceStagesList
                };
                referencesList.Add(referenceList);
            }

            string testReportHash = hashesInList;            

            List<TestReport> resultsList = TestTimeImprintsReportIsFilledOut.CalculateAverageVarianceDeviations(reportsListOfTheScenario, testTimingReportStagesList, testReportHash);
            double[] averagesResult = new double[averageLength];
            double[] variancesResult = new double[averageLength];
            double diffAverage = 0;
            double diffVariance = 0;
            double diffTotal = 0;
            for (int i = 1; i < averageLength; i++)
            {
                averagesResult[i] = resultsList[i].TestReportStages[0].SlidingAverageWork;
                diffAverage += Math.Abs(Math.Abs(averagesResult[i]) - Math.Abs(average[i]));
                
                variancesResult[i] = resultsList[i].TestReportStages[0].SlidingVarianceWork;
                diffVariance += Math.Abs(Math.Abs(variancesResult[i]) - Math.Abs(variance[i]));

                diffTotal += diffAverage + diffVariance;
                Console.WriteLine($"i = {i} from {averageLength}, average = {average[i]}, averagesResult = {averagesResult[i]}");

            }

            Assert.AreEqual(0, (int)diffTotal);
            //CollectionAssert.AreEqual();
        }

        private static int[] SwitchArraySelect(int arrayNum, int[] a0, int[] a1, int[] a2, int[] a3, int[] a4, int[] a5, int[] a6, int[] a7, int[] a8, int[] a9) => arrayNum switch
        {
            0 => a0,
            1 => a1,
            2 => a2,
            3 => a3,
            4 => a4,
            5 => a5,
            6 => a6,
            7 => a7,
            8 => a8,
            9 => a9,
            _ => throw new ArgumentOutOfRangeException(nameof(arrayNum), $"Not expected direction value: {arrayNum}"),
        };
    }
}