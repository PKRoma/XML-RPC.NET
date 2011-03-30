using System;
using System.IO;
using CookComputing.XmlRpc;
using NUnit.Framework;

namespace ntest
{
  public enum TestEnum
  {
    a,
    b,
    c,
  }

  class enumtest
  {
    [Test]
    public void Serialize()
    {
      Stream stm = new MemoryStream();
      XmlRpcRequest req = new XmlRpcRequest();
      req.args = new Object[] { TestEnum.c };
      req.method = "Foo";
      var ser = new XmlRpcRequestSerializer();
      ser.SerializeRequest(stm, req);
      stm.Position = 0;
      TextReader tr = new StreamReader(stm);
      string reqstr = tr.ReadToEnd();

      Assert.AreEqual(
        @"<?xml version=""1.0""?>
<methodCall>
  <methodName>Foo</methodName>
  <params>
    <param>
      <value>
        <i4>2</i4>
      </value>
    </param>
  </params>
</methodCall>", reqstr);
    }

    [Test]
    public void Deserialize()
    {
      string xml = 
@"<?xml version=""1.0"" encoding=""iso-8859-1""?>
<methodResponse>
  <params>
    <param>
      <value>
        <i4>2</i4>
      </value>
    </param>
  </params>
</methodResponse>";
      StringReader sr1 = new StringReader(xml);
      var deserializer = new XmlRpcResponseDeserializer();
      XmlRpcResponse response = deserializer.DeserializeResponse(sr1,
        typeof(TestEnum));
      TestEnum ret = (TestEnum)response.retVal;
      Assert.AreEqual(TestEnum.c, ret);
    }

    public enum LongEnum2 : long
    {
      i = (long)Int32.MaxValue + 1,
      j = 1,
      k = 0
    }

    public enum TestEnum3 : long
    {
      i = (long)Int32.MaxValue + 1,
      j = 1,
      k = 0
    }

    [Test]
    public void String_StringType()
    {
      string xml = @"<value><string>astring</string></value>";
      object obj = Utils.ParseValue(xml, typeof(string));
      Assert.AreEqual("astring", (string)obj);
    }
  }

}
