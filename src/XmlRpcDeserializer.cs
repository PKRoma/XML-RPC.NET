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
  using System.Diagnostics;
  using System.Collections.Generic;

  struct Fault
  {
    public int faultCode;
    public string faultString;
  }

  public class XmlRpcDeserializer
  {
    public XmlRpcNonStandard NonStandard
    {
      get { return m_nonStandard; }
      set { m_nonStandard = value; }
    }
    XmlRpcNonStandard m_nonStandard = XmlRpcNonStandard.None;

    // private properties
    bool AllowInvalidHTTPContent
    {
      get { return (m_nonStandard & XmlRpcNonStandard.AllowInvalidHTTPContent) != 0; }
    }

    bool AllowNonStandardDateTime
    {
      get { return (m_nonStandard & XmlRpcNonStandard.AllowNonStandardDateTime) != 0; }
    }

    bool AllowStringFaultCode
    {
      get { return (m_nonStandard & XmlRpcNonStandard.AllowStringFaultCode) != 0; }
    }

    bool IgnoreDuplicateMembers
    {
      get { return (m_nonStandard & XmlRpcNonStandard.IgnoreDuplicateMembers) != 0; }
    }

    bool MapEmptyDateTimeToMinValue
    {
      get { return (m_nonStandard & XmlRpcNonStandard.MapEmptyDateTimeToMinValue) != 0; }
    }

    bool MapZerosDateTimeToMinValue
    {
      get { return (m_nonStandard & XmlRpcNonStandard.MapZerosDateTimeToMinValue) != 0; }
    }

#if (!COMPACT_FRAMEWORK)
    public XmlRpcRequest DeserializeRequest(Stream stm, Type svcType)
    {
      if (stm == null)
        throw new ArgumentNullException("stm",
          "XmlRpcSerializer.DeserializeRequest");
      XmlTextReader rdr;
      try
      {
        rdr = new XmlTextReader(stm);
        rdr.ProhibitDtd = true;
        rdr.WhitespaceHandling = WhitespaceHandling.All;
      }
      catch (Exception ex)
      {
        throw new XmlRpcIllFormedXmlException(
          "Request from client does not contain valid XML.", ex);
      }
      return DeserializeRequest(rdr, svcType);
    }

    public XmlRpcRequest DeserializeRequest(TextReader txtrdr, Type svcType)
    {
      if (txtrdr == null)
        throw new ArgumentNullException("txtrdr",
          "XmlRpcSerializer.DeserializeRequest");
      XmlTextReader xmlRdr = new XmlTextReader(txtrdr);
      xmlRdr.ProhibitDtd = true;
      return DeserializeRequest(xmlRdr, svcType);
    }

    public XmlRpcRequest DeserializeRequest(XmlReader rdr, Type svcType)
    {
      try
      {
        XmlRpcRequest request = new XmlRpcRequest();
        rdr.MoveToContent();
        if (rdr.Name != "methodCall")
          throw new XmlRpcInvalidXmlRpcException(
            "Request XML not valid XML-RPC - missing methodCall element.");

        bool ok = MoveToChild(rdr, "methodName");
        if (!ok)
          throw new XmlRpcInvalidXmlRpcException(
            "Request XML not valid XML-RPC - missing methodName element.");
        string methodName = ReadContentAsString(rdr);
        if (methodName == "")
          throw new XmlRpcInvalidXmlRpcException(
            "Request XML not valid XML-RPC - empty methodName.");
        request.method = methodName;
        ok = MoveToEndElement(rdr, "methodName", 0);

        request.mi = null;
        ParameterInfo[] pis = null;
        if (svcType != null)
        {
          // retrieve info for the method which handles this XML-RPC method
          XmlRpcServiceInfo svcInfo
            = XmlRpcServiceInfo.CreateServiceInfo(svcType);
          request.mi = svcInfo.GetMethodInfo(request.method);
          // if a service type has been specified and we cannot find the requested
          // method then we must throw an exception
          if (request.mi == null)
          {
            string msg = String.Format("unsupported method called: {0}",
                                        request.method);
            throw new XmlRpcUnsupportedMethodException(msg);
          }
          // method must be marked with XmlRpcMethod attribute
          Attribute attr = Attribute.GetCustomAttribute(request.mi,
            typeof(XmlRpcMethodAttribute));
          if (attr == null)
          {
            throw new XmlRpcMethodAttributeException(
              "Method must be marked with the XmlRpcMethod attribute.");
          }
          pis = request.mi.GetParameters();
        }

        ok = rdr.ReadToNextSibling("params");
        if (!ok)
        {
          if (svcType != null)
          {
            if (pis.Length == 0)
            {
              request.args = new object[0];
              return request;
            }
            else
            {
              throw new XmlRpcInvalidParametersException(
                "Method takes parameters and params element is missing.");
            }
          }
          else
          {
            request.args = new object[0];
            return request;
          }
        }

        int paramsPos = pis != null ? GetParamsPos(pis) : -1;
        Type paramsType = null;
        if (paramsPos != -1)
          paramsType = pis[paramsPos].ParameterType.GetElementType();
        int minParamCount = pis == null ? int.MaxValue : (paramsPos == -1 ? pis.Length : paramsPos);
        ParseStack parseStack = new ParseStack("request");
        MappingAction mappingAction = MappingAction.Error;
        var objs = new List<object>();
        var paramsObjs = new List<object>();
        int paramCount = 0;
        bool gotParam = MoveToChild(rdr, "param");
        while (gotParam)
        {
          paramCount++;
          if (svcType != null && paramCount > minParamCount && paramsPos == -1)
            throw new XmlRpcInvalidParametersException(
              "Request contains too many param elements based on method signature.");
          ok = MoveToChild(rdr, "value");
          if (!ok)
            throw new XmlRpcInvalidXmlRpcException("Missing value element.");
          if (paramCount <= minParamCount)
          {
            if (svcType != null)
            {
              parseStack.Push(String.Format("parameter {0}", paramCount));
              // TODO: why following commented out?
              //          parseStack.Push(String.Format("parameter {0} mapped to type {1}", 
              //            i, pis[i].ParameterType.Name));
              var obj = ParseValueElement(rdr,
                pis[paramCount - 1].ParameterType, parseStack, mappingAction);
              objs.Add(obj);
            }
            else
            {
              parseStack.Push(String.Format("parameter {0}", paramCount));
              var obj = ParseValueElement(rdr, null, parseStack, mappingAction);
              objs.Add(obj);
            }
            parseStack.Pop();
          }
          else
          {
            parseStack.Push(String.Format("parameter {0}", paramCount + 1));
            var paramsObj = ParseValueElement(rdr, paramsType, parseStack, mappingAction);
            paramsObjs.Add(paramsObj);
            parseStack.Pop();
          }
          ok = MoveToEndElement(rdr, "param", 0);
          gotParam = rdr.ReadToNextSibling("param");
        }

        if (svcType != null && paramCount < minParamCount)
          throw new XmlRpcInvalidParametersException(
            "Request contains too few param elements based on method signature.");

        if (paramsPos != -1)
        {
          Object[] args = new Object[1];
          args[0] = paramCount - minParamCount;
          Array varargs = (Array)CreateArrayInstance(pis[paramsPos].ParameterType,
            args);
          for (int i = 0; i < paramsObjs.Count; i++)
            varargs.SetValue(paramsObjs[i], i);
          objs.Add(varargs);
        }
        request.args = objs.ToArray();
        return request;
      }
      catch (XmlException ex)
      {
        throw new XmlRpcIllFormedXmlException("Request contains invalid XML", ex);
      }
    }

    int GetParamsPos(ParameterInfo[] pis)
    {
      if (pis.Length == 0)
        return -1;
      if (Attribute.IsDefined(pis[pis.Length - 1], typeof(ParamArrayAttribute)))
      {
        return pis.Length - 1;
      }
      else
        return -1;
    }
#endif

    public XmlRpcResponse DeserializeResponse(Stream stm, Type svcType)
    {
      if (stm == null)
        throw new ArgumentNullException("stm",
          "XmlRpcSerializer.DeserializeResponse");
      if (AllowInvalidHTTPContent)
      {
        Stream newStm = new MemoryStream();
        Util.CopyStream(stm, newStm);
        stm = newStm;
        stm.Position = 0;
        while (true)
        {
          // for now just strip off any leading CR-LF characters
          int byt = stm.ReadByte();
          if (byt == -1)
            throw new XmlRpcIllFormedXmlException(
              "Response from server does not contain valid XML.");
          if (byt != 0x0d && byt != 0x0a && byt != ' ' && byt != '\t')
          {
            stm.Position = stm.Position - 1;
            break;
          }
        }
      }
      XmlTextReader rdr = new XmlTextReader(stm);
#if (!COMPACT_FRAMEWORK)
      rdr.ProhibitDtd = true;
#endif
      rdr.WhitespaceHandling = WhitespaceHandling.All;
      return DeserializeResponse(rdr, svcType);
    }

    public XmlRpcResponse DeserializeResponse(TextReader txtrdr, Type svcType)
    {
      if (txtrdr == null)
        throw new ArgumentNullException("txtrdr",
          "XmlRpcSerializer.DeserializeResponse");
      XmlTextReader xmlRdr = new XmlTextReader(txtrdr);
#if (!COMPACT_FRAMEWORK)
      xmlRdr.ProhibitDtd = true;
#endif
      return DeserializeResponse(xmlRdr, svcType);
    }

    public XmlRpcResponse DeserializeResponse(XmlReader rdr, Type returnType)
    {
      try
      {
        rdr.MoveToContent();
        Debug.Assert(rdr.Name == "methodResponse");
        MoveToChild(rdr, "params", "fault");
        if (rdr.Name == "fault")
        {
          DeserializeFault();
        }
        if (rdr.IsEmptyElement)
          return new XmlRpcResponse { retVal = null }; ;
        if (!MoveToChild(rdr, "param"))
          throw new XmlRpcInvalidXmlRpcException("Missing param element in response");
        if (!MoveToChild(rdr, "value"))
          throw new XmlRpcInvalidXmlRpcException("Missing value element in response");
        if (returnType == typeof(void))
          return new XmlRpcResponse { retVal = null };
        object retObj = ParseValueElement(rdr, returnType, new ParseStack("response"),
          MappingAction.Error);
        var response = new XmlRpcResponse { retVal = retObj };
        return response;
      }
      catch (XmlException ex)
      {
        throw new XmlRpcIllFormedXmlException("Response contains invalid XML", ex);
      }
    }

    private void DeserializeFault()
    {
      ParseStack faultStack = new ParseStack("fault response");
      // TODO: use global action setting
      MappingAction mappingAction = MappingAction.Error;
      XmlRpcFaultException faultEx = ParseFault(null, faultStack, // TODO: fix
        mappingAction);
      throw faultEx;
    }

    public Object ParseValueElement(
      XmlReader rdr,
      Type valType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      // if suppplied type is System.Object then ignore it because
      // if doesn't provide any useful information (parsing methods
      // expect null in this case)
      if (valType != null && valType.BaseType == null)
        valType = null;
      object ret = "";
      if (rdr.IsEmptyElement)
      {
        CheckImplictString(valType, parseStack);
        return "";
      }
      rdr.Read();
      if (rdr.NodeType == XmlNodeType.Text)
      {
        CheckImplictString(valType, parseStack);
        ret = rdr.Value;
        MoveToEndElement(rdr, "value", 0);
      }
      else
      {
        if (rdr.NodeType == XmlNodeType.Whitespace)
        {
          ret = rdr.Value;
          rdr.Read();
        }
        if (rdr.NodeType == XmlNodeType.EndElement)
          CheckImplictString(valType, parseStack);
        else
        {
          Type parsedType;
          Type parsedArrayType;
          ret = ParseValue(rdr, valType, parseStack, mappingAction,
            out parsedType, out parsedArrayType);
        }
        MoveToEndElement(rdr, "value", 0);
      }
      Debug.Assert(rdr.NodeType == XmlNodeType.EndElement && rdr.Name == "value");
      return ret;
    }

    private void CheckImplictString(Type valType, ParseStack parseStack)
    {
      if (valType != null && valType != typeof(string))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains implicit string value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(valType)
          + " expected " + StackDump(parseStack));
      }
    }

    object ParseValue(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType,
      out Type ParsedArrayType)
    {
      ParsedType = null;
      ParsedArrayType = null;
      object retObj = null;
      if (rdr.Name == "array")
        retObj = ParseArray(rdr, valType, parseStack, mappingAction,
          out ParsedType, out ParsedArrayType);
      else if (rdr.Name == "base64")
        retObj = ParseBase64(rdr, valType, parseStack, mappingAction,
          out ParsedType, out ParsedArrayType);
      else if (rdr.Name == "struct")
      {
        // if we don't know the expected struct type then we must
        // parse the XML-RPC struct as an instance of XmlRpcStruct
        if (valType != null && valType != typeof(XmlRpcStruct)
          && !valType.IsSubclassOf(typeof(XmlRpcStruct)))
        {
          retObj = ParseStruct(rdr, valType, parseStack, mappingAction,
            out ParsedType, out ParsedArrayType);
        }
        else
        {
          if (valType == null || valType == typeof(object))
            valType = typeof(XmlRpcStruct);
          // TODO: do we need to validate type here?
          retObj = ParseHashtable(rdr, valType, parseStack, mappingAction,
            out ParsedType, out ParsedArrayType);
        }
      }
      else if (rdr.Name == "i4"  // integer has two representations in XML-RPC spec
        || rdr.Name == "int")
      {
        retObj = ParseInt(rdr, valType, parseStack, mappingAction, 
          out ParsedType, out ParsedArrayType);
      }
      else if (rdr.Name == "i8")
      {
        retObj = ParseLong(rdr, valType, parseStack, mappingAction,
          out ParsedType, out ParsedArrayType);
      }
      else if (rdr.Name == "string")
      {
        retObj = ParseString(rdr, valType, parseStack, mappingAction,
          out ParsedType, out ParsedArrayType);
      }
      else if (rdr.Name == "boolean")
      {
        retObj = ParseBoolean(rdr, valType, parseStack, mappingAction,
          out ParsedType, out ParsedArrayType);
      }
      else if (rdr.Name == "double")
      {
        retObj = ParseDouble(rdr, valType, parseStack, mappingAction,
          out ParsedType, out ParsedArrayType);
      }
      else if (rdr.Name == "dateTime.iso8601")
      {
        retObj = ParseDateTime(rdr, valType, parseStack, mappingAction,
          out ParsedType, out ParsedArrayType);
      }
      else
        throw new XmlRpcInvalidXmlRpcException(
          "Invalid value element: <" + rdr.Name + ">");
      return retObj;
    }

    private object ParseString(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      CheckExpectedType(valType, typeof(string), parseStack);
      ParsedType = typeof(string);
      ParsedArrayType = typeof(string[]);
      return OnStack("string", parseStack, delegate() 
        { string ret = rdr.ReadElementContentAsString(); MoveToContent(rdr); return ret; });
    }

    private object ParseInt(XmlReader rdr, Type valType, ParseStack parseStack, 
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      CheckExpectedType(valType, typeof(int), parseStack);
      ParsedType = typeof(int);
      ParsedArrayType = typeof(int[]);
      return OnStack("integer", parseStack, delegate()
      {
        string str = rdr.ReadElementContentAsString();
        try
        {
          int ret = Convert.ToInt32(str);
          return ret;
        }
        catch (FormatException fex)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
          + " contains invalid int element (overflow) " + StackDump(parseStack), fex);
        }
        catch (Exception ex)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
          + " contains invalid int element " + StackDump(parseStack), ex);
        }
      });
    }

    private object ParseLong(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      CheckExpectedType(valType, typeof(long), parseStack);
      ParsedType = typeof(int);
      ParsedArrayType = typeof(int[]);
      return OnStack("i8", parseStack, delegate() 
      {
        string str = rdr.ReadElementContentAsString();
        try
        {
          long ret = Convert.ToInt64(str);
          return ret;
        }
        catch (FormatException fex)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
          + " contains invalid int element (overflow) " + StackDump(parseStack), fex);
        }
        catch (Exception ex)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
          + " contains invalid i8 element " + StackDump(parseStack), ex);
        }
      });
    }

    private object ParseBoolean(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      CheckExpectedType(valType, typeof(bool), parseStack);
      ParsedType = typeof(int);
      ParsedArrayType = typeof(int[]);
      return OnStack("boolean", parseStack, delegate() 
        { return rdr.ReadElementContentAsBoolean(); });
    }

    private object ParseDouble(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      CheckExpectedType(valType, typeof(double), parseStack);
      ParsedType = typeof(int);
      ParsedArrayType = typeof(int[]);
      return OnStack("double", parseStack, delegate() 
        { return rdr.ReadElementContentAsDouble(); });
    }

    private object ParseDateTime(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      CheckExpectedType(valType, typeof(DateTime), parseStack);
      ParsedType = typeof(int);
      ParsedArrayType = typeof(int[]);
      return OnStack("dateTime", parseStack, delegate()
      {
        DateTime retVal;
        string s = rdr.ReadElementContentAsString();
        if (!DateTime8601.TryParseDateTime8601(s, out retVal))
        {
          if (MapZerosDateTimeToMinValue && s.StartsWith("0000")
            && (s == "00000000T00:00:00" || s == "0000-00-00T00:00:00Z"
            || s == "00000000T00:00:00Z" || s == "0000-00-00T00:00:00"))
            retVal = DateTime.MinValue;
          else
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains invalid dateTime value "
              + StackDump(parseStack));
        }
        return retVal;
      });
    }

    private object ParseBase64(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      CheckExpectedType(valType, typeof(byte[]), parseStack);
      ParsedType = typeof(int);
      ParsedArrayType = typeof(int[]);
      return OnStack("base64", parseStack, delegate() 
      {
        string s = rdr.ReadElementContentAsString();
        try
        {
          byte[] ret = Convert.FromBase64String(s);
          return ret;
        }
        catch (Exception)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid base64 value "
            + StackDump(parseStack));
        }
      });
    }

    private object ParseArray(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      ParsedType = null;
      ParsedArrayType = null;
      // required type must be an array
      if (valType != null
        && !(valType.IsArray == true
            || valType == typeof(Array)
            || valType == typeof(object)))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains array value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(valType)
          + " expected " + StackDump(parseStack));
      }
      if (valType != null)
      {
        XmlRpcType xmlRpcType = XmlRpcServiceInfo.GetXmlRpcType(valType);
        if (xmlRpcType == XmlRpcType.tMultiDimArray)
        {
          parseStack.Push("array mapped to type " + valType.Name);
          Object ret = ParseMultiDimArray(rdr, valType, parseStack,
            mappingAction);
          return ret;
        }
        parseStack.Push("array mapped to type " + valType.Name);
      }
      else
        parseStack.Push("array");

      MoveToChild(rdr, "data");

      //XmlNode[] childNodes = SelectNodes(dataNode, "value");
      //int nodeCount = childNodes.Length;
      //Object[] elements = new Object[nodeCount];

      var values = new List<object>();
      Type elemType = DetermineArrayItemType(valType);



      bool bGotType = false;
      Type useType = null;

      bool gotValue = MoveToChild(rdr, "value");
      while (gotValue)
      {
        parseStack.Push(String.Format("element {0}", values.Count));
        object value = ParseValueElement(rdr, elemType, parseStack, mappingAction);
        values.Add(value);
        MoveToEndElement(rdr, "value", 0);
        gotValue = rdr.ReadToNextSibling("value");
        parseStack.Pop();
      }


      foreach (object value in values)
      {
        //if (bGotType == false)
        //{
        //  useType = parsedArrayType;
        //  bGotType = true;
        //}
        //else
        //{
        //  if (useType != parsedArrayType)
        //    useType = null;
        //}
      }

      Object[] args = new Object[1];
      args[0] = values.Count;
      Object retObj = null;
      if (valType != null
        && valType != typeof(Array)
        && valType != typeof(object))
      {
        retObj = CreateArrayInstance(valType, args);
      }
      else
      {
        if (useType == null)
          retObj = CreateArrayInstance(typeof(object[]), args);
        else
          retObj = CreateArrayInstance(useType, args);
      }
      for (int j = 0; j < values.Count; j++)
      {
        ((Array)retObj).SetValue(values[j], j);
      }

      parseStack.Pop();

      return retObj;
    }

    private static Type DetermineArrayItemType(Type valType)
    {
      Type elemType = null;
      if (valType != null
        && valType != typeof(Array)
        && valType != typeof(object))
      {
#if (!COMPACT_FRAMEWORK)
        elemType = valType.GetElementType();
#else
        string[] checkMultiDim = Regex.Split(ValueType.FullName, 
          "\\[\\]$");
        // determine assembly of array element type
        Assembly asmbly = ValueType.Assembly;
        string[] asmblyName = asmbly.FullName.Split(',');
        string elemTypeName = checkMultiDim[0] + ", " + asmblyName[0]; 
        elemType = Type.GetType(elemTypeName);
#endif
      }
      else
      {
        elemType = typeof(object);
      }
      return elemType;
    }

    private object ParseMultiDimArray(XmlReader rdr, Type valType, ParseStack parseStack, MappingAction mappingAction)
    {
      throw new NotImplementedException();
    }

    private object ParseStruct(XmlReader rdr, Type valueType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      ParsedType = null;
      ParsedArrayType = null;

      if (valueType.IsPrimitive)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains struct value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(valueType)
          + " expected " + StackDump(parseStack));
      }
#if !FX1_0
      if (valueType.IsGenericType
        && valueType.GetGenericTypeDefinition() == typeof(Nullable<>))
      {
        valueType = valueType.GetGenericArguments()[0];
      }
#endif
      object retObj;
      try
      {
        retObj = Activator.CreateInstance(valueType);
      }
      catch (Exception)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains struct value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(valueType)
          + " expected (as type " + valueType.Name + ") "
          + StackDump(parseStack));
      }
      // Note: mapping action on a struct is only applied locally - it 
      // does not override the global mapping action when members of the 
      // struct are parsed
      MappingAction localAction = mappingAction;
      if (valueType != null)
      {
        parseStack.Push("struct mapped to type " + valueType.Name);
        localAction = StructMappingAction(valueType, mappingAction);
      }
      else
      {
        parseStack.Push("struct");
      }
      // create map of field names and remove each name from it as 
      // processed so we can determine which fields are missing
      // TODO: replace HashTable with lighter collection
      Hashtable names = new Hashtable();
      CreateFieldNamesMap(valueType, names);
      int fieldCount = 0;
      List<string> rpcNames = new List<string>();
      try
      {
        bool gotMember = MoveToChild(rdr, "member");
        while (gotMember)
        {
          bool ok = MoveToChild(rdr, "name");
          if (!ok)
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains a member with missing name"
              + " " + StackDump(parseStack));
          string rpcName = ReadContentAsString(rdr);

          if (rpcNames.Contains(rpcName))
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains struct value with duplicate member "
              + rpcName
              + " " + StackDump(parseStack));
          rpcNames.Add(rpcName);

          ok = MoveToEndElement(rdr, "name", 0);

          string name = GetStructName(valueType, rpcName) ?? rpcName;
          MemberInfo mi = valueType.GetField(name);
          if (mi == null) valueType.GetProperty(name);
          //if (mi == null)
          //    continue;
          if (names.Contains(name))
              names.Remove(name);
          else
          {
              if (Attribute.IsDefined(mi, typeof(NonSerializedAttribute)))
              {
                  parseStack.Push(String.Format("member {0}", name));
                  throw new XmlRpcNonSerializedMember("Cannot map XML-RPC struct "
                  + "member onto member marked as [NonSerialized]: "
                  + " " + StackDump(parseStack));
              }
          }
          Type memberType = mi.MemberType == MemberTypes.Field
          ? (mi as FieldInfo).FieldType : (mi as PropertyInfo).PropertyType;
          string parseMsg = valueType == null
              ? String.Format("member {0}", name)
              : String.Format("member {0} mapped to type {1}", name, memberType.Name);

          ok = rdr.ReadToNextSibling("value");
          if (!ok)
              throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains a member with missing value"
              + " " + StackDump(parseStack));
          object valObj = OnStack(parseMsg, parseStack, delegate()
          {
              return ParseValueElement(rdr, memberType, parseStack, mappingAction);
          });

          ok = MoveToEndElement(rdr, "member", rdr.Depth, 
            new string[] { "name", "value"});
          if (!ok)
          {
            if (rdr.Name == "name") // TODO: fix
                throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
                + " contains member with more than one name element"
                + " " + StackDump(parseStack));
            else if (rdr.Name == "value") // TODO: fix
                throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
                + " contains member with more than one value element"
                + " " + StackDump(parseStack));
          }
          gotMember = rdr.ReadToNextSibling("member");
          if (mi.MemberType == MemberTypes.Field)
            (mi as FieldInfo).SetValue(retObj, valObj);
          else
            (mi as PropertyInfo).SetValue(retObj, valObj, null);
          fieldCount++;
        }
        MoveToEndElement(rdr, "struct", 0);

        if (localAction == MappingAction.Error && names.Count > 0)
          ReportMissingMembers(valueType, names, parseStack);
        return retObj;
      }
      finally
      {
        parseStack.Pop();
      }
    }

    private static void CreateFieldNamesMap(Type valueType, Hashtable names)
    {
        foreach (FieldInfo fi in valueType.GetFields())
        {
            if (Attribute.IsDefined(fi, typeof(NonSerializedAttribute)))
                continue;
            names.Add(fi.Name, fi.Name);
        }
        foreach (PropertyInfo pi in valueType.GetProperties())
        {
            if (Attribute.IsDefined(pi, typeof(NonSerializedAttribute)))
                continue;
            names.Add(pi.Name, pi.Name);
        }
    }

    private object ParseHashtable(XmlReader rdr, Type valType, ParseStack parseStack,
      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
    {
      ParsedType = null;
      ParsedArrayType = null;
      XmlRpcStruct retObj = new XmlRpcStruct();
      if (rdr.IsEmptyElement)
        return retObj;
      parseStack.Push("struct mapped to XmlRpcStruct");
      try
      {
        bool ok;
        bool gotMember = MoveToChild(rdr, "member");
        while (gotMember)
        {
          ok = MoveToChild(rdr, "name");
          if (!ok)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains a member with missing name"
              + " " + StackDump(parseStack));
          string rpcName = ReadContentAsString(rdr);
          if (retObj.ContainsKey(rpcName)
            && !IgnoreDuplicateMembers)
              throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
                + " contains struct value with duplicate member "
                + rpcName
                + " " + StackDump(parseStack));
          ok = MoveToEndElement(rdr, "name", 0);
          ok = rdr.ReadToNextSibling("value");
          if (!ok)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains a member with missing value"
              + " " + StackDump(parseStack));
          object value = OnStack(String.Format("member {0}", rpcName), 
            parseStack, delegate()
            {
              return ParseValueElement(rdr, null, parseStack, mappingAction);
            });
          if (!retObj.ContainsKey(rpcName))
            retObj[rpcName] = value;
          ok = MoveToEndElement(rdr, "member", rdr.Depth, new string[] { "name", "value"});
          if (!ok)
          {
            if (rdr.Name == "name") // TODO: fix
              throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
                + " contains member with more than one name element"
                + " " + StackDump(parseStack));
            else if (rdr.Name == "value") // TODO: fix
              throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
                + " contains member with more than one value element"
                + " " + StackDump(parseStack));
          }
          gotMember = rdr.ReadToNextSibling("member");
        }
        ok = MoveToEndElement(rdr, "struct", 0);
      }
      finally
      {
        parseStack.Pop();
      }
      return retObj;
    }

    private void CheckExpectedType(Type actualType, Type expectedType, ParseStack parseStack)
    {
      if (actualType != null && actualType != typeof(Object)
        && actualType != expectedType 
        && (expectedType.IsValueType 
          && actualType != typeof(Nullable<>).MakeGenericType(expectedType)))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType +
          " contains "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(expectedType)
          + "value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(actualType)
          + " expected " + StackDump(parseStack));
      }
    }

    delegate T Func<T>();

    private T OnStack<T>(string p, ParseStack parseStack, Func<T> func)
    {
      parseStack.Push(p);
      try 
      {
        return func();
      }
      finally
      {
        parseStack.Pop();
      }
    }
















    Object ParseValue(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      throw new NotImplementedException();
      Type parsedType;
      Type parsedArrayType;
      return ParseValue(node, ValueType, parseStack, mappingAction,
        out parsedType, out parsedArrayType);
    }








    //#if (DEBUG)
    public
      //#endif
    Object ParseValue(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction,
      out Type ParsedType,
      out Type ParsedArrayType)
    {
      throw new NotImplementedException();
      ParsedType = null;
      ParsedArrayType = null;
      // if suppplied type is System.Object then ignore it because
      // if doesn't provide any useful information (parsing methods
      // expect null in this case)
      Type valType = ValueType;
      if (valType != null && valType.BaseType == null)
        valType = null;

      Object retObj = null;
      if (node == null)
      {
        retObj = "";
      }
      else if (node is XmlText || node is XmlWhitespace)
      {
        if (valType != null && valType != typeof(string))
        {
          throw new XmlRpcTypeMismatchException(parseStack.ParseType
            + " contains implicit string value where "
            + XmlRpcServiceInfo.GetXmlRpcTypeString(valType)
            + " expected " + StackDump(parseStack));
        }
        retObj = node.Value;
      }
      else
      {
        if (node.Name == "array")
          retObj = ParseArray(node, valType, parseStack, mappingAction);
        else if (node.Name == "base64")
          retObj = ParseBase64(node, valType, parseStack, mappingAction);
        else if (node.Name == "struct")
        {
          // if we don't know the expected struct type then we must
          // parse the XML-RPC struct as an instance of XmlRpcStruct
          if (valType != null && valType != typeof(XmlRpcStruct)
            && !valType.IsSubclassOf(typeof(XmlRpcStruct)))
          {
            retObj = ParseStruct(node, valType, parseStack, mappingAction);
          }
          else
          {
            if (valType == null || valType == typeof(object))
              valType = typeof(XmlRpcStruct);
            // TODO: do we need to validate type here?
            retObj = ParseHashtable(node, valType, parseStack, mappingAction);
          }
        }
        else if (node.Name == "i4"  // integer has two representations in XML-RPC spec
          || node.Name == "int")
        {
          retObj = ParseInt(node, valType, parseStack, mappingAction);
          ParsedType = typeof(int);
          ParsedArrayType = typeof(int[]);
        }
        else if (node.Name == "i8")
        {
          retObj = ParseLong(node, valType, parseStack, mappingAction);
          ParsedType = typeof(long);
          ParsedArrayType = typeof(long[]);
        }
        else if (node.Name == "string")
        {
          retObj = ParseString(node, valType, parseStack, mappingAction);
          ParsedType = typeof(string);
          ParsedArrayType = typeof(string[]);
        }
        else if (node.Name == "boolean")
        {
          retObj = ParseBoolean(node, valType, parseStack, mappingAction);
          ParsedType = typeof(bool);
          ParsedArrayType = typeof(bool[]);
        }
        else if (node.Name == "double")
        {
          retObj = ParseDouble(node, valType, parseStack, mappingAction);
          ParsedType = typeof(double);
          ParsedArrayType = typeof(double[]);
        }
        else if (node.Name == "dateTime.iso8601")
        {
          retObj = ParseDateTime(node, valType, parseStack, mappingAction);
          ParsedType = typeof(DateTime);
          ParsedArrayType = typeof(DateTime[]);
        }
        else
          throw new XmlRpcInvalidXmlRpcException(
            "Invalid value element: <" + node.Name + ">");
      }
      return retObj;
    }

    Object ParseArray(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      // required type must be an array
      if (ValueType != null
        && !(ValueType.IsArray == true
            || ValueType == typeof(Array)
            || ValueType == typeof(object)))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains array value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      if (ValueType != null)
      {
        XmlRpcType xmlRpcType = XmlRpcServiceInfo.GetXmlRpcType(ValueType);
        if (xmlRpcType == XmlRpcType.tMultiDimArray)
        {
          parseStack.Push("array mapped to type " + ValueType.Name);
          Object ret = ParseMultiDimArray(node, ValueType, parseStack,
            mappingAction);
          return ret;
        }
        parseStack.Push("array mapped to type " + ValueType.Name);
      }
      else
        parseStack.Push("array");
      XmlNode dataNode = SelectSingleNode(node, "data");
      XmlNode[] childNodes = SelectNodes(dataNode, "value");
      int nodeCount = childNodes.Length;
      Object[] elements = new Object[nodeCount];
      // determine type of array elements
      Type elemType = null;
      if (ValueType != null
        && ValueType != typeof(Array)
        && ValueType != typeof(object))
      {
#if (!COMPACT_FRAMEWORK)
        elemType = ValueType.GetElementType();
#else
        string[] checkMultiDim = Regex.Split(ValueType.FullName, 
          "\\[\\]$");
        // determine assembly of array element type
        Assembly asmbly = ValueType.Assembly;
        string[] asmblyName = asmbly.FullName.Split(',');
        string elemTypeName = checkMultiDim[0] + ", " + asmblyName[0]; 
        elemType = Type.GetType(elemTypeName);
#endif
      }
      else
      {
        elemType = typeof(object);
      }
      bool bGotType = false;
      Type useType = null;
      int i = 0;
      foreach (XmlNode vNode in childNodes)
      {
        parseStack.Push(String.Format("element {0}", i));
        XmlNode vvNode = SelectValueNode(vNode);
        Type parsedType;
        Type parsedArrayType;
        elements[i++] = ParseValue(vvNode, elemType, parseStack, mappingAction,
                                    out parsedType, out parsedArrayType);
        if (bGotType == false)
        {
          useType = parsedArrayType;
          bGotType = true;
        }
        else
        {
          if (useType != parsedArrayType)
            useType = null;
        }
        parseStack.Pop();
      }
      Object[] args = new Object[1]; args[0] = nodeCount;
      Object retObj = null;
      if (ValueType != null
        && ValueType != typeof(Array)
        && ValueType != typeof(object))
      {
        retObj = CreateArrayInstance(ValueType, args);
      }
      else
      {
        if (useType == null)
          retObj = CreateArrayInstance(typeof(object[]), args);
        else
          retObj = CreateArrayInstance(useType, args);
      }
      for (int j = 0; j < elements.Length; j++)
      {
        ((Array)retObj).SetValue(elements[j], j);
      }
      parseStack.Pop();
      return retObj;
    }

    Object ParseMultiDimArray(XmlNode node, Type ValueType,
      ParseStack parseStack, MappingAction mappingAction)
    {
      // parse the type name to get element type and array rank
#if (!COMPACT_FRAMEWORK)
      Type elemType = ValueType.GetElementType();
      int rank = ValueType.GetArrayRank();
#else
      string[] checkMultiDim = Regex.Split(ValueType.FullName, 
        "\\[,[,]*\\]$");
      Type elemType = Type.GetType(checkMultiDim[0]);
      string commas = ValueType.FullName.Substring(checkMultiDim[0].Length+1, 
        ValueType.FullName.Length-checkMultiDim[0].Length-2);
      int rank = commas.Length+1;
#endif
      // elements will be stored sequentially as nested arrays are parsed
      ArrayList elements = new ArrayList();
      // create array to store length of each dimension - initialize to 
      // all zeroes so that when parsing we can determine if an array for 
      // that dimension has been parsed already
      int[] dimLengths = new int[rank];
      dimLengths.Initialize();
      ParseMultiDimElements(node, rank, 0, elemType, elements, dimLengths,
        parseStack, mappingAction);
      // build arguments to define array dimensions and create the array
      Object[] args = new Object[dimLengths.Length];
      for (int argi = 0; argi < dimLengths.Length; argi++)
      {
        args[argi] = dimLengths[argi];
      }
      Array ret = (Array)CreateArrayInstance(ValueType, args);
      // copy elements into new multi-dim array
      //!! make more efficient
      int length = ret.Length;
      for (int e = 0; e < length; e++)
      {
        int[] indices = new int[dimLengths.Length];
        int div = 1;
        for (int f = (indices.Length - 1); f >= 0; f--)
        {
          indices[f] = (e / div) % dimLengths[f];
          div *= dimLengths[f];
        }
        ret.SetValue(elements[e], indices);
      }
      return ret;
    }

    void ParseMultiDimElements(XmlNode node, int Rank, int CurRank,
      Type elemType, ArrayList elements, int[] dimLengths,
      ParseStack parseStack, MappingAction mappingAction)
    {
      if (node.Name != "array")
      {
        throw new XmlRpcTypeMismatchException(
          "param element does not contain array element.");
      }
      XmlNode dataNode = SelectSingleNode(node, "data");
      XmlNode[] childNodes = SelectNodes(dataNode, "value");
      int nodeCount = childNodes.Length;
      //!! check that multi dim array is not jagged
      if (dimLengths[CurRank] != 0 && nodeCount != dimLengths[CurRank])
      {
        throw new XmlRpcNonRegularArrayException(
          "Multi-dimensional array must not be jagged.");
      }
      dimLengths[CurRank] = nodeCount;  // in case first array at this rank
      if (CurRank < (Rank - 1))
      {
        foreach (XmlNode vNode in childNodes)
        {
          XmlNode arrayNode = SelectSingleNode(vNode, "array");
          ParseMultiDimElements(arrayNode, Rank, CurRank + 1, elemType,
            elements, dimLengths, parseStack, mappingAction);
        }
      }
      else
      {
        foreach (XmlNode vNode in childNodes)
        {
          XmlNode vvNode = SelectValueNode(vNode);
          elements.Add(ParseValue(vvNode, elemType, parseStack,
            mappingAction));
        }
      }
    }

    Object ParseStruct(
      XmlNode node,
      Type valueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (valueType.IsPrimitive)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains struct value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(valueType)
          + " expected " + StackDump(parseStack));
      }
#if !FX1_0
      if (valueType.IsGenericType
        && valueType.GetGenericTypeDefinition() == typeof(Nullable<>))
      {
        valueType = valueType.GetGenericArguments()[0];
      }
#endif
      object retObj;
      try
      {
        retObj = Activator.CreateInstance(valueType);
      }
      catch (Exception)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains struct value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(valueType)
          + " expected (as type " + valueType.Name + ") "
          + StackDump(parseStack));
      }
      // Note: mapping action on a struct is only applied locally - it 
      // does not override the global mapping action when members of the 
      // struct are parsed
      MappingAction localAction = mappingAction;
      if (valueType != null)
      {
        parseStack.Push("struct mapped to type " + valueType.Name);
        localAction = StructMappingAction(valueType, mappingAction);
      }
      else
      {
        parseStack.Push("struct");
      }
      // create map of field names and remove each name from it as 
      // processed so we can determine which fields are missing
      // TODO: replace HashTable with lighter collection
      Hashtable names = new Hashtable();
      foreach (FieldInfo fi in valueType.GetFields())
      {
        if (Attribute.IsDefined(fi, typeof(NonSerializedAttribute)))
          continue;
        names.Add(fi.Name, fi.Name);
      }
      foreach (PropertyInfo pi in valueType.GetProperties())
      {
        if (Attribute.IsDefined(pi, typeof(NonSerializedAttribute)))
          continue;
        names.Add(pi.Name, pi.Name);
      }
      XmlNode[] members = SelectNodes(node, "member");
      int fieldCount = 0;
      foreach (XmlNode member in members)
      {
        if (member.Name != "member")
          continue;
        XmlNode nameNode;
        bool dupName;
        XmlNode valueNode;
        bool dupValue;
        SelectTwoNodes(member, "name", out nameNode, out dupName, "value",
          out valueNode, out dupValue);
        if (nameNode == null || nameNode.FirstChild == null)
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains a member with missing name"
            + " " + StackDump(parseStack));
        if (dupName)
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains member with more than one name element"
            + " " + StackDump(parseStack));
        string name = nameNode.FirstChild.Value;
        if (valueNode == null)
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains struct member " + name + " with missing value "
            + " " + StackDump(parseStack));
        if (dupValue)
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains member with more than one value element"
            + " " + StackDump(parseStack));
        string structName = GetStructName(valueType, name);
        if (structName != null)
          name = structName;
        MemberInfo mi = valueType.GetField(name);
        if (mi == null)
          mi = valueType.GetProperty(name);
        if (mi == null)
          continue;
        if (names.Contains(name))
          names.Remove(name);
        else
        {
          if (Attribute.IsDefined(mi, typeof(NonSerializedAttribute)))
          {
            parseStack.Push(String.Format("member {0}", name));
            throw new XmlRpcNonSerializedMember("Cannot map XML-RPC struct "
              + "member onto member marked as [NonSerialized]: "
              + " " + StackDump(parseStack));
          }
          if (!IgnoreDuplicateMembers)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains struct value with duplicate member "
              + nameNode.FirstChild.Value
              + " " + StackDump(parseStack));
          else
            continue;   // ignore duplicate member
        }
        Object valObj = null;
        switch (mi.MemberType)
        {
          case MemberTypes.Field:
            FieldInfo fi = (FieldInfo)mi;
            if (valueType == null)
              parseStack.Push(String.Format("member {0}", name));
            else
              parseStack.Push(String.Format("member {0} mapped to type {1}",
                name, fi.FieldType.Name));
            try
            {
              XmlNode vvvNode = SelectValueNode(valueNode);
              valObj = ParseValue(vvvNode, fi.FieldType,
                parseStack, mappingAction);
            }
            catch (XmlRpcInvalidXmlRpcException)
            {
              if (valueType != null && localAction == MappingAction.Error)
              {
                MappingAction memberAction = MemberMappingAction(valueType,
                  name, MappingAction.Error);
                if (memberAction == MappingAction.Error)
                  throw;
              }
            }
            finally
            {
              parseStack.Pop();
            }
            fi.SetValue(retObj, valObj);
            break;
          case MemberTypes.Property:
            PropertyInfo pi = (PropertyInfo)mi;
            if (valueType == null)
              parseStack.Push(String.Format("member {0}", name));
            else

              parseStack.Push(String.Format("member {0} mapped to type {1}",
                name, pi.PropertyType.Name));
            XmlNode vvNode = SelectValueNode(valueNode);
            valObj = ParseValue(vvNode, pi.PropertyType,
              parseStack, mappingAction);
            parseStack.Pop();

            pi.SetValue(retObj, valObj, null);
            break;
        }
        fieldCount++;
      }
      if (localAction == MappingAction.Error && names.Count > 0)
        ReportMissingMembers(valueType, names, parseStack);
      parseStack.Pop();
      return retObj;
    }

    void ReportMissingMembers(
      Type valueType,
      Hashtable names,
      ParseStack parseStack)
    {
      StringBuilder sb = new StringBuilder();
      int errorCount = 0;
      string sep = "";
      foreach (string s in names.Keys)
      {
        MappingAction memberAction = MemberMappingAction(valueType, s,
          MappingAction.Error);
        if (memberAction == MappingAction.Error)
        {
          sb.Append(sep);
          sb.Append(s);
          sep = " ";
          errorCount++;
        }
      }
      if (errorCount > 0)
      {
        string plural = "";
        if (errorCount > 1)
          plural = "s";
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains struct value with missing non-optional member"
          + plural + ": " + sb.ToString() + " " + StackDump(parseStack));
      }
    }

    string GetStructName(Type ValueType, string XmlRpcName)
    {
      // given a member name in an XML-RPC struct, check to see whether
      // a field has been associated with this XML-RPC member name, return
      // the field name if it has else return null
      if (ValueType == null)
        return null;
      foreach (FieldInfo fi in ValueType.GetFields())
      {
        Attribute attr = Attribute.GetCustomAttribute(fi,
          typeof(XmlRpcMemberAttribute));
        if (attr != null
          && attr is XmlRpcMemberAttribute
          && ((XmlRpcMemberAttribute)attr).Member == XmlRpcName)
        {
          string ret = fi.Name;
          return ret;
        }
      }
      foreach (PropertyInfo pi in ValueType.GetProperties())
      {
        Attribute attr = Attribute.GetCustomAttribute(pi,
          typeof(XmlRpcMemberAttribute));
        if (attr != null
          && attr is XmlRpcMemberAttribute
          && ((XmlRpcMemberAttribute)attr).Member == XmlRpcName)
        {
          string ret = pi.Name;
          return ret;
        }
      }
      return null;
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

    Object ParseHashtable(
      XmlNode node,
      Type valueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      XmlRpcStruct retObj = new XmlRpcStruct();
      parseStack.Push("struct mapped to XmlRpcStruct");
      try
      {
        XmlNode[] members = SelectNodes(node, "member");
        foreach (XmlNode member in members)
        {
          if (member.Name != "member")
            continue;
          XmlNode nameNode;
          bool dupName;
          XmlNode valueNode;
          bool dupValue;
          SelectTwoNodes(member, "name", out nameNode, out dupName, "value",
            out valueNode, out dupValue);
          if (nameNode == null || nameNode.FirstChild == null)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains a member with missing name"
              + " " + StackDump(parseStack));
          if (dupName)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains member with more than one name element"
              + " " + StackDump(parseStack));
          string rpcName = nameNode.FirstChild.Value;
          if (valueNode == null)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains struct member " + rpcName + " with missing value "
              + " " + StackDump(parseStack));
          if (dupValue)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains member with more than one value element"
              + " " + StackDump(parseStack));
          if (retObj.Contains(rpcName))
          {
            if (!IgnoreDuplicateMembers)
              throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
                + " contains struct value with duplicate member "
                + nameNode.FirstChild.Value
                + " " + StackDump(parseStack));
            else
              continue;
          }
          object valObj;
          parseStack.Push(String.Format("member {0}", rpcName));
          try
          {
            XmlNode vvNode = SelectValueNode(valueNode);
            valObj = ParseValue(vvNode, null, parseStack,
              mappingAction);
          }
          finally
          {
            parseStack.Pop();
          }
          retObj.Add(rpcName, valObj);
        }
      }
      finally
      {
        parseStack.Pop();
      }
      return retObj;
    }

    Object ParseInt(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (ValueType != null && ValueType != typeof(Object)
        && ValueType != typeof(System.Int32)
#if !FX1_0
 && ValueType != typeof(int?)
#endif
      )
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType +
          " contains int value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      int retVal;
      parseStack.Push("integer");
      try
      {
        XmlNode valueNode = node.FirstChild;
        if (valueNode == null)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid int element " + StackDump(parseStack));
        }
        try
        {
          String strValue = valueNode.Value;
          retVal = Int32.Parse(strValue);
        }
        catch (Exception)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid int value " + StackDump(parseStack));
        }
      }
      finally
      {
        parseStack.Pop();
      }
      return retVal;
    }

    Object ParseLong(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (ValueType != null && ValueType != typeof(Object)
        && ValueType != typeof(System.Int64)
#if !FX1_0
 && ValueType != typeof(long?)
#endif
)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType +
          " contains i8 value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      long retVal;
      parseStack.Push("i8");
      try
      {
        XmlNode valueNode = node.FirstChild;
        if (valueNode == null)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid i8 element " + StackDump(parseStack));
        }
        try
        {
          String strValue = valueNode.Value;
          retVal = Int64.Parse(strValue);
        }
        catch (Exception)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid i8 value " + StackDump(parseStack));
        }
      }
      finally
      {
        parseStack.Pop();
      }
      return retVal;
    }

    Object ParseString(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (ValueType != null && ValueType != typeof(System.String)
        && ValueType != typeof(Object))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains string value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      string ret;
      parseStack.Push("string");
      try
      {
        if (node.FirstChild == null)
          ret = "";
        else
          ret = node.FirstChild.Value;
      }
      finally
      {
        parseStack.Pop();
      }
      return ret;
    }

    Object ParseBoolean(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (ValueType != null && ValueType != typeof(Object)
        && ValueType != typeof(System.Boolean)
#if !FX1_0
 && ValueType != typeof(bool?)
#endif
)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains boolean value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      bool retVal;
      parseStack.Push("boolean");
      try
      {
        string s = node.FirstChild.Value;
        if (s == "1")
        {
          retVal = true;
        }
        else if (s == "0")
        {
          retVal = false;
        }
        else
        {
          throw new XmlRpcInvalidXmlRpcException(
            "reponse contains invalid boolean value "
            + StackDump(parseStack));
        }
      }
      finally
      {
        parseStack.Pop();
      }
      return retVal;
    }

    Object ParseDouble(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (ValueType != null && ValueType != typeof(Object)
        && ValueType != typeof(System.Double)
#if !FX1_0
 && ValueType != typeof(double?)
#endif
      )
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains double value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      Double retVal;
      parseStack.Push("double");
      try
      {
        retVal = Double.Parse(node.FirstChild.Value,
          CultureInfo.InvariantCulture.NumberFormat);
      }
      catch (Exception)
      {
        throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
          + " contains invalid double value " + StackDump(parseStack));
      }
      finally
      {
        parseStack.Pop();
      }
      return retVal;
    }

    Object ParseDateTime(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (ValueType != null && ValueType != typeof(Object)
        && ValueType != typeof(System.DateTime)
#if !FX1_0
 && ValueType != typeof(DateTime?)
#endif
      )
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains dateTime.iso8601 value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      DateTime retVal;
      parseStack.Push("dateTime");
      try
      {
        XmlNode child = node.FirstChild;
        if (child == null)
        {
          if (MapEmptyDateTimeToMinValue)
            return DateTime.MinValue;
          else
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains empty dateTime value "
              + StackDump(parseStack));
        }
        string s = child.Value;
        // Allow various iso8601 formats, e.g.
        //   XML-RPC spec yyyyMMddThh:mm:ss
        //   WordPress yyyyMMddThh:mm:ssZ
        //   TypePad yyyy-MM-ddThh:mm:ssZ
        //   other yyyy-MM-ddThh:mm:ss
        if (!DateTime8601.TryParseDateTime8601(s, out retVal))
        {
          if (MapZerosDateTimeToMinValue && s.StartsWith("0000")
            && (s == "00000000T00:00:00" || s == "0000-00-00T00:00:00Z"
            || s == "00000000T00:00:00Z" || s == "0000-00-00T00:00:00"))
            retVal = DateTime.MinValue;
          else
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains invalid dateTime value "
              + StackDump(parseStack));
        }
      }
      finally
      {
        parseStack.Pop();
      }
      return retVal;
    }

    Object ParseBase64(
      XmlNode node,
      Type ValueType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      if (ValueType != null && ValueType != typeof(byte[])
        && ValueType != typeof(Object))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains base64 value where "
          + XmlRpcServiceInfo.GetXmlRpcTypeString(ValueType)
          + " expected " + StackDump(parseStack));
      }
      byte[] ret;
      parseStack.Push("base64");
      try
      {
        if (node.FirstChild == null)
          ret = new byte[0];
        else
        {
          string s = node.FirstChild.Value;
          try
          {
            ret = Convert.FromBase64String(s);
          }
          catch (Exception)
          {
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains invalid base64 value "
              + StackDump(parseStack));
          }
        }
      }
      finally
      {
        parseStack.Pop();
      }
      return ret;
    }

    XmlRpcFaultException ParseFault(
      XmlNode faultNode,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      XmlNode valueNode = SelectSingleNode(faultNode, "value");
      XmlNode structNode = SelectSingleNode(valueNode, "struct");
      if (structNode == null)
      {
        throw new XmlRpcInvalidXmlRpcException(
          "struct element missing from fault response.");
      }
      Fault fault;
      try
      {
        fault = (Fault)ParseValue(structNode, typeof(Fault), parseStack,
          mappingAction);
      }
      catch (Exception ex)
      {
        // some servers incorrectly return fault code in a string
        // ignore AllowStringFaultCode setting because existing applications
        // may rely on incorrect handling of this setting
        FaultStructStringCode faultStrCode;
        try
        {
          faultStrCode = (FaultStructStringCode)ParseValue(structNode,
            typeof(FaultStructStringCode), parseStack, mappingAction);
          fault.faultCode = Convert.ToInt32(faultStrCode.faultCode);
          fault.faultString = faultStrCode.faultString;
        }
        catch (Exception)
        {
          // use exception from when attempting to parse code as integer
          throw ex;
        }
      }
      return new XmlRpcFaultException(fault.faultCode, fault.faultString);
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

    private static bool MoveToChild(XmlReader rdr, string name1, string name2)
    {
      int depth = rdr.Depth;
      rdr.Read();
      while (rdr.Depth >= depth)
      {
        if (rdr.Depth == depth && rdr.NodeType == XmlNodeType.EndElement)
          return false;
        if (rdr.Depth == (depth + 1) && rdr.NodeType == XmlNodeType.Element 
          && (rdr.Name == name1 || rdr.Name == name2))
          return true;
        rdr.Read();
      }
      return false;
    }

    private static bool MoveToChild(XmlReader rdr, string name)
    {
      return MoveToChild(rdr, name, name);
    }

    private static bool MoveToEndElement(XmlReader rdr, string p, int startDepth)
    {
      if (rdr.NodeType == XmlNodeType.Element && rdr.Name == p && rdr.IsEmptyElement)
        return true;
      while (rdr.Depth >= startDepth)
      {
        if (rdr.NodeType == XmlNodeType.EndElement
          && rdr.Name == p)
          return true;
        rdr.Read();
      }
      return false;
    }

    private static bool MoveToEndElement(XmlReader rdr, string p, int startDepth,
      string[] notAllowed)
    {
      if (rdr.NodeType == XmlNodeType.Element && rdr.Name == p && rdr.IsEmptyElement)
        return true;
      while (rdr.Depth >= startDepth)
      {
        if (rdr.NodeType == XmlNodeType.EndElement
          && rdr.Name == p)
          return true;
        rdr.Read();
        if (rdr.Depth == startDepth)
        {
          foreach (string notAllow in notAllowed)
            if (rdr.Name == notAllow) return false;
        }
      }
      return false;
    }

    private static void MoveToContent(XmlReader rdr)
    {
      while (rdr.NodeType != XmlNodeType.Element && rdr.NodeType != XmlNodeType.EndElement)
        rdr.Read();
    }

    private static string ReadContentAsString(XmlReader rdr)
    {
      string elementName = rdr.Name;
      if (rdr.IsEmptyElement)
        return "";
      rdr.Read();
      string ret = "";
      if (rdr.NodeType == XmlNodeType.Whitespace
        || rdr.NodeType == XmlNodeType.Text)
      {
        ret = rdr.Value;
      }
      else if (rdr.NodeType == XmlNodeType.Element)
        throw new Exception("unexpected element");
      MoveToEndElement(rdr, elementName, 0);
      Debug.Assert((rdr.NodeType == XmlNodeType.EndElement 
        || rdr.NodeType == XmlNodeType.Element) && rdr.Name == elementName);
      return ret;
    }

  }
}
