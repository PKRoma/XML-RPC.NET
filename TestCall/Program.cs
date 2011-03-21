using System;
using System.Collections.Generic;
using System.Text;
using CookComputing.XmlRpc;
using System.IO;
using System.Xml;

class Program
{
  static void Main(string[] args)
  {
    var call = new XmlRpcCall();
    IAsyncResult ar = call.BeginCall(new Uri("http://www.cookcomputing.com/xmlrpcsamples/RPC2.ashx"),
      new WebSettings(), Writer, Reader, null, "ABCD");
    ar.AsyncWaitHandle.WaitOne();
    object ret = call.EndCall(ar);

  }

  static string request =
           @"<?xml version=""1.0""?>
<methodCall>
    <methodName>Foo</methodName>
    <params>
        <param>
            <value>
                <int>1</int>
            </value>
        </param>
    </params>
</methodCall>";

  static void Writer(Stream stream)
  {
    var request = new XmlRpcRequest { method="examples.getStateName", args = new object[] { 1 } };
    var serializer = new XmlRpcRequestSerializer();
    serializer.SerializeRequest(stream, request);
  }

  static object Reader(Stream stream)
  {
    var deserializer = new XmlRpcResponseDeserializer();
    var response = deserializer.DeserializeResponse(stream, null);

    return response.retVal;
  }
}
