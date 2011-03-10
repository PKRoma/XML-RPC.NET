using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;

namespace CookComputing.XmlRpc
{
  public class XmlRpcXmlWriter
  {
    public static XmlWriter Create(Stream stm, Encoding encoding, bool useIndentation, int indentation)
    {
      var stmWriter = new EncodingStreamWriter(stm, encoding);
      XmlWriter xtw = XmlWriter.Create(stmWriter, ConfigureXmlFormat(encoding, useIndentation, indentation));
      return xtw;
    }

    private static XmlWriterSettings ConfigureXmlFormat(Encoding encoding, bool useIndentation, int indentation)
    {
      if (useIndentation)
      {
        return new XmlWriterSettings
        {
          Indent = true,
          IndentChars = new string(' ', indentation),
          Encoding = encoding,
          NewLineHandling = NewLineHandling.None,
        };
      }
      else
      {
        return new XmlWriterSettings
        {
          Indent = false,
          Encoding = encoding
        };
      }
    }

    private class EncodingStreamWriter : StreamWriter
    {
      Encoding _encoding;

      public EncodingStreamWriter(Stream stm, Encoding encoding)
        : base(stm)
      {
        _encoding = encoding;
      }

      public override Encoding Encoding
      {
        get { return _encoding; }
      }
    }
  }
}
