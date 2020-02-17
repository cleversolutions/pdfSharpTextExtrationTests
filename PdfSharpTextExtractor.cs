using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Pdf.IO;

public class PdfSharpTextExtractor
{
    private Dictionary<string, FontResource> FontLookup;
    private string CurrentFont;

    public PdfSharpTextExtractor()
    {
        FontLookup = new Dictionary<string, FontResource>();
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
                // if(pageIdx == 2) break;
                // if (pageIdx <= 2) continue;
                if (pageIdx == 4) break;
                Debug.WriteLine($"Processing Page {pageIdx}");

                // generate the lookup tables we will need for each page
                ParseFonts(page);

                // extract the text
                ExtractText(ContentReader.ReadContent(page), result);

                // delineate each page with a newline
                result.AppendLine();

            }
            return result.ToString();
        }
    }

    ///
    /// Build a dictionary of font names and their associated information used to encode the data
    /// as unicode strings.
    ///
    private void ParseFonts(PdfPage page)
    {
        var fontResource = page.Resources.Elements.GetDictionary("/Font")?.Elements;
        if (fontResource == null) return;
        //All that above isn't going to do, but it's close...
        foreach (var fontName in fontResource.Keys)
        {
            var resource = fontResource[fontName] as PdfReference;
            var font = new FontResource(fontName, resource);
            
            FontLookup[fontName] = font;
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
        }
        // if(obj.OpCode.OpCodeName == OpCodeName.ET){
        //     target.Append("\n");
        // }
    }

    private void ExtractTextFromString(CString obj, StringBuilder target)
    {
        string text = obj.Value;

        if (!string.IsNullOrEmpty(CurrentFont) && FontLookup.ContainsKey(CurrentFont))
        {
            //Do character sub with the current fontMap
            var font = FontLookup[CurrentFont];
            text = font.Encode(text);
        }
        else
        {
            Debug.WriteLine($"Font {CurrentFont ?? "(null)"} not found.{text}");
        }
        target.Append(text);
    }
}