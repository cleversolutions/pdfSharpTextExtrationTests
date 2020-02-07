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
        private Dictionary<string, Dictionary<int,int>> FontLookup = new Dictionary<string, Dictionary<int, int>>();
        private string CurrentFont = null;

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            // var filePath = "../../../test-files/test.pdf";
            // var filePath = "../../../test-files/2017-BMA.pdf";
            var filePath = "../../../test-files/windows-vista.pdf";
            //var filePath = "../../../test-files/managecookies.pdf";
            string result;
            using (var fs = File.Open(filePath, FileMode.Open))
            {
                result = GetTextFromPdf(fs);
            }
            Debug.WriteLine($"Final Result: {result.Substring(0, 500)}");
            // Debug.WriteLine("Result should be: en dash between quotes \"–\". – A");
            Assert.AreEqual("en dash between quotes \"–\". – A\n", result);
        }

        public string GetTextFromPdf(Stream pdfFileStream)
        {
            using (var document = PdfReader.Open(pdfFileStream, PdfDocumentOpenMode.ReadOnly))
            {
                var result = new StringBuilder();
                foreach (var page in document.Pages)
                {
                    var fontResource = page.Resources.Elements.GetDictionary("/Font")?.Elements;
                    if(fontResource == null) continue;
                    //All that above isn't going to do, but it's close...
                    foreach(var fontName in fontResource.Keys){
                        var resource = fontResource[fontName];
                        var unicodeDictionary = ((resource as PdfReference)?.Value as PdfDictionary)?.Elements?.GetDictionary("/ToUnicode");
                        var stream = unicodeDictionary?.Stream;
                        if(stream == null ){
                            continue;
                        }
                        var cmap = ParseCMap(stream.ToString());
                        if(cmap != null && ! FontLookup.ContainsKey(fontName)){
                            FontLookup.Add(fontName, cmap);
                        }
                    }

                    ExtractText(ContentReader.ReadContent(page), result);
                    result.AppendLine();
                }
                return result.ToString();
            }

        }

        private Dictionary<int,int> ParseCMap(string cMap){
            Debug.WriteLine(cMap);
            var map = new Dictionary<int,int>();
            ParseCMap(cMap, map);
            return map;
        }

        // A CMap is a character map. 
        // https://blog.idrsolutions.com/2012/05/understanding-the-pdf-file-format-embedded-cmap-tables/
        private void ParseCMap(string cMap, Dictionary<int, int> mapping)
        {
            //cMap can have either bfChar, or bfRange, take whichever is first
            int beginbfcharIdx = cMap.IndexOf("beginbfchar");
            int beginbfrangeIdx = cMap.IndexOf("beginbfrange");
            int bfCharLen = cMap.IndexOf("endbfchar") - beginbfcharIdx;
            int bfRangeLen = cMap.IndexOf("endbfrange") - beginbfrangeIdx;

            //If we have both, take the first one
            if (beginbfcharIdx >= 0 && beginbfrangeIdx >= 0)
            {
                if (beginbfcharIdx < beginbfrangeIdx)
                {
                    ParseBFChar(cMap.Substring(beginbfcharIdx + 11, bfCharLen), mapping);
                    cMap = cMap.Substring(beginbfcharIdx + 11 + bfCharLen + 9);
                }
                else
                {
                    ParseBFRange(cMap.Substring(beginbfrangeIdx + 12, bfRangeLen), mapping);
                    cMap = cMap.Substring(beginbfrangeIdx + 12 + bfRangeLen + 10);
                }
            }
            else if (beginbfcharIdx >= 0)
            {
                ParseBFChar(cMap.Substring(beginbfcharIdx + 11, bfCharLen), mapping);
                cMap = cMap.Substring(beginbfcharIdx = 11 + bfCharLen + 9);
            }
            else if (beginbfrangeIdx >= 0)
            {
                ParseBFRange(cMap.Substring(beginbfrangeIdx + 12, bfRangeLen), mapping);
                cMap = cMap.Substring(beginbfrangeIdx + 12 + bfRangeLen + 10);
            }
            else
            {
                //There is nothing left to parse
                return;
            }
            //Recurse until there is nothing left to parse
            ParseCMap(cMap, mapping);
        }

        ///
        /// Pase the contents of a CMAP table from beginbfchar to endbfchar
        ///
        private void ParseBFChar(string bfChar, Dictionary<int,int> mapping)
        {
            string[] lines = bfChar.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            foreach(var map in lines){
                var match = Regex.Match(map, @"<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]{4})>");
                if(match.Groups.Count == 3){
                    int glyf = Convert.ToInt32(match.Groups[1].Value, 16);
                    int ucode = Convert.ToInt32(match.Groups[2].Value, 16);
                    if(!mapping.ContainsKey(glyf)){
                        mapping.Add(glyf, ucode);
                    }
                }
            }
        }

        ///
        /// Parse the contents of a CMAP file from beginbfrange to endbfrange
        /// This will generate a mapping for each character in each range
        ///
        private void ParseBFRange(string fbRange, Dictionary<int,int> mapping)
        {
            string[] CMapArray = fbRange.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            //TODO... this is just a bit more complicated
            throw new NotImplementedException("To Be done");
        }


        private void ExtractText(CObject obj, StringBuilder target)
        {
            switch (obj)
            {
                case COperator cOperator:
                    ExtractTextFromOperator(cOperator, target);
                    return;
                case CSequence sequence: //CArray, CSequence
                    ExtractTextFromEnumable(sequence, target);
                    return;
                case CString cString:
                    ExtractTextFromString(cString, target);
                    return;
                case CInteger _:
                case CComment _:
                case CName _:
                case CNumber _:
                    //Do nothing
                    return;
                default:
                    throw new NotImplementedException(obj.GetType().AssemblyQualifiedName);
            }
        }

        private void ExtractTextFromEnumable(CSequence sequence, StringBuilder target)
        {
            foreach (var obj in sequence)
            {
                ExtractText(obj, target);
            }
        }

        private void ExtractTextFromOperator(COperator obj, StringBuilder target)
        {
            Debug.WriteLine($"Op -> {obj.ToString()}");
            foreach (var op in obj.Operands)
            {
                if (op is CInteger cint)
                {
                    Debug.WriteLine($"  > CInteger - {op.ToString()} ({cint.Value:X})");
                }
                else if (op is CName cname)
                {
                    Debug.WriteLine($"  > CName - {op.ToString()} ({cname.Name})");
                }
                else
                {
                    Debug.WriteLine($"  > {op.GetType().Name} - {op.ToString()}");
                }
            }

            if(obj.OpCode.OpCodeName == OpCodeName.Tf){
                var fontName = obj.Operands.OfType<CName>().FirstOrDefault();
                if(fontName != null){
                    //This is likely the wrong way to do this
                    CurrentFont = fontName.Name;
                    Debug.WriteLine($"Selecting Font {fontName.Name}");
                }else{
                    Debug.WriteLine("Can't parse font name");
                }
                
            }
            if (obj.OpCode.OpCodeName == OpCodeName.Tj || obj.OpCode.OpCodeName == OpCodeName.TJ)
            {
                foreach (var element in obj.Operands)
                {
                    ExtractText(element, target);
                }

                // target.Append(" ");
            }
        }


        private void ExtractTextFromString(CString obj, StringBuilder target)
        {
            string text = obj.Value;

            if(! string.IsNullOrEmpty(CurrentFont) && FontLookup.ContainsKey(CurrentFont)){
                //Do character sub with the current fontMap
                var fontMap = FontLookup[CurrentFont];
                
                //Convert to bytes
                var chars = text.ToCharArray();
                Debug.WriteLine($"Replacing Chars in ({string.Join(",", chars.Select(b => $"{b:X}"))})");
                var newChars = chars
                    .Where(c => c != '\0')
                    .Select(b => fontMap.ContainsKey(b) ? (char)fontMap[b] : b )
                    .ToArray();

                Debug.WriteLine($"Replaced Chars in  ({string.Join(",", newChars.Select(b => $"{b:X}"))})");
                text = new string(newChars);
                Debug.WriteLine($"Apending {text}");
            
            }
            target.Append(text);
        }

    }
}