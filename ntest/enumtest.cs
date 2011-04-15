using System;
using System.IO;
using CookComputing.XmlRpc;
using NUnit.Framework;

namespace ntest
{

  // byte, sbyte, short, ushort, int, uint, long or ulong.
  public enum ByteEnum : byte
  {
    Zero,
    One,
    Two,
  }

  public enum SByteEnum : sbyte
  {
    Zero,
    One,
    Two,
  }

  public enum ShortEnum : short
  {
    Zero,
    One,
    Two,
  }

  public enum UShortEnum : ushort
  {
    Zero,
    One,
    Two,
  }

  public enum IntEnum : int
  {
    Zero,
    One,
    Two,
  }

  public enum UIntEnum : uint
  {
    Zero,
    One,
    Two,
  }

  public enum LongEnum : long
  {
    Zero = 0,
    One = 1,
    Two = 2,
    MaxIntPlusOne = (long)Int32.MaxValue + 1,
  }


  public enum ULongEnum : ulong
  {
    Zero = 0,
    One = 1,
    Two = 2,
    MaxUintPlusOne = (long)UInt32.MaxValue + 1,
  }


  class enumtest
  {
    const long maxIntPlusOne = (long)Int32.MaxValue + 1;
    const ulong maxUintPlusOne = (ulong)UInt32.MaxValue + 1;

    [Test]
    public void EnumXmlRpcType()
    {
      Assert.AreEqual(XmlRpcType.tInt32, XmlRpcTypeInfo.GetXmlRpcType(typeof(ByteEnum)), "byte");
      Assert.AreEqual(XmlRpcType.tInt32, XmlRpcTypeInfo.GetXmlRpcType(typeof(SByteEnum)), "sbyte");
      Assert.AreEqual(XmlRpcType.tInt32, XmlRpcTypeInfo.GetXmlRpcType(typeof(ShortEnum)), "short");
      Assert.AreEqual(XmlRpcType.tInt32, XmlRpcTypeInfo.GetXmlRpcType(typeof(UShortEnum)), "ushort");
      Assert.AreEqual(XmlRpcType.tInt32, XmlRpcTypeInfo.GetXmlRpcType(typeof(IntEnum)), "int");
      Assert.AreEqual(XmlRpcType.tInt64, XmlRpcTypeInfo.GetXmlRpcType(typeof(UIntEnum)), "uint");
      Assert.AreEqual(XmlRpcType.tInt64, XmlRpcTypeInfo.GetXmlRpcType(typeof(LongEnum)), "long");
      Assert.AreEqual(XmlRpcType.tInvalid, XmlRpcTypeInfo.GetXmlRpcType(typeof(ULongEnum)), "ulong");
    }

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
    public void SerializeByteEnum()
    {
      string xml = Utils.SerializeValue(ByteEnum.Two, false);
      Assert.AreEqual("<value><i4>2</i4></value>", xml);
    }

    [Test]
    public void SerializeSByteEnum()
    {
      string xml = Utils.SerializeValue(SByteEnum.Two, false);
      Assert.AreEqual("<value><i4>2</i4></value>", xml);
    }

    [Test]
    public void SerializeShortEnum()
    {
      string xml = Utils.SerializeValue(ShortEnum.Two, false);
      Assert.AreEqual("<value><i4>2</i4></value>", xml);
    }

    [Test]
    public void SerializeUShortEnum()
    {
      string xml = Utils.SerializeValue(UShortEnum.Two, false);
      Assert.AreEqual("<value><i4>2</i4></value>", xml);
    }

    [Test]
    public void SerializeIntEnum()
    {
      string xml = Utils.SerializeValue(IntEnum.Two, false);
      Assert.AreEqual("<value><i4>2</i4></value>", xml);
    }

    [Test]
    public void SerializeUIntEnum()
    {
      string xml = Utils.SerializeValue(UIntEnum.Two, false);
      Assert.AreEqual("<value><i8>2</i8></value>", xml);
    }

    [Test]
    public void SerializeLongEnum()
    {
      string xml = Utils.SerializeValue(LongEnum.MaxIntPlusOne, false);
      Assert.AreEqual("<value><i8>" + maxIntPlusOne.ToString() + "</i8></value>", xml);
    }

    [Test]
    [ExpectedException(typeof(XmlRpcUnsupportedTypeException))]
    public void SerializeULongEnum()
    {
      string xml = Utils.SerializeValue(ULongEnum.MaxUintPlusOne, false);
      Assert.AreEqual("<value><i8>" + maxIntPlusOne.ToString() + "</i8></value>", xml);
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
    public void DeserializeByteEnum()
    {
      string xml = "<value><i4>2</i4></value>";
      object o = Utils.ParseValue(xml, typeof(ByteEnum));
      Assert.IsInstanceOf<ByteEnum>(o);
      Assert.AreEqual(ByteEnum.Two, o);
    }

    [Test]
    public void DeserializeSByteEnum()
    {
      string xml = "<value><i4>2</i4></value>";
      object o = Utils.ParseValue(xml, typeof(SByteEnum));
      Assert.IsInstanceOf<SByteEnum>(o);
      Assert.AreEqual(SByteEnum.Two, o);
    }

    [Test]
    public void DeserializeShortEnum()
    {
      string xml = "<value><i4>2</i4></value>";
      object o = Utils.ParseValue(xml, typeof(ShortEnum));
      Assert.IsInstanceOf<ShortEnum>(o);
      Assert.AreEqual(ShortEnum.Two, o);
    }

    [Test]
    public void DeserializeUShortEnum()
    {
      string xml = "<value><i4>2</i4></value>";
      object o = Utils.ParseValue(xml, typeof(UShortEnum));
      Assert.IsInstanceOf<UShortEnum>(o);
      Assert.AreEqual(UShortEnum.Two, o);
    }

    [Test]
    public void DeserializeIntEnum()
    {
      string xml = "<value><i4>2</i4></value>";
      object o = Utils.ParseValue(xml, typeof(IntEnum));
      Assert.IsInstanceOf<IntEnum>(o);
      Assert.AreEqual(IntEnum.Two, o);
    }

    [Test]
    public void DeserializeUIntEnum()
    {
      string xml = "<value><i8>2</i8></value>";
      object o = Utils.ParseValue(xml, typeof(UIntEnum));
      Assert.IsInstanceOf<UIntEnum>(o);
      Assert.AreEqual(UIntEnum.Two, o);
    }

    [Test]
    public void DeserializeLongEnum()
    {
      string xml = "<value><i8>2</i8></value>";
      object o = Utils.ParseValue(xml, typeof(LongEnum));
      Assert.IsInstanceOf<LongEnum>(o);
      Assert.AreEqual(LongEnum.Two, o);
    }

    [Test]
    [ExpectedException(typeof(XmlRpcInvalidEnumValue))]
    public void DeserializeULongEnum()
    {
      string xml = "<value><i8>2</i8></value>";
      object o = Utils.ParseValue(xml, typeof(ULongEnum));
      Assert.IsInstanceOf<ULongEnum>(o);
      Assert.AreEqual(ULongEnum.Two, o);
    }

    [Test]
    [ExpectedException(typeof(XmlRpcInvalidEnumValue))]
    public void DeserializeMissingValue()
    {
      string xml = "<value><i4>1234</i4></value>";
      object o = Utils.ParseValue(xml, typeof(IntEnum));
      Assert.IsInstanceOf<IntEnum>(o);
      Assert.AreEqual(IntEnum.Two, o);
    }

    [Test]
    [ExpectedException(typeof(XmlRpcInvalidEnumValue))]
    public void DeserializeIntOverflow()
    {
      string xml = "<value><i4>" + maxIntPlusOne.ToString() + "</i4></value>";
      object o = Utils.ParseValue(xml, typeof(IntEnum));
    }
  }
}