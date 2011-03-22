using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net;

namespace CookComputing.XmlRpc
{
  class WebClient
  {
    public object Call(Uri uri, Stream inputStream, Stream outputStream)
    {
      WebRequest webRequest = WebRequest.Create(uri);

      return null;

    }

    public IAsyncResult BeginCall(Uri uri, Stream inputStream, Stream outputStream, 
      AsyncCallback asyncCallback, object state)
    {
      WebRequest webRequest = WebRequest.Create(uri);
      webRequest.Method = "POST";
      webRequest.ContentType = "text/xml";

      var result = new CallAsyncResult(webRequest, inputStream, outputStream, 
        asyncCallback, state, this, "call");
      result.Process();
      return result;
    }

    public object EndCall(IAsyncResult result)
    {
      object ret = AsyncResultWithTimeout<object>.End(result, this, "call");
      return ret;
    }


    class CallAsyncResult : AsyncResultWithTimeout<object>
    {
      Stream _inputStream;
      Stream _outputStream;
      object _state;
      WebRequest _webRequest;
      Stream _reqStream;
      WebResponse _webResponse;
      Stream _responseStream;
      byte[] _buffer = new byte[4096];

      public CallAsyncResult(WebRequest webRequest, Stream inputStream, Stream outputStream,
        AsyncCallback asyncCallback, object state, object owner, string id)
        : base(asyncCallback, state, owner, "call", TimeSpan.FromMilliseconds(10))
      {
        _webRequest = webRequest;
        _inputStream = inputStream;
        _outputStream = outputStream;
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
          _reqStream = _webRequest.EndGetRequestStream(asyncResult);
          int count = _inputStream.Read(_buffer, 0, _buffer.Length);
          _reqStream.BeginWrite(_buffer, 0, count, GetWriteStreamCallback, asyncResult.AsyncState);
        }
        catch (Exception ex)
        {
          ProcessAsyncException(ex);
        }
      }

      void GetWriteStreamCallback(IAsyncResult asyncResult)
      {
        _reqStream.EndWrite(asyncResult);
        int count = _inputStream.Read(_buffer, 0, _buffer.Length);
        if (count > 0)
          _reqStream.BeginWrite(_buffer, 0, count, GetWriteStreamCallback, _state);
        else
        {
          _reqStream.Close();
          _webRequest.BeginGetResponse(GetResponseCallback, asyncResult.AsyncState);
        }
      }

      void GetResponseCallback(IAsyncResult asyncResult)
      {
        try
        {
          _webResponse = _webRequest.GetResponse();
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
          _outputStream.Write(_buffer, 0, count);
          if (_outputStream.Length == _webResponse.ContentLength || count == 0)
          {
            _outputStream.Position = 0;
            SetResult(null);
            Complete(null, false);
          }
          else
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
}
