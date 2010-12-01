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
      var settings = new XmlReaderSettings
      {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
      };
      XmlReader xmlRdr = XmlReader.Create(stm, settings);
      return DeserializeRequest(xmlRdr, svcType);
    }

    public XmlRpcRequest DeserializeRequest(TextReader txtrdr, Type svcType)
    {
      if (txtrdr == null)
        throw new ArgumentNullException("txtrdr",
          "XmlRpcSerializer.DeserializeRequest");
      var settings = new XmlReaderSettings
      {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
      };
      XmlReader xmlRdr = XmlReader.Create(txtrdr, settings);
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
      var settings = new XmlReaderSettings
      {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
      };
      XmlReader xmlRdr = XmlReader.Create(stm, settings);
      return DeserializeResponse(xmlRdr, svcType);
    }

    public XmlRpcResponse DeserializeResponse(TextReader txtrdr, Type svcType)
    {
      if (txtrdr == null)
        throw new ArgumentNullException("txtrdr",
          "XmlRpcSerializer.DeserializeResponse");
      var settings = new XmlReaderSettings
      {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
      };
      XmlReader xmlRdr = XmlReader.Create(txtrdr, settings);
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
          DeserializeFault(rdr);
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

    private void DeserializeFault(XmlReader rdr)
    {
      ParseStack faultStack = new ParseStack("fault response");
      // TODO: use global action setting
      MappingAction mappingAction = MappingAction.Error;
      XmlRpcFaultException faultEx = ParseFault(rdr, faultStack, // TODO: fix
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
      var names = new List<string>();
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

    private static void CreateFieldNamesMap(Type valueType, List<string> names)
    {
        foreach (FieldInfo fi in valueType.GetFields())
        {
            if (Attribute.IsDefined(fi, typeof(NonSerializedAttribute)))
                continue;
            names.Add(fi.Name);
        }
        foreach (PropertyInfo pi in valueType.GetProperties())
        {
            if (Attribute.IsDefined(pi, typeof(NonSerializedAttribute)))
                continue;
            names.Add(pi.Name);
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

    void ReportMissingMembers(
      Type valueType,
      List<string> names,
      ParseStack parseStack)
    {
      StringBuilder sb = new StringBuilder();
      int errorCount = 0;
      string sep = "";
      foreach (string s in names)
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

    XmlRpcFaultException ParseFault(
    XmlReader rdr,
    ParseStack parseStack,
    MappingAction mappingAction)
    {
      throw new NotImplementedException();
      //XmlNode valueNode = SelectSingleNode(faultNode, "value");
      //XmlNode structNode = SelectSingleNode(valueNode, "struct");
      //if (structNode == null)
      //{
      //  throw new XmlRpcInvalidXmlRpcException(
      //    "struct element missing from fault response.");
      //}
      //Fault fault;
      //try
      //{
      //  fault = (Fault)ParseValue(structNode, typeof(Fault), parseStack,
      //    mappingAction);
      //}
      //catch (Exception ex)
      //{
      //  // some servers incorrectly return fault code in a string
      //  // ignore AllowStringFaultCode setting because existing applications
      //  // may rely on incorrect handling of this setting
      //  FaultStructStringCode faultStrCode;
      //  try
      //  {
      //    faultStrCode = (FaultStructStringCode)ParseValue(structNode,
      //      typeof(FaultStructStringCode), parseStack, mappingAction);
      //    fault.faultCode = Convert.ToInt32(faultStrCode.faultCode);
      //    fault.faultString = faultStrCode.faultString;
      //  }
      //  catch (Exception)
      //  {
      //    // use exception from when attempting to parse code as integer
      //    throw ex;
      //  }
      //}
      //return new XmlRpcFaultException(fault.faultCode, fault.faultString);
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
