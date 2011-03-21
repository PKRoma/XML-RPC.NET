using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using CookComputing.XmlRpc;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace CookComputing.XmlRpc
{
  public class CallAsyncResult : AsyncResult<object>
  {
    WebRequest webRequest;
    Action<Stream> _writer;
    ReaderDelegate _reader;
    object _state;
    WebRequest _webRequest;
    WebResponse _webResponse;
    Stream _responseStream;
    Stream _bufferStream;
    byte[] _buffer = new byte[4096];

    public CallAsyncResult(WebRequest webRequest, Action<Stream> writer,
      ReaderDelegate reader, AsyncCallback asyncCallback, object state, object owner, string id)
      : base(asyncCallback, state, owner, "call")
    {
      _webRequest = webRequest;
      _writer = writer;
      _reader = reader;
      _state = state;
    }

    override internal void Process()
    {
      _webRequest.BeginGetRequestStream(new AsyncCallback(GetRequestStreamCallback), _state);
    }

    void GetRequestStreamCallback(IAsyncResult asyncResult)
    {
      try
      {
        Stream reqStream = _webRequest.EndGetRequestStream(asyncResult);
        _writer(reqStream);
        reqStream.Flush();
        reqStream.Close();
        _webRequest.BeginGetResponse(GetResponseCallback, _state);
      }
      catch (Exception ex)
      {
        ProcessAsyncException(ex);
      }
    }

    void GetResponseCallback(IAsyncResult asyncResult)
    {
      try
      {
        _webResponse = _webRequest.GetResponse();
        if (_webResponse.ContentLength != -1)
          _bufferStream = new MemoryStream((int)_webResponse.ContentLength);
        else
          _bufferStream = new MemoryStream();
        _responseStream = _webResponse.GetResponseStream();
        _responseStream.BeginRead(_buffer, 0, _buffer.Length, ReadResponseCallback, null);
      }
      catch (Exception ex)
      {
        ProcessAsyncException(ex);
      }
    }

    void ReadResponseCallback(IAsyncResult asyncResult)
    {
      try
      {
        int count = _responseStream.EndRead(asyncResult);
        _bufferStream.Write(_buffer, 0, count);
        if (_bufferStream.Length == _webResponse.ContentLength || count == 0)
        {
          _bufferStream.Position = 0;
          object reto = _reader(_bufferStream);
          SetResult(reto);
          Complete(null, false);
        }
        _responseStream.BeginRead(_buffer, 0, _buffer.Length, ReadResponseCallback, null);
      }
      catch (Exception ex)
      {
        ProcessAsyncException(ex);
      }
    }

    void ProcessAsyncException(Exception ex)
    {
      Complete(ex);
    }


  }
}
