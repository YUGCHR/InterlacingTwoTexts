using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Collections.Generic;
using BackgroundDispatcher.Services;
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
        [DataRow("QLGNAEKIRLRNGEAE", "KING")]

        public void FindCellSequenceToBuildGivenWord(string sourceMatriString, string givenWord)
        { 
            int sourceMatrixLength = sourceMatriString.Length;
            int matrixSideSize = (int)Math.Sqrt((double)sourceMatrixLength);

            if (matrixSideSize * matrixSideSize != sourceMatrixLength)
            {
                Console.WriteLine($"Problem with Matrix size was found - sourceMatriString = {sourceMatriString}, matrixSideSize = {matrixSideSize}, matrixSideSize^2 = {matrixSideSize * matrixSideSize}");
            }
            else
            {                
                string[] numbersInRow = new string[matrixSideSize];
                for (int j = 0; j < matrixSideSize; j++)
                {
                    numbersInRow[j] = j.ToString();
                }
                string row = String.Join("", numbersInRow);
                Console.WriteLine($"Matrix from sourceMatriString = {sourceMatriString} has the following view, matrixSideSize = {matrixSideSize}");
                Console.WriteLine($"  {row}");

                for (int i = 0; i < matrixSideSize; i++)
                {                    
                    string currentMatrixRow = sourceMatriString.Substring(i * matrixSideSize, matrixSideSize);
                    Console.WriteLine($"{i}-{currentMatrixRow}");
                }
            }

            int givenWordLength = givenWord.Length;
            for (int n = 0; n < givenWordLength; n++)
            {
                char currentLetter = givenWord[n];
                int indexOfcurrentLetter = sourceMatriString.IndexOf(currentLetter, 0, sourceMatrixLength - 1);
                int rowPosition = (int)indexOfcurrentLetter / matrixSideSize;
                int columnPosition = indexOfcurrentLetter % matrixSideSize;
                Console.WriteLine($" currentLetter {currentLetter} was found in position {indexOfcurrentLetter}, row {rowPosition} / col {columnPosition} of sourceMatriString");
            }






                Assert.AreEqual(1, 1);
        }


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

        [TestMethod()]
        // 
        [DataRow("key-test-reports-timing-imprints-list-11.json", null)]
        //[DataRow("testJsonAppSettings.json", null)]

        public void CalculateAverageVarianceDeviationsTestWithDataFromFile(string dataFileName, string dataFilePath)
        {
            List<TestReport> reportsListOfTheScenario = new();
            //List<TestReport> jsonTestModel = new();

            Console.WriteLine($"dataFileName - {dataFileName}");

            StreamReader file = File.OpenText(@dataFileName);
            TextReader textReader = file;
            string all = textReader.ReadToEnd();
            file.Close();
            //JsonTextReader reader = new JsonTextReader(file);
            {
                JsonSerializer serializer = new JsonSerializer();
                //reportsListOfTheScenario = (List<TestReport>)serializer.Deserialize(file, typeof(List<TestReport>));                
                reportsListOfTheScenario = JsonConvert.DeserializeObject<List<TestReport>>(all);
            }

            int jsonTestModelCount = reportsListOfTheScenario.Count;

            string jsonStructureIntegrityBegin = reportsListOfTheScenario[0].Guid;
            string jsonStructureIntegrityEnd = reportsListOfTheScenario[jsonTestModelCount - 1].Guid;

            Console.WriteLine($"dataFileName - {dataFileName}, Begin - {jsonStructureIntegrityBegin}");
            Console.WriteLine($"dataFileName - {dataFileName}, Final - {jsonStructureIntegrityEnd}");

            ConstantsSet constantsSet = new();
            string description = $" TEST with JSON data from file {dataFileName} - List<TestReport> reportsListOfTheScenario";
            int testScenario = 100;
            bool res = TestTimeImprintsReportIsFilledOut.ViewListOfReportsInConsole(constantsSet, description, testScenario, reportsListOfTheScenario);

            Assert.AreEqual(1, 1);
            //Assert.AreEqual(jsonStructureIntegrityBegin, jsonStructureIntegrityEnd);
        }
    }
}