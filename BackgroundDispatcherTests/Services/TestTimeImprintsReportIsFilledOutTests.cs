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
        [DataRow(new int[] { 10, 20, 30, 40, 50 }, new string[] { "ffff", "ffff", "ffff" }, new int[] { 1, 10, 100, 1000, 10000 }, new int[] { 2, 20, 200, 2000, 20000 }, new int[] { 3, 30, 300, 3000, 30000 })]

        public void CalculateAverageVarianceDeviationsTest(int[] refInt, string[] hashes, int[] rep1, int[] rep2, int[] rep3)
        {
            List<TestReport> referenceList = new();
            List<TestReport> reportsListOfTheScenario = new();
            List<TestReport.TestReportStage> testTimingReportStagesList = new();
            string testReportHash = "ffff";

            List<TestReport> resultList = TestTimeImprintsReportIsFilledOut.CalculateAverageVarianceDeviations(reportsListOfTheScenario, testTimingReportStagesList, testReportHash);

            CollectionAssert.AreEqual(referenceList, resultList);
        }
    }
}