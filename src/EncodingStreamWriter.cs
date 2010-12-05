using System.IO;
using System.Text;

namespace CookComputing.XmlRpc
{
  public class EncodingStreamWriter : StreamWriter
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
