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
        private Dictionary<string, Dictionary<int, int>> FontLookup = new Dictionary<string, Dictionary<int, int>>();
        private string CurrentFont = null;

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            // var filePath = "../../../test-files/test.pdf";
            var filePath = "../../../test-files/2017-BMA.pdf";
            // var filePath = "../../../test-files/windows-vista.pdf";
            //var filePath = "../../../test-files/managecookies.pdf";
            string result;
            PdfSharpTextExtractor extractor = new PdfSharpTextExtractor();
            using (var fs = File.Open(filePath, FileMode.Open))
            {
                result = extractor.GetTextFromPdf(fs);
                // result = GetTextFromPdf(fs);
            }
            Debug.WriteLine($"Final Result: {result.Substring(0, Math.Min(result.Length, 1000))}");
            // Debug.WriteLine("Result should be: en dash between quotes \"–\". – A");
            Assert.AreEqual("en dash between quotes \"–\". – A\n", result);
        }
        public string GetTextFromPdf(Stream pdfFileStream)
        {
            using (var document = PdfReader.Open(pdfFileStream, PdfDocumentOpenMode.ReadOnly))
            {
                var result = new StringBuilder();
                int pageIdx = 0;
                foreach (var page in document.Pages)
                {
                    pageIdx++;
                    if(pageIdx <= 2) continue;
                    if(pageIdx == 4) break;
                    Debug.WriteLine($"Processing Page {pageIdx}");

                    ParseCMAPs(page);
                    // ExtractText(ContentReader.ReadContent(page), result);
                    result.AppendLine();

                }
                return result.ToString();
            }
        }

        private void ParseCMAPs(PdfPage page)
        {
            var fontResource = page.Resources.Elements.GetDictionary("/Font")?.Elements;
            if (fontResource == null) return;
            //All that above isn't going to do, but it's close...
            foreach (var fontName in fontResource.Keys)
            {
                var resource = fontResource[fontName];
                var unicodeDictionary = ((resource as PdfReference)?.Value as PdfDictionary)?.Elements?.GetDictionary("/ToUnicode");
                var stream = unicodeDictionary?.Stream;
                if (stream == null)
                {
                    continue;
                }
                var cm = new CMap(stream);
                // var cmap = ParseCMap(stream.ToString());
                // FontLookup[fontName] = cmap;
            }
        }

        private Dictionary<int, int> ParseCMap(string cMap)
        {
            // Debug.WriteLine(cMap);
            var map = new Dictionary<int, int>();

            //So here is the awesome part of this... there can be multiple code spaces
            //each having different number of bytes...
            //but there may be no overlap
            //https://www.adobe.com/content/dam/acom/en/devnet/font/pdfs/5014.CIDFont_Spec.pdf
            //so check if a char is in the 1 byte region, nope, then check in the 2 byte region etc

            int codespaceStartIdx = cMap.IndexOf("begincodespacerange");
            int codespaceLength = cMap.IndexOf("endcodespacerange") - codespaceStartIdx;
            
            var codeSpace = new List<Tuple<int,int>>();
            ParseCodeSpaceRange(cMap.Substring(codespaceStartIdx + 19, codespaceLength - 19), codeSpace);

            ParseCMap(cMap, map);
            return map;
        }

        // A CMap is a character map. 
        // https://blog.idrsolutions.com/2012/05/understanding-the-pdf-file-format-embedded-cmap-tables/
        private void ParseCMap(string cMap, Dictionary<int, int> mapping)
        {
            //TODO I'll likely refactor this into a class with attributes for the cmap, code space and other attributes

            //TODO check for usecmap -- we can refer to other CMAPs including built-in ones...

            //TODO -- confirm if maps may use cfchar instead of bfchar
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
                    ParseBFChar(cMap.Substring(beginbfcharIdx + 11, bfCharLen - 11), mapping);
                    cMap = cMap.Substring(beginbfcharIdx + 11 + bfCharLen + 9 - 11);
                }
                else
                {
                    ParseBFRange(cMap.Substring(beginbfrangeIdx + 12, bfRangeLen - 12), mapping);
                    cMap = cMap.Substring(beginbfrangeIdx + 12 + bfRangeLen + 10 - 12);
                }
            }
            else if (beginbfcharIdx >= 0)
            {
                ParseBFChar(cMap.Substring(beginbfcharIdx + 11, bfCharLen - 11), mapping);
                cMap = cMap.Substring(beginbfcharIdx = 11 + bfCharLen + 9 - 11);
            }
            else if (beginbfrangeIdx >= 0)
            {
                ParseBFRange(cMap.Substring(beginbfrangeIdx + 12, bfRangeLen - 12), mapping);
                cMap = cMap.Substring(beginbfrangeIdx + 12 + bfRangeLen + 10 - 12);
            }
            else
            {
                //There is nothing left to parse
                return;
            }
            //Recurse until there is nothing left to parse
            ParseCMap(cMap, mapping);
        }

        private void ParseCodeSpaceRange(string codeSpaceStr, IList<Tuple<int,int>> codeSpace ){
            string[] lines = codeSpaceStr.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            foreach (var map in lines)
            {
                var match = Regex.Match(map, @"<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]{4})>");
                if (match.Groups.Count == 3)
                {
                    int low = Convert.ToInt32(match.Groups[1].Value, 16);
                    int high = Convert.ToInt32(match.Groups[2].Value, 16);
                    codeSpace.Add(new Tuple<int,int>(low,high));
                }
            }
        }

        ///
        /// Pase the contents of a CMAP table from beginbfchar to endbfchar
        ///
        private void ParseBFChar(string bfChar, Dictionary<int, int> mapping)
        {
            string pattern = @"\s?<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]{4})>";
            var match = Regex.Match(bfChar, pattern);
            while(match.Success){
                //pop the match from bfChar
                bfChar = bfChar.Substring(match.Length);

                //extract the cid and unicode and add it to our mapping
                if (match.Groups.Count == 3)
                {
                    try{
                        int cid = Convert.ToInt32(match.Groups[1].Value, 16);
                        int ucode = Convert.ToInt32(match.Groups[2].Value, 16);
                        if (!mapping.ContainsKey(cid))
                        {
                            mapping.Add(cid, ucode);
                        }
                    }catch(Exception){
                        //TODO -- I think this happens when multiple cids match one ucode
                        //They are all crammed into 1 big number. We need to know how many bytes
                        //the map uses, and loop over the cids. 
                        Debug.WriteLine($"Wow, didn't see that coming. <{match.Groups[1]}> <{match.Groups[2]}>");
                    }
                }

                //grab the next pair of char maps
                match = Regex.Match(bfChar, pattern);
            }
        }

        ///
        /// Parse the contents of a CMAP file from beginbfrange to endbfrange
        /// This will generate a mapping for each character in each range
        ///
        private void ParseBFRange(string fbRange, Dictionary<int, int> mapping)
        {
            Debug.WriteLine("ParseRange");
            string[] CMapArray = fbRange.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            string[] lines = fbRange.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            foreach (var map in lines)
            {
                //Example <4b><4b><005f>
                var match = Regex.Match(map, @"<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]{4})>");
                if (match.Groups.Count == 4)
                {
                    //Convert our matches to ints
                    int fromGlyf = Convert.ToInt32(match.Groups[1].Value, 16);
                    int toGlyf = Convert.ToInt32(match.Groups[2].Value, 16);
                    int ucode = Convert.ToInt32(match.Groups[3].Value, 16);

                    if (fromGlyf > toGlyf) continue; //That would be unusual
                    //Map all chars from fromGlyf to toGlyf and add
                    for (int i = 0; fromGlyf + i <= toGlyf; i++)
                    {
                        int glyf = fromGlyf + i;
                        if (!mapping.ContainsKey(glyf))
                        {
                            mapping.Add(glyf, ucode + i);
                        }
                    }
                }
                else
                {
                    //maybe the format was <02> <02> [<0066006C>]
                    throw new NotImplementedException("Lower hanging fruit first");
                }
            }

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
            if (obj.OpCode.OpCodeName == OpCodeName.Tf)
            {
                var fontName = obj.Operands.OfType<CName>().FirstOrDefault();
                if (fontName != null)
                {
                    //This is likely the wrong way to do this
                    CurrentFont = fontName.Name;
                    // Debug.WriteLine($"Selecting Font {fontName.Name}");
                }
                else
                {
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

            if (!string.IsNullOrEmpty(CurrentFont) && FontLookup.ContainsKey(CurrentFont))
            {
                //Do character sub with the current fontMap
                var fontMap = FontLookup[CurrentFont];

                //This is not working....
                //1B5 is 437 which is u
                //but I'm getting 2 chars 1 (0x1), 181 (0xB5)
                //So when i do ths substitutino as I do below, I'm missing the correct sub

                //Convert to bytes
                var chars = text.ToCharArray();

                //So, I believe I need to detamine how many bytes are used in the 
                //begincodespacerange, and if it is > 1 shift chars together like below
                if(chars.Length > 1){
                    int ch = (chars[0] << 8) | chars[1];
                }

                //TODO - IT is incorrect to fallback to the glyf for Type0 fonts.
                //See https://www.adobe.com/content/dam/acom/en/devnet/pdf/pdfs/pdf_reference_archives/PDFReference.pdf
                //page 354  for instructions

                // Debug.WriteLine($"Replacing Chars in ({string.Join(",", chars.Select(b => $"{b:X}"))})");
                var newChars = chars
                    .Where(c => c != '\0')
                    .Select(b => fontMap.ContainsKey(b) ? (char)fontMap[b] : b)
                    .ToArray();

                // Debug.WriteLine($"Replaced Chars in  ({string.Join(",", newChars.Select(b => $"{b:X}"))})");
                text = new string(newChars);
                // Debug.WriteLine($"Apending {text}");
                // target.Append(text);
            }
            else
            {
                Debug.WriteLine($"Font {CurrentFont ?? "(null)"} not found");
            }
            target.Append(text);
        }

    }
}