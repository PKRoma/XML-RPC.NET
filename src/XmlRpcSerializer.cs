/* 
XML-RPC.NET library
Copyright (c) 2001-2011, Charles Cook <charlescook@cookcomputing.com>

Permission is hereby granted, free of charge, to any person 
obtaining a copy of this software and associated documentation 
files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, 
publish, distribute, sublicense, and/or sell copies of the Software, 
and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be 
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES 
OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
DEALINGS IN THE SOFTWARE.
*/

// TODO: overriding default mapping action in a struct should not affect nested structs

namespace CookComputing.XmlRpc
{
  using System;
  using System.Collections;
  using System.Globalization;
  using System.IO;
  using System.Reflection;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Threading;
  using System.Xml;
  using System.Collections.Generic;

  public class XmlRpcSerializer
  {
    // public properties

    public int Indentation
    {
      get { return m_indentation; }
      set { m_indentation = value; }
    }
    int m_indentation = 2;

    public bool UseEmptyParamsTag
    {
      get { return m_bUseEmptyParamsTag; }
      set { m_bUseEmptyParamsTag = value; }
    }
    bool m_bUseEmptyParamsTag = true;

    public bool UseIndentation
    {
      get { return m_bUseIndentation; }
      set { m_bUseIndentation = value; }
    }
    bool m_bUseIndentation = true;

    public bool UseIntTag
    {
      get { return m_useIntTag; }
      set { m_useIntTag = value; }
    }
    bool m_useIntTag;

    public bool UseStringTag
    {
      get { return m_useStringTag; }
      set { m_useStringTag = value; }
    }
    bool m_useStringTag = true;

    public Encoding XmlEncoding
    {
      get { return m_encoding; }
      set { m_encoding = value; }
    }
    Encoding m_encoding = null;

    //#if (DEBUG)
    public
      //#endif
    void Serialize(
      XmlWriter xtw,
      Object o,
      NullMappingAction mappingAction)
    {
      Serialize(xtw, o, mappingAction, new List<object>());
    }

    //#if (DEBUG)
    public
      //#endif
    void Serialize(
      XmlWriter xtw,
      Object o,
      NullMappingAction mappingAction,
      List<object> nestedObjs)
    {
      if (nestedObjs.Contains(o))
        throw new XmlRpcUnsupportedTypeException(nestedObjs[0].GetType(),
          "Cannot serialize recursive data structure");
      nestedObjs.Add(o);
      try
      {
        xtw.WriteStartElement("", "value", "");
        XmlRpcType xType = XmlRpcTypeInfo.GetXmlRpcType(o);
        if (xType == XmlRpcType.tArray)
        {
          xtw.WriteStartElement("", "array", "");
          xtw.WriteStartElement("", "data", "");
          Array a = (Array)o;
          foreach (Object aobj in a)
          {
            //if (aobj == null)
            //  throw new XmlRpcMappingSerializeException(String.Format(
            //    "Items in array cannot be null ({0}[]).",
            //o.GetType().GetElementType()));
            Serialize(xtw, aobj, mappingAction, nestedObjs);
          }
          xtw.WriteEndElement();
          xtw.WriteEndElement();
        }
        else if (xType == XmlRpcType.tMultiDimArray)
        {
          Array mda = (Array)o;
          int[] indices = new int[mda.Rank];
          BuildArrayXml(xtw, mda, 0, indices, mappingAction, nestedObjs);
        }
        else if (xType == XmlRpcType.tBase64)
        {
          byte[] buf = (byte[])o;
          xtw.WriteStartElement("", "base64", "");
          xtw.WriteBase64(buf, 0, buf.Length);
          xtw.WriteEndElement();
        }
        else if (xType == XmlRpcType.tBoolean)
        {
          bool boolVal = (bool)o;
          if (boolVal)
            xtw.WriteElementString("boolean", "1");
          else
            xtw.WriteElementString("boolean", "0");
        }
        else if (xType == XmlRpcType.tDateTime)
        {
          DateTime dt = (DateTime)o;
          string sdt = dt.ToString("yyyyMMdd'T'HH':'mm':'ss",
          DateTimeFormatInfo.InvariantInfo);
          xtw.WriteElementString("dateTime.iso8601", sdt);
        }
        else if (xType == XmlRpcType.tDouble)
        {
          double doubleVal = (double)o;
          xtw.WriteElementString("double", doubleVal.ToString(null,
          CultureInfo.InvariantCulture));
        }
        else if (xType == XmlRpcType.tHashtable)
        {
          xtw.WriteStartElement("", "struct", "");
          XmlRpcStruct xrs = o as XmlRpcStruct;
          foreach (object obj in xrs.Keys)
          {
            string skey = obj as string;
            xtw.WriteStartElement("", "member", "");
            xtw.WriteElementString("name", skey);
            Serialize(xtw, xrs[skey], mappingAction, nestedObjs);
            xtw.WriteEndElement();
          }
          xtw.WriteEndElement();
        }
        else if (xType == XmlRpcType.tInt32)
        {
          if (UseIntTag)
            xtw.WriteElementString("int", o.ToString());
          else
            xtw.WriteElementString("i4", o.ToString());
        }
        else if (xType == XmlRpcType.tInt64)
        {
          xtw.WriteElementString("i8", o.ToString());
        }
        else if (xType == XmlRpcType.tString)
        {
          if (UseStringTag)
          {
            xtw.WriteStartElement("string");
            xtw.WriteString((string)o);
            xtw.WriteEndElement();
          }
          else
            xtw.WriteString((string)o);
        }
        else if (xType == XmlRpcType.tStruct)
        {
          NullMappingAction structAction
            = StructMappingAction(o.GetType(), mappingAction);
          xtw.WriteStartElement("", "struct", "");
          MemberInfo[] mis = o.GetType().GetMembers();
          foreach (MemberInfo mi in mis)
          {
            if (Attribute.IsDefined(mi, typeof(NonSerializedAttribute)))
              continue;
            if (mi.MemberType == MemberTypes.Field)
            {
              FieldInfo fi = (FieldInfo)mi;
              string member = fi.Name;
              Attribute attrchk = Attribute.GetCustomAttribute(fi,
              typeof(XmlRpcMemberAttribute));
              if (attrchk != null && attrchk is XmlRpcMemberAttribute)
              {
                string mmbr = ((XmlRpcMemberAttribute)attrchk).Member;
                if (mmbr != "")
                  member = mmbr;
              }
              if (fi.GetValue(o) == null)
              {
                NullMappingAction memberAction = MemberNullMappingAction(o.GetType(),
                  fi.Name, structAction);
                if (memberAction == NullMappingAction.Ignore)
                  continue;
                else if (memberAction == NullMappingAction.Error)
                  throw new XmlRpcMappingSerializeException(@"Member """ + member +
                    @""" of struct """ + o.GetType().Name + @""" cannot be null.");
              }
              xtw.WriteStartElement("", "member", "");
              xtw.WriteElementString("name", member);
              Serialize(xtw, fi.GetValue(o), mappingAction, nestedObjs);
              xtw.WriteEndElement();
            }
            else if (mi.MemberType == MemberTypes.Property)
            {
              PropertyInfo pi = (PropertyInfo)mi;
              string member = pi.Name;
              Attribute attrchk = Attribute.GetCustomAttribute(pi,
              typeof(XmlRpcMemberAttribute));
              if (attrchk != null && attrchk is XmlRpcMemberAttribute)
              {
                string mmbr = ((XmlRpcMemberAttribute)attrchk).Member;
                if (mmbr != "")
                  member = mmbr;
              }
              if (pi.GetValue(o, null) == null)
              {
                NullMappingAction memberAction = MemberNullMappingAction(o.GetType(),
                  pi.Name, structAction);
                if (memberAction == NullMappingAction.Ignore)
                  continue;
                else if (memberAction == NullMappingAction.Error)
                  throw new XmlRpcMappingSerializeException(@"Member """ + member +
                    @""" of struct """ + o.GetType().Name + @""" cannot be null.");
              }
              xtw.WriteStartElement("", "member", "");
              xtw.WriteElementString("name", member);
              Serialize(xtw, pi.GetValue(o, null), mappingAction, nestedObjs);
              xtw.WriteEndElement();
            }
          }
          xtw.WriteEndElement();
        }
        else if (xType == XmlRpcType.tVoid)
          xtw.WriteElementString("string", "");
        else if (xType == XmlRpcType.tNil)
        {
          xtw.WriteStartElement("nil");
          xtw.WriteEndElement();
        }
        else
          throw new XmlRpcUnsupportedTypeException(o.GetType());
        xtw.WriteEndElement();
      }
      catch (System.NullReferenceException)
      {
        throw new XmlRpcNullReferenceException("Attempt to serialize data "
          + "containing null reference");
      }
      finally
      {
        nestedObjs.RemoveAt(nestedObjs.Count - 1);
      }
    }

    void BuildArrayXml(
      XmlWriter xtw,
      Array ary,
      int CurRank,
      int[] indices,
      NullMappingAction mappingAction,
      List<object> nestedObjs)
    {
      xtw.WriteStartElement("", "array", "");
      xtw.WriteStartElement("", "data", "");
      if (CurRank < (ary.Rank - 1))
      {
        for (int i = 0; i < ary.GetLength(CurRank); i++)
        {
          indices[CurRank] = i;
          xtw.WriteStartElement("", "value", "");
          BuildArrayXml(xtw, ary, CurRank + 1, indices, mappingAction, nestedObjs);
          xtw.WriteEndElement();
        }
      }
      else
      {
        for (int i = 0; i < ary.GetLength(CurRank); i++)
        {
          indices[CurRank] = i;
          Serialize(xtw, ary.GetValue(indices), mappingAction, nestedObjs);
        }
      }
      xtw.WriteEndElement();
      xtw.WriteEndElement();
    }

    struct FaultStruct
    {
      public int faultCode;
      public string faultString;
    }

    struct FaultStructStringCode
    {
      public string faultCode;
      public string faultString;
    }

    public void SerializeFaultResponse(
      Stream stm,
      XmlRpcFaultException faultEx)
    {
      FaultStruct fs;
      fs.faultCode = faultEx.FaultCode;
      fs.faultString = faultEx.FaultString;
      XmlWriter xtw = XmlRpcXmlWriter.Create(stm, XmlEncoding, UseIndentation, Indentation);
      xtw.WriteStartDocument();
      xtw.WriteStartElement("", "methodResponse", "");
      xtw.WriteStartElement("", "fault", "");
      Serialize(xtw, fs, NullMappingAction.Error);
      xtw.WriteEndElement();
      xtw.WriteEndElement();
      xtw.Flush();
    }

    protected XmlWriterSettings ConfigureXmlFormat()
    {
      if (m_bUseIndentation)
      {
        return new XmlWriterSettings
        {
          Indent = true,
          IndentChars = new string(' ', m_indentation),
          Encoding = m_encoding,
          NewLineHandling = NewLineHandling.None,
        };
      }
      else
      {
        return new XmlWriterSettings
        {
          Indent = false,
          Encoding = m_encoding
        };
      }
    }

    string StackDump(ParseStack parseStack)
    {
      StringBuilder sb = new StringBuilder();
      foreach (string elem in parseStack)
      {
        sb.Insert(0, elem);
        sb.Insert(0, " : ");
      }
      sb.Insert(0, parseStack.ParseType);
      sb.Insert(0, "[");
      sb.Append("]");
      return sb.ToString();
    }

    public bool IsStructParamsMethod(MethodInfo mi)
    {
      if (mi == null)
        return false;
      bool ret = false;
      Attribute attr = Attribute.GetCustomAttribute(mi,
        typeof(XmlRpcMethodAttribute));
      if (attr != null)
      {
        XmlRpcMethodAttribute mattr = (XmlRpcMethodAttribute)attr;
        ret = mattr.StructParams;
      }
      return ret;
    }

    NullMappingAction StructMappingAction(
      Type type,
      NullMappingAction currentAction)
    {
      // if struct member has mapping action attribute, override the current
      // mapping action else just return the current action
      if (type == null)
        return currentAction;
      Attribute attr = Attribute.GetCustomAttribute(type, typeof(XmlRpcNullMappingAttribute));
      if (attr != null)
        return ((XmlRpcNullMappingAttribute)attr).Action;
      attr = Attribute.GetCustomAttribute(type, typeof(XmlRpcMissingMappingAttribute));
      if (attr != null)
        return MapToNullMappingAction(((XmlRpcMissingMappingAttribute)attr).Action);
      return currentAction;
    }

    NullMappingAction MemberNullMappingAction(
      Type type,
      string memberName,
      NullMappingAction currentAction)
    {
      // if struct member has mapping action attribute, override the current
      // mapping action else just return the current action
      if (type == null)
        return currentAction;
      Attribute attr = null;
      MemberInfo[] mis = type.GetMember(memberName);
      if (mis.Length > 0 && mis != null)
      {
        attr = Attribute.GetCustomAttribute(mis[0], typeof(XmlRpcNullMappingAttribute));
        if (attr != null)
          return ((XmlRpcNullMappingAttribute)attr).Action;
        // check for missing mapping attribute for backwards compatibility
        attr = Attribute.GetCustomAttribute(mis[0], typeof(XmlRpcMissingMappingAttribute));
        if (attr != null)
          return MapToNullMappingAction(((XmlRpcMissingMappingAttribute)attr).Action);
      }
      return currentAction;
    }

    private static NullMappingAction MapToNullMappingAction(MappingAction missingMappingAction)
    {
      switch (missingMappingAction)
      {
        case MappingAction.Error:
          return NullMappingAction.Error;
        case MappingAction.Ignore:
          return NullMappingAction.Ignore;
        default:
          throw new XmlRpcException("Unexpected missingMappingAction in MapToNullMappingAction");
      }
    }
  }
}
