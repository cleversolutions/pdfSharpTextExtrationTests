using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;

public class FontResource
{
    ///
    /// Parse the fonts from the page's resources structure including the encoding differences and CMAPs
    ///
    public FontResource(string fontName, PdfReference resource)
    {
        var resourceElements = (resource?.Value as PdfDictionary)?.Elements;
        //Extract the encoding differences array
        var differences = resourceElements?.GetDictionary("/Encoding")?.Elements?.GetArray("/Differences");
        if (differences != null)
        {
            _differences = differences;
        }

        //Extract the CMAPs 
        var unicodeDictionary = resourceElements?.GetDictionary("/ToUnicode");
        var stream = unicodeDictionary?.Stream;
        if (stream != null)
        {
            _cmap = new CMap(stream, fontName);
        }
    }


    private readonly CMap _cmap;
    private PdfArray _differences;

    public string Encode(string text)
    {
        // convert any characters that fall in the /Encoding /Differences array
        var differencesLength= _differences.Elements.Count ;
        if (_differences != null && differencesLength > 0)
        {
            var glyphMap = AdobeGlyfList.Instance;
            var chars = text.ToCharArray().Select(ch => {
                if(differencesLength > ch){
                    var item = _differences.Elements[ch];
                    if(item is PdfName name){
                        return glyphMap.Lookup(name.Value);    
                    }
                    if(item is PdfInteger number){
                        return ((char)number.Value).ToString();
                    } 
                }
                return ch.ToString();
            });
            return string.Concat(chars);
        }

        // if this font has a /ToUnciode CMAP then we will first resolve the text through it
        if (_cmap != null)
        {
            return _cmap.Encode(text);
        }

        // fallback on just displaying the text
        return text;
    }
}