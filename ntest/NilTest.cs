using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Reflection;
using System.Threading;
using  NUnit.Framework;
using CookComputing.XmlRpc;
namespace ntest

// TODO: test any culture dependencies
{
  [TestFixture]
  public class NilTest
  {
    [Test]
    public void SerializeRequestNil()
    {
      Stream stm = new MemoryStream();
      XmlRpcRequest req = new XmlRpcRequest();
      req.args = new Object[] { null, 1234567 };
      req.method = "NilMethod";
      req.mi = this.GetType().GetMethod("NilMethod");
      var ser = new XmlRpcRequestSerializer();
      ser.Indentation = 4;
      ser.SerializeRequest(stm, req);
      stm.Position = 0;
      TextReader tr = new StreamReader(stm);
      string reqstr = tr.ReadToEnd();
      Assert.AreEqual(
        @"<?xml version=""1.0""?>
<methodCall>
    <methodName>NilMethod</methodName>
    <params>
        <param>
            <value>
                <nil />
            </value>
        </param>
        <param>
            <value>
                <i4>1234567</i4>
            </value>
        </param>
    </params>
</methodCall>", reqstr);
    }

    [XmlRpcMethod]
    public int? NilMethod(int? x, int? y)
    {
      return null;
    }

    [Test]
    public void SerializeRequestNilParams()
    {
      Stream stm = new MemoryStream();
      XmlRpcRequest req = new XmlRpcRequest();
      req.args = new Object[] { new object[] { 1, null, 2} };
      req.method = "NilParamsMethod";
      req.mi = this.GetType().GetMethod("NilParamsMethod");
      var ser = new XmlRpcRequestSerializer();
      ser.Indentation = 4;
      ser.SerializeRequest(stm, req);
      stm.Position = 0;
      TextReader tr = new StreamReader(stm);
      string reqstr = tr.ReadToEnd();
      Assert.AreEqual(
        @"<?xml version=""1.0""?>
<methodCall>
    <methodName>NilParamsMethod</methodName>
    <params>
        <param>
            <value>
                <i4>1</i4>
            </value>
        </param>
        <param>
            <value>
                <nil />
            </value>
        </param>
        <param>
            <value>
                <i4>2</i4>
            </value>
        </param>
    </params>
</methodCall>", reqstr);
    }

    [Test]
    public void SerializeRequestArrayWithNull()
    {
      Stream stm = new MemoryStream();
      XmlRpcRequest req = new XmlRpcRequest();
      string[] array = new string[] { "AAA", null, "CCC" };
      req.args = new Object[] { array };
      req.method = "ArrayMethod";
      req.mi = this.GetType().GetMethod("ArrayMethod");
      var ser = new XmlRpcRequestSerializer();
      ser.Indentation = 4;
      ser.SerializeRequest(stm, req);
      stm.Position = 0;
      TextReader tr = new StreamReader(stm);
      string reqstr = tr.ReadToEnd();
      Assert.AreEqual(
        @"<?xml version=""1.0""?>
<methodCall>
    <methodName>ArrayMethod</methodName>
    <params>
        <param>
            <value>
                <array>
                    <data>
                        <value>
                            <string>AAA</string>
                        </value>
                        <value>
                            <nil />
                        </value>
                        <value>
                            <string>CCC</string>
                        </value>
                    </data>
                </array>
            </value>
        </param>
    </params>
</methodCall>", reqstr);
    }

    public void ArrayMethod(string[] strings)
    {
    }

    [Test]
    public void SerializeRequestStructWithNil()
    {
      Stream stm = new MemoryStream();
      XmlRpcRequest req = new XmlRpcRequest();
      req.args = new Object[] { new Bounds() };
      req.method = "NilMethod";
      req.mi = this.GetType().GetMethod("NilMethod");
      var ser = new XmlRpcRequestSerializer();
      ser.Indentation = 4;
      ser.SerializeRequest(stm, req);
      stm.Position = 0;
      TextReader tr = new StreamReader(stm);
      string reqstr = tr.ReadToEnd();
      Assert.AreEqual(
        @"<?xml version=""1.0""?>
<methodCall>
    <methodName>NilMethod</methodName>
    <params>
        <param>
            <value>
                <struct>
                    <member>
                        <name>lowerBound</name>
                        <value>
                            <nil />
                        </value>
                    </member>
                    <member>
                        <name>upperBound</name>
                        <value>
                            <nil />
                        </value>
                    </member>
                </struct>
            </value>
        </param>
    </params>
</methodCall>", reqstr);
    }

    [XmlRpcNullMapping(NullMappingAction.Nil)]
    public struct StructWithArray
    {
      public int?[] ints;
    }

    [XmlRpcMethod]
    public void StructWithArrayMethod(StructWithArray x)
    {
    }

    [Test]
    public void SerializeRequestStructArrayWithNil()
    {
      Stream stm = new MemoryStream();
      XmlRpcRequest req = new XmlRpcRequest();
      req.args = new Object[] { new StructWithArray { ints = new int?[] { 1, null, 3 } } };
      req.method = "NilMethod";
      req.mi = this.GetType().GetMethod("NilMethod");
      var ser = new XmlRpcRequestSerializer();
      ser.Indentation = 4;
      ser.SerializeRequest(stm, req);
      stm.Position = 0;
      TextReader tr = new StreamReader(stm);
      string reqstr = tr.ReadToEnd();
      Assert.AreEqual(
        @"<?xml version=""1.0""?>
<methodCall>
    <methodName>NilMethod</methodName>
    <params>
        <param>
            <value>
                <struct>
                    <member>
                        <name>ints</name>
                        <value>
                            <array>
                                <data>
                                    <value>
                                        <i4>1</i4>
                                    </value>
                                    <value>
                                        <nil />
                                    </value>
                                    <value>
                                        <i4>3</i4>
                                    </value>
                                </data>
                            </array>
                        </value>
                    </member>
                </struct>
            </value>
        </param>
    </params>
</methodCall>", reqstr);
    }

    public void NilParamsMethod(params int?[] numbers)
    {
    }

    [Test]
    public void DeserializeRequestStructWithNil()
    {
      string xml = @"<?xml version=""1.0""?>
<methodCall>
    <methodName>StructWithArrayMethod</methodName>
    <params>
        <param>
            <value>
                <struct>
                    <member>
                        <name>ints</name>
                        <value>
                            <array>
                                <data>
                                    <value>
                                        <i4>1</i4>
                                    </value>
                                    <value>
                                        <nil />
                                    </value>
                                    <value>
                                        <i4>3</i4>
                                    </value>
                                </data>
                            </array>
                        </value>
                    </member>
                </struct>
            </value>
        </param>
    </params>
</methodCall>";
      StringReader sr = new StringReader(xml);
      var deserializer = new XmlRpcRequestDeserializer();
      XmlRpcRequest request = deserializer.DeserializeRequest(sr, this.GetType());

      Assert.AreEqual(request.method, "StructWithArrayMethod", "method is TestString");
      Assert.AreEqual(1, request.args.Length);
      Assert.IsInstanceOf<StructWithArray>(request.args[0], "argument is StructWithArray");
      int?[] arg = ((StructWithArray)request.args[0]).ints;
      Assert.AreEqual(1, arg[0]);
      Assert.IsNull(arg[1]);
      Assert.AreEqual(3, arg[2]);
    }

    [Test]
    public void DeserializeRequestNilMethod()
    {
      string xml = @"<?xml version=""1.0"" ?> 
<methodCall>
  <methodName>NilMethod</methodName> 
  <params>
    <param>
      <value><nil /></value>
    </param>
    <param>
      <value><int>12345</int></value>
    </param>
  </params>
</methodCall>";
      StringReader sr = new StringReader(xml);
      var deserializer = new XmlRpcRequestDeserializer();
      XmlRpcRequest request = deserializer.DeserializeRequest(sr, this.GetType());

      Assert.AreEqual(request.method, "NilMethod", "method is TestString");
      Assert.IsNull(request.args[0], "argument is null");
      Assert.AreEqual(12345, (int)request.args[1], "argument is 12345");
    }




    [Test]
    public void DeserializeResponseNilMethod()
    {
      string xml = @"<?xml version=""1.0"" ?> 
<methodResponse>
  <params>
    <param>
      <value><nil /></value>
    </param>
  </params>
</methodCall>";
      StringReader sr = new StringReader(xml);
      var deserializer = new XmlRpcResponseDeserializer();
      XmlRpcResponse response = deserializer.DeserializeResponse(sr, this.GetType());

      Assert.IsNull(response.retVal, "return value is null");
    }

    [Test]
    public void DeserializeResponseStructWithNil()
    {
      string xml = @"<?xml version=""1.0"" ?> 
<methodResponse>
  <params>
    <param>
      <value>
        <struct>
          <member>
            <name>lowerBound</name>
            <value><nil/></value>
          </member>
          <member>
            <name>upperBound</name>
            <value><nil/></value>
          </member>
        </struct>
      </value>
    </param>
  </params>
</methodResponse>";
      StringReader sr = new StringReader(xml);
      var deserializer = new XmlRpcResponseDeserializer();
      XmlRpcResponse response = deserializer.DeserializeResponse(sr, typeof(Bounds));
      Assert.IsInstanceOf<Bounds>(response.retVal);
      Bounds bounds = response.retVal as Bounds;
      Assert.IsNull(bounds.lowerBound);
      Assert.IsNull(bounds.upperBound);
    }

    [Test]
    public void DeserializeNilObject()
    {
      string xml = "<value><nil /></value>";
      object o = Utils.ParseValue(xml, typeof(object));
      Assert.IsNull(o);
    }
  }

  [XmlRpcNullMapping(NullMappingAction.Nil)]
  class Bounds
  {
    public int? lowerBound;
    public int? upperBound;
  }

}


