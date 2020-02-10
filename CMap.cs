using System.Collections.Generic;
using static PdfSharpCore.Pdf.PdfDictionary;
using System.Diagnostics;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public class CMap
{
    private readonly string CMapStr;

    public List<CodeSpaceRange> CodeSpaceRanges { get; set; }

    public CMap(PdfStream stream)
    {
        CodeSpaceRanges = new List<CodeSpaceRange>();
        CMapStr = stream.ToString();

        //First parse the code space ranges
        ParseCodeSpaceRanges();

        //Parse the bfchar and bfrange mappings
        ParseMappings(CMapStr);
    }

    public string Encode(string text)
    {
        //Convert to bytes
        var chars = text.ToCharArray();
        string result = "";

        //This is wildly wrong, but tests the refactor.. will work on this next
        //need to find the range, then depedning on the number of bytes in the range
        //shift bytes together such as int ch = (chars[0] << 8) | chars[1];
        for (int chrIdx = 0; chrIdx < chars.Length; chrIdx++)
        {
            // 1-byte cid
            int cid = chars[chrIdx];
            var range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid && r.NumberOfBytes == 1);
            if (range != null)
            {
                if (range.Mapping.TryGetValue(cid, out int ucode))
                {
                    // chars[chrIdx] = (char)ucode;
                    result += (char)ucode;
                    continue;
                }
            }

            // 2-byte cid
            cid = (chars[chrIdx] << 8) | chars[chrIdx + 1];
            range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid && r.NumberOfBytes == 2);
            if (range != null)
            {
                if (range.Mapping.TryGetValue(cid, out int ucode))
                {
                    // chars[chrIdx] = (char)ucode;
                    result += (char)ucode;
                    chrIdx++;
                    continue;
                }else{
                    result += (char)cid;
                    chrIdx++;
                    continue;
                }
            }

            //TODO 3 and 4 byte

            //Fallback on using the cid
            result.Append((char)cid);


            // for (int numBytes = 1; numBytes <= 4; numBytes++)
            // {
            //     char
            //     int cid = BitConverter.ToInt32()
            //     var range = CodeSpaceRanges.FirstOrDefault()
            // }
        }

        // var newChars = chars
        //         .Where(c => c != '\0')
        //         .Select(b => cmap.ContainsKey(b) ? (char)cmap[b] : b)
        //         .ToArray();


        // text = new string(newChars);

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

    private void AddMapping(int cid, int ucode, int lengthInBytes)
    {
        //Find the proper codespace range and add the mapping
        var range = CodeSpaceRanges.FirstOrDefault(r => r.Low <= cid && r.High >= cid && r.NumberOfBytes == lengthInBytes);
        if (range != null)
        {
            range.Mapping[cid] = ucode;
        }
    }

    ///
    /// Pase the contents of a CMAP table from beginbfchar to endbfchar
    ///
    private void ParseBFChar(string bfChar)
    {
        string pattern = @"\s?<([a-fA-F0-9]+)>\s?<([a-fA-F0-9]{4})>";
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
                    int cid = Convert.ToInt32(match.Groups[1].Value, 16);
                    //TODO -- this needs to be different.. can be multiple chars
                    int ucode = Convert.ToInt32(match.Groups[2].Value, 16);
                    AddMapping(cid, ucode, match.Groups[1].Value.Length / 2);
                }
                catch (Exception)
                {
                    //TODO -- I think this happens when multiple cids match one ucode
                    //They are all crammed into 1 big number. We need to know how many bytes
                    //the map uses, and loop over the cids.
                    Debug.WriteLine($"Oops.. we still need to handle this. <{match.Groups[1]}> <{match.Groups[2]}>");
                }
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
                int fromGlyf = Convert.ToInt32(match.Groups[1].Value, 16);
                int toGlyf = Convert.ToInt32(match.Groups[2].Value, 16);
                int ucode = Convert.ToInt32(match.Groups[3].Value, 16);

                //Ensure to is > then from
                if (fromGlyf > toGlyf) continue;

                //Map all chars from fromGlyf to toGlyf and add
                for (int i = 0; fromGlyf + i <= toGlyf; i++)
                {
                    int glyf = fromGlyf + i;
                    AddMapping(glyf, ucode, match.Groups[1].Value.Length / 2);
                }
            }
            else
            {
                //maybe the format was <02> <02> [<0066006C>]
                throw new NotImplementedException("Lower hanging fruit first");
            }
        }
    }
}


//Ooops I was doing beginbfchar early....
// var highStr = match.Groups[2].Value;
//                 var highBytes = new byte[highStr.Length / 2];
//                 for(int i = 0; i < highStr.Length / 2; i++){
//                     highBytes[i] = Convert.ToByte(highStr.Substring(i*2, 2));
//                 }
//                 var high = Encoding.BigEndianUnicode.GetString(new byte[]{0x00, 0x66, 0x00, 0x66, 0x00, 0x69})