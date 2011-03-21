using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using CookComputing.XmlRpc;
using System.Net;
using System.Security.Cryptography.X509Certificates;

public delegate object ReaderDelegate(Stream stream);

namespace CookComputing.XmlRpc
{
  public class XmlRpcCall
  {
    public object Call(Uri uri, WebSettings settings, Action<Stream> writer,
      ReaderDelegate reader) 
    {
      WebRequest webRequest = WebRequest.Create(uri);
      SetProperties(webRequest, settings);
      SetRequestHeaders(settings.Headers, webRequest);
#if (!COMPACT_FRAMEWORK && !SILVERLIGHT)
      // SetClientCertificates(_settings.ClientCertificates, _webRequest);
#endif


      return null;

    }

    public IAsyncResult BeginCall(Uri uri, WebSettings settings, Action<Stream> writer,
      ReaderDelegate reader, AsyncCallback asyncCallback, object state)
    {
      WebRequest webRequest = WebRequest.Create(uri);
      SetProperties(webRequest, settings);
      SetRequestHeaders(settings.Headers, webRequest);
#if (!COMPACT_FRAMEWORK && !SILVERLIGHT)
      // SetClientCertificates(_settings.ClientCertificates, _webRequest);
#endif

      var result = new CallAsyncResult(webRequest, writer, reader, asyncCallback, state,
        this, "call");
      result.Process();
      return result;
    }

    public object EndCall(IAsyncResult result)
    {
      object ret = AsyncResult<object>.End(result, this, "call");
      return ret;
    }


    public void SetProperties(WebRequest webReq, WebSettings settings)
    {
      webReq.Method = "POST";
      webReq.ContentType = "text/xml";
      HttpWebRequest httpReq = (HttpWebRequest)webReq;
#if (!SILVERLIGHT)
      if (settings.Proxy != null)
        webReq.Proxy = settings.Proxy;
      httpReq.UserAgent = settings.UserAgent;
      httpReq.ProtocolVersion = settings.ProtocolVersion;
      httpReq.KeepAlive = settings.KeepAlive;
      httpReq.AllowAutoRedirect = settings.AllowAutoRedirect;
      webReq.PreAuthenticate = settings.PreAuthenticate;
      webReq.Timeout = settings.Timeout;
      webReq.Credentials = settings.Credentials;
      // Compact Framework sets this to false by default
      (webReq as HttpWebRequest).AllowWriteStreamBuffering = true;
#endif
#if (!COMPACT_FRAMEWORK && !SILVERLIGHT)
      httpReq.CookieContainer = settings.CookieContainer;
      webReq.ConnectionGroupName = settings.ConnectionGroupName;
#endif
#if (!COMPACT_FRAMEWORK && !FX1_0 && !SILVERLIGHT)
      httpReq.ServicePoint.Expect100Continue = settings.Expect100Continue;
      httpReq.ServicePoint.UseNagleAlgorithm = settings.UseNagleAlgorithm;
      if (settings.EnableCompression)
        webReq.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");
#endif
    }

    private void SetRequestHeaders(
      WebHeaderCollection headers,
      WebRequest webReq)
    {
      foreach (string key in headers)
      {
#if (!SILVERLIGHT)
        webReq.Headers.Add(key, headers[key]);
#endif
      }
    }
#if (!COMPACT_FRAMEWORK && !SILVERLIGHT)
    private void SetClientCertificates(
      X509CertificateCollection certificates,
      WebRequest webReq)
    {
      foreach (X509Certificate certificate in certificates)
      {
        HttpWebRequest httpReq = (HttpWebRequest)webReq;
        httpReq.ClientCertificates.Add(certificate);
      }
    }
#endif
  }
}
