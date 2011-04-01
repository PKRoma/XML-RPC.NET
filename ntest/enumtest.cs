using System;
using System.IO;
using CookComputing.XmlRpc;
using NUnit.Framework;

namespace ntest
{
  public enum IntEnum
  {
    Zero,
    One,
    Two,
  }

  public enum LongEnum : long
  {
    Zero = 0,
    One = 1,
    MaxIntPlusOne = (long)Int32.MaxValue + 1,
  }

  class enumtest
  {
    const long maxIntPlusOne = (long)Int32.MaxValue + 1;

    [Test]
    public void SerializeRequest()
    {
      Stream stm = new MemoryStream();
      XmlRpcRequest req = new XmlRpcRequest();
      req.args = new Object[] { IntEnum.Two };
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
      Assert.AreEqual("<value><i4>" + maxIntPlusOne.ToString() + "</i4>", xml);
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
        typeof(IntEnum));
      IntEnum ret = (IntEnum)response.retVal;
      Assert.AreEqual(IntEnum.Two, ret);
    }


    [Test]
    public void DeserializeIntEnum()
    {
      string xml = "<value><i4>2</i4>";
      object o = Utils.ParseValue(xml, typeof(IntEnum));
      Assert.IsInstanceOf<IntEnum>(o);
      Assert.AreEqual(IntEnum.Two, o);
    }

    [Test]
    [ExpectedException(typeof(XmlRpcInvalidEnumValue))]
    public void DeserializeMissingValue()
    {
      string xml = "<value><i4>1234</i4>";
      object o = Utils.ParseValue(xml, typeof(IntEnum));
      Assert.IsInstanceOf<IntEnum>(o);
      Assert.AreEqual(IntEnum.Two, o);
    }

    [Test]
    [ExpectedException(typeof(XmlRpcInvalidEnumValue))]
    public void DeserializeIntOverflow()
    {
      string xml = "<value><i4>" + maxIntPlusOne.ToString() + "</i4>";
      object o = Utils.ParseValue(xml, typeof(IntEnum));
    }
  }
}


/*
The base-type of an enumeration may be one of the following: 
  byte, sbyte, short, ushort, int, uint, long or ulong. 
  If no base-type is declared, than the default of int is use

 Enum.GetName - retrieves the name of the constant in the enumeration that has a specific value.
 Enum.GetNames - retrieves an array of the names of the constants in the enumeration.
 Enum.GetValues - retrieves an array of the values of the constants in the enumeration.
 Enum.IsDefined - returns an indication whether a constant with a specified value exists in a specified enumeration.



*/