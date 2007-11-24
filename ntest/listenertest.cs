using System;
using CookComputing.XmlRpc;
using NUnit.Framework;

#if !FX1_0

namespace ntest
{
  [TestFixture]
  public class ListenerTest
  {
    Listener _listener = new Listener(new StateNameListnerService());

    [TestFixtureSetUp]
    public void Setup()
    {
      _listener.Start();
    }

    [TestFixtureTearDown]
    public void TearDown()
    {
      _listener.Stop();
    }


    [Test]
    public void MakeCall()
    {
      IStateName proxy = XmlRpcProxyGen.Create < IStateName>();
      proxy.Url = "http://127.0.0.1:11000/";
      string name = proxy.GetStateName(1);
    }
  }
}

#endif