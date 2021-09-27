using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConstantData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace ConstantData.Tests
{
    [TestClass()]
    public class MonitorLoopTests
    {
        [TestMethod()]
        [DataRow("QLGN AEKI RLRN GEAE", "KING")]

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
                Console.WriteLine($"Matrix from sourceMatriString = {sourceMatriString} has the following view, matrixSideSize = {matrixSideSize}");

                for (int i = 0; i < matrixSideSize; i++)
                {
                    //currentMatrixRow = "";
                    //for (int j = 0; j < matrixSideSize; j++)
                    string currentMatrixRow = sourceMatriString.Substring(i * matrixSideSize, matrixSideSize);
                    Console.WriteLine($"i {i}, matrixSideSize = {matrixSideSize}, matrixSideSize^2 = {matrixSideSize * matrixSideSize}");
                }
            }


            



            Assert.AreEqual(1, 1);
        }
    }
}