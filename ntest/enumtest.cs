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

  public enum LongEnum2 : long
  {
    i = 0,
    j = 1,
    k = (long)Int32.MaxValue + 1
  }

  class enumtest
  {
    [Test]
    public void SerializeRequest()
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
    public void SerializeIntEnum()
    {
      string xml = Utils.SerializeValue(666, false);
      Assert.AreEqual("<value><i4>2</i4>", xml);
    }

    [Test]
    public void SerializeLongEnum()
    {
      long lnum = (long)Int32.MaxValue + 1;
      string xml = Utils.SerializeValue(lnum, false);
      Assert.AreEqual("<value><i4>" + lnum.ToString() + "</i4>", xml);
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

  }
}


/*
Test cases : 

ser - number mapped to i4 or i8 depending on enum type

deser - number to be deserialized doesnt exist in enum
deser - number overflows range of enum





*/