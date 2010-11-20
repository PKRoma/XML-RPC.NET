/* 
XML-RPC.NET library
Copyright (c) 2001-2006, Charles Cook <charlescook@cookcomputing.com>

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

    public void SerializeRequest(Stream stm, XmlRpcRequest request) 
    {
      XmlTextWriter xtw = new XmlTextWriter(stm, m_encoding);
      ConfigureXmlFormat(xtw);
      xtw.WriteStartDocument();
      xtw.WriteStartElement("", "methodCall", "");
    {
      // TODO: use global action setting
      MappingAction mappingAction = MappingAction.Error; 
      if (request.xmlRpcMethod == null)
        xtw.WriteElementString("methodName", request.method);
      else
        xtw.WriteElementString("methodName", request.xmlRpcMethod);
      if (request.args.Length > 0 || UseEmptyParamsTag)
      {
        xtw.WriteStartElement("", "params", "");
        try
        {
          if (!IsStructParamsMethod(request.mi))
            SerializeParams(xtw, request, mappingAction);
          else
            SerializeStructParams(xtw, request, mappingAction);
        }
        catch (XmlRpcUnsupportedTypeException ex)
        {
          throw new XmlRpcUnsupportedTypeException(ex.UnsupportedType,
            String.Format("A parameter is of, or contains an instance of, "
            + "type {0} which cannot be mapped to an XML-RPC type",
            ex.UnsupportedType));
        }
        xtw.WriteEndElement();
      }
    }
      xtw.WriteEndElement();
      xtw.Flush();
    }

    void SerializeParams(XmlTextWriter xtw, XmlRpcRequest request,
      MappingAction mappingAction)
    {
      ParameterInfo[] pis = null;
      if (request.mi != null)
      {
        pis = request.mi.GetParameters();
      }
      for (int i = 0; i < request.args.Length; i++)
      {
        if (pis != null)
        {
          if (i >= pis.Length)
            throw new XmlRpcInvalidParametersException("Number of request "
              + "parameters greater than number of proxy method parameters.");
          if (i == pis.Length - 1 
            && Attribute.IsDefined(pis[i], typeof(ParamArrayAttribute)))
          {
            Array ary = (Array)request.args[i];
            foreach (object o in ary)
            {
              if (o == null)
                throw new XmlRpcNullParameterException(
                  "Null parameter in params array");
              xtw.WriteStartElement("", "param", "");
              Serialize(xtw, o, mappingAction);
              xtw.WriteEndElement();
            }
            break;
          }
        }
        if (request.args[i] == null)
        {
          throw new XmlRpcNullParameterException(String.Format(
            "Null method parameter #{0}", i + 1));
        }
        xtw.WriteStartElement("", "param", "");
        Serialize(xtw, request.args[i], mappingAction);
        xtw.WriteEndElement();
      }
    }

    void SerializeStructParams(XmlTextWriter xtw, XmlRpcRequest request,
      MappingAction mappingAction)
    {
      ParameterInfo[] pis = request.mi.GetParameters();
      if (request.args.Length > pis.Length)
        throw new XmlRpcInvalidParametersException("Number of request "
          + "parameters greater than number of proxy method parameters.");
      if (Attribute.IsDefined(pis[request.args.Length - 1],
        typeof(ParamArrayAttribute)))
      {
        throw new XmlRpcInvalidParametersException("params parameter cannot "
          + "be used with StructParams.");
      }
      xtw.WriteStartElement("", "param", "");
      xtw.WriteStartElement("", "value", "");
      xtw.WriteStartElement("", "struct", "");
      for (int i = 0; i < request.args.Length; i++)
      {
        if (request.args[i] == null)
        {
          throw new XmlRpcNullParameterException(String.Format(
            "Null method parameter #{0}", i + 1));
        }
        xtw.WriteStartElement("", "member", "");
        xtw.WriteElementString("name", pis[i].Name);
        Serialize(xtw, request.args[i], mappingAction);
        xtw.WriteEndElement();
      }
      xtw.WriteEndElement();
      xtw.WriteEndElement();
      xtw.WriteEndElement();
    }

#if (!COMPACT_FRAMEWORK)
    public void SerializeResponse(Stream stm, XmlRpcResponse response)
    {
      Object ret = response.retVal;
      if (ret is XmlRpcFaultException)
      {
        SerializeFaultResponse(stm, (XmlRpcFaultException)ret);
        return;
      }

      XmlTextWriter xtw = new XmlTextWriter(stm, m_encoding);
      ConfigureXmlFormat(xtw);
      xtw.WriteStartDocument();
      xtw.WriteStartElement("", "methodResponse", "");
      xtw.WriteStartElement("", "params", "");
      // "void" methods actually return an empty string value
      if (ret == null)
      {
        ret = "";
      }
      xtw.WriteStartElement("", "param", "");
      // TODO: use global action setting
      MappingAction mappingAction = MappingAction.Error;
      try
      {
        Serialize(xtw, ret, mappingAction);
      }
      catch (XmlRpcUnsupportedTypeException ex)
      {
        throw new XmlRpcInvalidReturnType(string.Format(
          "Return value is of, or contains an instance of, type {0} which "
          + "cannot be mapped to an XML-RPC type", ex.UnsupportedType));
      }
      xtw.WriteEndElement();
      xtw.WriteEndElement();
      xtw.WriteEndElement();
      xtw.Flush();
    }
#endif

    //#if (DEBUG)
    public
      //#endif
    void Serialize(
      XmlTextWriter xtw,
      Object o,
      MappingAction mappingAction)
    {
      Serialize(xtw, o, mappingAction, new ArrayList(16));
    }

    //#if (DEBUG)
    public
      //#endif
    void Serialize(
      XmlTextWriter xtw,
      Object o,
      MappingAction mappingAction,
      ArrayList nestedObjs)
    {
      if (nestedObjs.Contains(o))
        throw new XmlRpcUnsupportedTypeException(nestedObjs[0].GetType(),
          "Cannot serialize recursive data structure");
      nestedObjs.Add(o);
      try
      {
        xtw.WriteStartElement("", "value", "");
        XmlRpcType xType = XmlRpcServiceInfo.GetXmlRpcType(o.GetType());
        if (xType == XmlRpcType.tArray)
        {
          xtw.WriteStartElement("", "array", "");
          xtw.WriteStartElement("", "data", "");
          Array a = (Array)o;
          foreach (Object aobj in a)
          {
            if (aobj == null)
              throw new XmlRpcMappingSerializeException(String.Format(
                "Items in array cannot be null ({0}[]).",
            o.GetType().GetElementType()));
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
          bool boolVal;
          if (o is bool)
            boolVal = (bool)o;
          else
            boolVal = (bool)(XmlRpcBoolean)o;
          if (boolVal)
            xtw.WriteElementString("boolean", "1");
          else
            xtw.WriteElementString("boolean", "0");
        }
        else if (xType == XmlRpcType.tDateTime)
        {
          DateTime dt;
          if (o is DateTime)
            dt = (DateTime)o;
          else
            dt = (XmlRpcDateTime)o;
          string sdt = dt.ToString("yyyyMMdd'T'HH':'mm':'ss",
          DateTimeFormatInfo.InvariantInfo);
          xtw.WriteElementString("dateTime.iso8601", sdt);
        }
        else if (xType == XmlRpcType.tDouble)
        {
          double doubleVal;
          if (o is double)
            doubleVal = (double)o;
          else
            doubleVal = (XmlRpcDouble)o;
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
            xtw.WriteElementString("string", (string)o);
          else
            xtw.WriteString((string)o);
        }
        else if (xType == XmlRpcType.tStruct)
        {
          MappingAction structAction
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
                MappingAction memberAction = MemberMappingAction(o.GetType(),
                  fi.Name, structAction);
                if (memberAction == MappingAction.Ignore)
                  continue;
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
                MappingAction memberAction = MemberMappingAction(o.GetType(),
                  pi.Name, structAction);
                if (memberAction == MappingAction.Ignore)
                  continue;
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
      XmlTextWriter xtw,
      Array ary,
      int CurRank,
      int[] indices,
      MappingAction mappingAction,
      ArrayList nestedObjs)
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

      XmlTextWriter xtw = new XmlTextWriter(stm, m_encoding);
      ConfigureXmlFormat(xtw);
      xtw.WriteStartDocument();
      xtw.WriteStartElement("", "methodResponse", "");
      xtw.WriteStartElement("", "fault", "");
      Serialize(xtw, fs, MappingAction.Error);
      xtw.WriteEndElement();
      xtw.WriteEndElement();
      xtw.Flush();
    }

    void ConfigureXmlFormat(
      XmlTextWriter xtw)
    {
      if (m_bUseIndentation)
      {
        xtw.Formatting = Formatting.Indented;
        xtw.Indentation = m_indentation;
      }
      else
      {
        xtw.Formatting = Formatting.None;
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

    XmlNode SelectSingleNode(XmlNode node, string name)
    {
#if (COMPACT_FRAMEWORK)
      foreach (XmlNode selnode in node.ChildNodes)
      {
        // For "*" element else return null
        if ((name == "*") && !(selnode.Name.StartsWith("#")))
          return selnode;
        if (selnode.Name == name)
          return selnode;
      }
      return null;
#else
      return node.SelectSingleNode(name);
#endif
    }

    XmlNode[] SelectNodes(XmlNode node, string name)
    {
      ArrayList list = new ArrayList();
      foreach (XmlNode selnode in node.ChildNodes)
      {
        if (selnode.Name == name)
          list.Add(selnode);
      }
      return (XmlNode[])list.ToArray(typeof(XmlNode));
    }

    XmlNode SelectValueNode(XmlNode valueNode)
    {
      // an XML-RPC value is either held as the child node of a <value> element
      // or is just the text of the value node as an implicit string value
      XmlNode vvNode = SelectSingleNode(valueNode, "*");
      if (vvNode == null)
        vvNode = valueNode.FirstChild;
      return vvNode;
    }

    void SelectTwoNodes(XmlNode node, string name1, out XmlNode node1,
      out bool dup1, string name2, out XmlNode node2, out bool dup2)
    {
      node1 = node2 = null;
      dup1 = dup2 = false;
      foreach (XmlNode selnode in node.ChildNodes)
      {
        if (selnode.Name == name1)
        {
          if (node1 == null)
            node1 = selnode;
          else
            dup1 = true;
        }
        else if (selnode.Name == name2)
        {
          if (node2 == null)
            node2 = selnode;
          else
            dup2 = true;
        }
      }
    }

    // TODO: following to return Array?
    object CreateArrayInstance(Type type, object[] args)
    {
#if (!COMPACT_FRAMEWORK)
      return Activator.CreateInstance(type, args);
#else
		Object Arr = Array.CreateInstance(type.GetElementType(), (int)args[0]);
		return Arr;
#endif
    }

    bool IsStructParamsMethod(MethodInfo mi)
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

    MappingAction StructMappingAction(
  Type type,
  MappingAction currentAction)
    {
      // if struct member has mapping action attribute, override the current
      // mapping action else just return the current action
      if (type == null)
        return currentAction;
      Attribute attr = Attribute.GetCustomAttribute(type,
        typeof(XmlRpcMissingMappingAttribute));
      if (attr != null)
        return ((XmlRpcMissingMappingAttribute)attr).Action;
      return currentAction;
    }

    MappingAction MemberMappingAction(
      Type type,
      string memberName,
      MappingAction currentAction)
    {
      // if struct member has mapping action attribute, override the current
      // mapping action else just return the current action
      if (type == null)
        return currentAction;
      Attribute attr = null;
      FieldInfo fi = type.GetField(memberName);
      if (fi != null)
        attr = Attribute.GetCustomAttribute(fi,
          typeof(XmlRpcMissingMappingAttribute));
      else
      {
        PropertyInfo pi = type.GetProperty(memberName);
        attr = Attribute.GetCustomAttribute(pi,
          typeof(XmlRpcMissingMappingAttribute));
      }
      if (attr != null)
        return ((XmlRpcMissingMappingAttribute)attr).Action;
      return currentAction;
    }
  }


}
