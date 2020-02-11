using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using NUnit.Framework;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Pdf.IO;
using System.Text.RegularExpressions;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Fonts.OpenType;
using PdfSharpCore.Pdf;
using static PdfSharpCore.Pdf.PdfDictionary;
using System.Collections.Generic;

namespace pdf_text_extractor_test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            // var filePath = "../../../test-files/test.pdf";
            //var filePath = "../../../test-files/2017-BMA.pdf";
            var filePath = "../../../test-files/windows-vista.pdf";
            //var filePath = "../../../test-files/managecookies.pdf";
            string result;
            PdfSharpTextExtractor extractor = new PdfSharpTextExtractor();
            using (var fs = File.Open(filePath, FileMode.Open))
            {
                result = extractor.GetTextFromPdf(fs);
            }
            Debug.WriteLine($"Final Result: {result.Substring(0, Math.Min(result.Length, 1000))}");

            // Debug.WriteLine("Result should be: en dash between quotes \"–\". – A");
            //Assert.AreEqual("en dash between quotes \"–\". – A\n", result);
        }
    }
}