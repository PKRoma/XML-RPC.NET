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

    WebClient client = new WebClient();
    Stream readStream = new MemoryStream();
    IAsyncResult ar = client.BeginGet(new Uri("http://www.sun.com"), readStream, null, null);
    ar.AsyncWaitHandle.WaitOne();

    client.EndGet(ar);

    var rdr = new StreamReader(readStream);
    string page = rdr.ReadToEnd();

  }


    //Stream inputStream = new MemoryStream();
    //Stream outputStream = new MemoryStream();
    //var request = new XmlRpcRequest
    //{
    //  method = "examples.getStateName",
    //  args = new object[] { 1 }
    //};
    //var serializer = new XmlRpcRequestSerializer();
    //serializer.SerializeRequest(inputStream, request);
    //inputStream.Position = 0;

    //var call = new WebClientX();
    //IAsyncResult ar = call.BeginCall(new Uri("http://www.cookcomputing.com/xmlrpcsamples/RPC2.ashx"),
    //  inputStream, outputStream, null, "ABCD");
    //ar.AsyncWaitHandle.WaitOne();
    //call.EndCall(ar);

    //outputStream.Position = 0;
    //var deserializer = new XmlRpcResponseDeserializer();
    //var response = deserializer.DeserializeResponse(outputStream, null);

    //object reto = response.retVal;
  //}



  static object Reader(Stream stream)
  {
    var deserializer = new XmlRpcResponseDeserializer();
    var response = deserializer.DeserializeResponse(stream, null);

    return response.retVal;
  }
}

