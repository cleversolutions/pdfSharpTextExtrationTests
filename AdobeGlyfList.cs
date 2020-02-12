using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;

public class AdobeGlyfList
{
    private static AdobeGlyfList _instance = null;
    private Dictionary<string, string> Dictionary { get; set; }

    private AdobeGlyfList()
    {
        Init();
    }

    public static AdobeGlyfList Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new AdobeGlyfList();
            }
            return _instance;
        }
    }

    public string Lookup(string glyph){
        if(glyph.StartsWith(@"/")) glyph = glyph.Substring(1);
        Dictionary.TryGetValue(glyph, out string unicode);
        return unicode;
    }

    private void Init()
    {
        Dictionary = new Dictionary<string, string>();
        string line;

        // Read the file and display it line by line.  
        System.IO.StreamReader file = new System.IO.StreamReader(@"glyphlist.txt");
        while ((line = file.ReadLine()) != null)
        {
            if(line.StartsWith("#")) continue;
            var match = Regex.Match(line, @"(?<glyph>^.*);((?<unicode>[0-9A-F]{4})\s*)+");
            if(match.Success){
                string glyphName = match.Groups["glyph"].Value;
                var chars  = match.Groups["unicode"].Captures
                    .Select(c => (char)Convert.ToInt16(c.Value, 16));
                 string unicode = string.Concat(chars);
                 Dictionary[glyphName] = unicode;
            }
        }

        file.Close();
    }
}