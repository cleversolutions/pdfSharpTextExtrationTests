using System.Collections.Generic;
using static PdfSharpCore.Pdf.PdfDictionary;
using System.Diagnostics;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

///
/// Parse a CMap file.
/// This is not a complete CMAP parser. There are many parts of the CMAP spec that this doesn't
/// yet implement. This parser aims to catch the most common features necessary for text extraction
/// of pdf documents. This is also probably completely wrong. I've read bits and pieces of the 
/// Adobe PDF spec to piece this together, but I am certainly not the domain expert one should be
/// to do a proper job of this.
/// TODO - Include referenced CMAPs
/// TODO - Support standard CMAPs
///
public class CMap
{
    private readonly string CMapStr;
    private readonly string FontName;

    public List<CodeSpaceRange> CodeSpaceRanges { get; set; }

    public CMap(PdfStream stream, string fontName)
    {
        CodeSpaceRanges = new List<CodeSpaceRange>();
        CMapStr = stream.ToString();
        FontName = fontName;

        //First parse the code space ranges
        ParseCodeSpaceRanges();

        //Parse the bfchar and bfrange mappings
        ParseMappings(CMapStr);
    }

    public string Encode(string text)
    {
        //Convert to bytes
        var chars = text.ToCharArray();
        //Should we use Encoding.BigEndianUnicode.GetBytes(text) instead?
        var be16 = Encoding.BigEndianUnicode.GetBytes(text);
        string result = "";

        //find a substitution for every char in the text
        for (int chrIdx = 0; chrIdx < chars.Length; chrIdx++)
        {
            //Ranges should not overlap, but the spec and the real world...
            //Start with 1 byte, see if we find a 1 byte match. If not try 2 bytes etc.
            //CodeSpaceRange.NumberOfBytes should indicate how many bytes we map, but it doesn't in real life
            CodeSpaceRange.Map map;
            int cid = chars[chrIdx];
            var range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid);
            if (range != null)
            {
                if (range.Mapping.TryGetValue(cid, out map) && map.SourceByteLength == 1)
                {
                    result += map.UnicodeValue;
                    continue;
                }
            }

            // 2-byte cid
            cid = (chars[chrIdx] << 8) | chars[chrIdx + 1];
            range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid);
            if (range != null)
            {
                if (range.Mapping.TryGetValue(cid, out map) && map.SourceByteLength == 2)
                {
                    result += map.UnicodeValue;
                    chrIdx++;
                    continue;
                }

            }

            // 3-byte cid
            cid = (chars[chrIdx] << 16) | (chars[chrIdx + 1] << 8) | chars[chrIdx + 2];
            range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid);
            if (range != null)
            {
                if (range.Mapping.TryGetValue(cid, out map) && map.SourceByteLength == 2)
                {
                    result += map.UnicodeValue;
                    chrIdx += 2;
                    continue;
                }
            }


            // 4-byte cid
            cid = (chars[chrIdx] << 32) | (chars[chrIdx + 1] << 16) | (chars[chrIdx + 2] << 8) | chars[chrIdx + 3];
            range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid);
            if (range != null)
            {
                if (range.Mapping.TryGetValue(cid, out map) && map.SourceByteLength == 2)
                {
                    result += map.UnicodeValue;
                    chrIdx += 3;
                    continue;
                }
            }

            //Fallback on using the cid... I don't think this is supposed to be done.
            cid = chars[chrIdx];
            Debug.WriteLine($"Failed to encode {cid:X}");
            result.Append((char)cid);

        }

        return result;
    }

    ///
    /// Parse the code space ranges begincodespacerange to endcodespacerange
    /// There can be several ranges, and they may be 1, 2, 3 or 4 bytes that don't overlap
    /// Parse each range, then build a cid to unicode map in each range.
    ///
    private void ParseCodeSpaceRanges()
    {
        //find the text between begincodespacerange and endcodespacerange
        int codespaceStartIdx = CMapStr.IndexOf("begincodespacerange");
        int codespaceLength = CMapStr.IndexOf("endcodespacerange") - codespaceStartIdx;
        var ranges = CMapStr.Substring(codespaceStartIdx + 19, codespaceLength - 19);

        //Extract each low/high pair
        Match match;
        string pattern = @"\s?<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]+)>";
        while ((match = Regex.Match(ranges, pattern)).Success)
        {
            //pop the match from the ranges
            ranges = ranges.Substring(match.Length);

            //We should match 2 numbers
            if (match.Groups.Count == 3)
            {
                //each range must be representable by an int
                if (match.Groups[1].Value.Length > 8)
                {
                    Debug.WriteLine("codespacerange contains low value that is too large");
                    continue;
                }
                int strLength = match.Groups[1].Length;
                int low = Convert.ToInt32(match.Groups[1].Value, 16);
                int high = Convert.ToInt32(match.Groups[2].Value, 16);
                CodeSpaceRanges.Add(new CodeSpaceRange
                {
                    Low = low,
                    High = high,
                    NumberOfBytes = match.Groups[1].Length / 2
                });
            }
            else
            {
                Debug.WriteLine("codespacerange contains unexpected number of matches");
            }
        }

        //Order our code space ranges for the lookups
        CodeSpaceRanges.OrderBy(r => r.NumberOfBytes).ThenBy(r => r.Low);

    }

    public void ParseMappings(string cMap)
    {
        //TODO check for usecmap -- we can refer to other CMAPs including built-in ones...
        int beginbfcharIdx = cMap.IndexOf("beginbfchar");
        int beginbfrangeIdx = cMap.IndexOf("beginbfrange");
        int bfCharLen = cMap.IndexOf("endbfchar") - beginbfcharIdx;
        int bfRangeLen = cMap.IndexOf("endbfrange") - beginbfrangeIdx;

        //If we have both, take the first one
        if (beginbfcharIdx >= 0 && beginbfrangeIdx >= 0)
        {
            if (beginbfcharIdx < beginbfrangeIdx)
            {
                ParseBFChar(cMap.Substring(beginbfcharIdx + 11, bfCharLen - 11));
                cMap = cMap.Substring(beginbfcharIdx + 11 + bfCharLen + 9 - 11);
            }
            else
            {
                ParseBFRange(cMap.Substring(beginbfrangeIdx + 12, bfRangeLen - 12));
                cMap = cMap.Substring(beginbfrangeIdx + 12 + bfRangeLen + 10 - 12);
            }
        }
        else if (beginbfcharIdx >= 0)
        {
            ParseBFChar(cMap.Substring(beginbfcharIdx + 11, bfCharLen - 11));
            cMap = cMap.Substring(beginbfcharIdx = 11 + bfCharLen + 9 - 11);
        }
        else if (beginbfrangeIdx >= 0)
        {
            ParseBFRange(cMap.Substring(beginbfrangeIdx + 12, bfRangeLen - 12));
            cMap = cMap.Substring(beginbfrangeIdx + 12 + bfRangeLen + 10 - 12);
        }
        else
        {
            //There is nothing left to parse
            return;
        }
        //Recurse until there is nothing left to parse
        ParseMappings(cMap);
    }

    private void AddMapping(int cid, string ucode, int lengthInBytes)
    {
        //Find the proper codespace range and add the mapping
        var range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid);
        if (range != null)
        {
            range.AddMap(cid, ucode, lengthInBytes);
        }
        else
        {
            Debug.WriteLine($"Can't find char range for {cid:X}, {ucode:X} in font {FontName}");
        }
    }

    private string ConvertDstCode(string dstCode)
    {
        string ucode = null;
        int length = dstCode.Length;

        if (length <= 4)
        {
            // If the dstCode is 4 digit hex, convert it to a 1 char string
            char ch = (char)Convert.ToInt16(dstCode, 16);
            ucode = ch.ToString();
        }
        else if (length % 4 == 0)
        {
            // if dstCode is a multiple of 4, convert it into several char string
            var chars = Enumerable.Range(0, length / 4)
                .Select(i => dstCode.Substring(i * 4, 4))
                .Select(str => (char)Convert.ToInt16(str, 16));
            ucode = string.Concat(chars);
        }
        return ucode;
    }

    ///
    /// Pase the contents of a CMAP table from beginbfchar to endbfchar
    ///
    private void ParseBFChar(string bfChar)
    {
        //TODO - this assumes src and dst are both in hex format; however dst can be dstCharname 
        string pattern = @"\s?<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]+)>";
        Match match;
        while ((match = Regex.Match(bfChar, pattern)).Success)
        {
            //pop the match from bfChar
            bfChar = bfChar.Substring(match.Length);

            //extract the cid and unicode and add it to our mapping
            if (match.Groups.Count == 3)
            {
                try
                {
                    //The srcCode must be representable by an int
                    int srcCode = Convert.ToInt32(match.Groups[1].Value, 16);
                    int srcCodeByteLength = match.Groups[1].Value.Length / 2;

                    //convert the dstCode to a string
                    string ucode = ConvertDstCode(match.Groups[2].Value);
                    if (ucode == null)
                    {
                        Debug.WriteLine("dstCode length wasn't a multiple of 4");
                        continue;
                    }
                    AddMapping(srcCode, ucode, srcCodeByteLength);
                }
                catch (Exception)
                {
                    //TODO -- I think this happens when multiple cids match one ucode
                    //They are all crammed into 1 big number. We need to know how many bytes
                    //the map uses, and loop over the cids.
                    Debug.WriteLine($"Oops.. we still need to handle this. <{match.Groups[1]}> <{match.Groups[2]}>");
                }
            }
            else
            {
                Debug.WriteLine("Ummm... well that's awkward, we didn't match a pair of numbers.");
                break;
            }
        }
    }

    ///
    /// Parse the contents of a CMAP file from beginbfrange to endbfrange
    /// This will generate a mapping for each character in each range
    ///
    private void ParseBFRange(string fbRange)
    {
        Debug.WriteLine("ParseRange");
        string pattern = @"\s?<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]+)>";
        Match match;
        while ((match = Regex.Match(fbRange, pattern)).Success)
        {
            //pop the match from bfChar
            fbRange = fbRange.Substring(match.Length);

            if (match.Groups.Count == 4)
            {
                //Convert our matches to ints
                int srcCodeLow = Convert.ToInt32(match.Groups[1].Value, 16);
                int srcCodeHigh = Convert.ToInt32(match.Groups[2].Value, 16);
                char dstCodeLow = (char)Convert.ToInt16(match.Groups[3].Value, 16);

                //Ensure to is > then from
                if (srcCodeLow > srcCodeHigh) continue;

                //Map all chars from fromGlyf to toGlyf and add
                for (int i = 0; srcCodeLow + i <= srcCodeHigh; i++)
                {
                    int glyf = srcCodeLow + i;
                    char dstCode = (char)(((int)dstCodeLow) + i);
                    AddMapping(glyf, dstCode.ToString(), match.Groups[1].Value.Length / 2);
                }
            }
            else
            {
                //The format may have been srcCodeLo srcCodeHi [/dstCharName1../dstCharNamen]
                //where dstCharName is a postscript language name object ie. /quotesingle
                Debug.WriteLine("Oops, this isn't handled yet");
            }
        }
    }
}