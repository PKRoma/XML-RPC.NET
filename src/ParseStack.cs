using System;
using System.Collections.Generic;
using System.Text;

namespace CookComputing.XmlRpc
{
    public class ParseStack : Stack<string>
    {
      public ParseStack(string parseType)
      {
        m_parseType = parseType;
      }

      void Push(string str)
      {
        base.Push(str);
      }

      public string ParseType
      {
        get { return m_parseType; }
      }

      public string m_parseType = "";
    }
  }
