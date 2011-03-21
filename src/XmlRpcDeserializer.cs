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
  using System.Diagnostics;
  using System.Collections.Generic;

  public class XmlRpcDeserializer
  {
    public XmlRpcNonStandard NonStandard
    {
      get { return m_nonStandard; }
      set { m_nonStandard = value; }
    }
    XmlRpcNonStandard m_nonStandard = XmlRpcNonStandard.None;

    // private properties
    protected bool AllowInvalidHTTPContent
    {
      get { return (m_nonStandard & XmlRpcNonStandard.AllowInvalidHTTPContent) != 0; }
    }

    protected bool AllowNonStandardDateTime
    {
      get { return (m_nonStandard & XmlRpcNonStandard.AllowNonStandardDateTime) != 0; }
    }

    protected bool AllowStringFaultCode
    {
      get { return (m_nonStandard & XmlRpcNonStandard.AllowStringFaultCode) != 0; }
    }

    protected bool IgnoreDuplicateMembers
    {
      get { return (m_nonStandard & XmlRpcNonStandard.IgnoreDuplicateMembers) != 0; }
    }

    protected bool MapEmptyDateTimeToMinValue
    {
      get { return (m_nonStandard & XmlRpcNonStandard.MapEmptyDateTimeToMinValue) != 0; }
    }

    protected bool MapZerosDateTimeToMinValue
    {
      get { return (m_nonStandard & XmlRpcNonStandard.MapZerosDateTimeToMinValue) != 0; }
    }

    protected static XmlReader CreateXmlReader(Stream stm)
    {
#if (!SILVERLIGHT)
      XmlTextReader xmlRdr = new XmlTextReader(stm);
      ConfigureXmlTextReader(xmlRdr);
      return xmlRdr;
#else
      XmlReader xmlRdr = XmlReader.Create(stm, ConfigureXmlReaderSettings());
      return xmlRdr;
#endif
    }

    protected static XmlReader CreateXmlReader(TextReader txtrdr)
    {
#if (!SILVERLIGHT)
      XmlTextReader xmlRdr = new XmlTextReader(txtrdr);
      ConfigureXmlTextReader(xmlRdr);
      return xmlRdr;
#else
      XmlReader xmlRdr = XmlReader.Create(txtrdr, ConfigureXmlReaderSettings());
      return xmlRdr;
#endif
    }

#if (!SILVERLIGHT)
    private static void ConfigureXmlTextReader(XmlTextReader xmlRdr)
    {
      xmlRdr.Normalization = false;
      xmlRdr.ProhibitDtd = true;
      xmlRdr.WhitespaceHandling = WhitespaceHandling.All;
    }
#else
    private static XmlReaderSettings ConfigureXmlReaderSettings()
    {
      var settings = new XmlReaderSettings
      {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
      };
      return settings;
    }
#endif

    public Object ParseValueNode(
      IEnumerator<Node> iter,
      Type valType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      var valueNode = iter.Current as ValueNode;
      // if suppplied type is System.Object then ignore it because
      // if doesn't provide any useful information (parsing methods
      // expect null in this case)
      if (valType != null && valType.BaseType == null)
        valType = null;
      object ret = "";

      if (valueNode is StringValue && valueNode.ImplicitValue)
        CheckImplictString(valType, parseStack);

      Type parsedType;

      object retObj = null;
      if (iter.Current is ArrayValue)
        retObj = ParseArray(iter, valType, parseStack, mappingAction,
          out parsedType);
      else if (iter.Current is StructValue)
      {
        // if we don't know the expected struct type then we must
        // parse the XML-RPC struct as an instance of XmlRpcStruct
        if (valType != null && valType != typeof(XmlRpcStruct)
          && !valType.IsSubclassOf(typeof(XmlRpcStruct)))
        {
          retObj = ParseStruct(iter, valType, parseStack, mappingAction,
            out parsedType);
        }
        else
        {
          if (valType == null || valType == typeof(object))
            valType = typeof(XmlRpcStruct);
          // TODO: do we need to validate type here?
          retObj = ParseHashtable(iter, valType, parseStack, mappingAction,
            out parsedType);
        }
      }
      else if (iter.Current is Base64Value)
        retObj = ParseBase64(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      else if (iter.Current is IntValue)
      {
        retObj = ParseInt(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      }
      else if (iter.Current is LongValue)
      {
        retObj = ParseLong(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      }
      else if (iter.Current is StringValue)
      {
        retObj = ParseString(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      }
      else if (iter.Current is BooleanValue)
      {
        retObj = ParseBoolean(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      }
      else if (iter.Current is DoubleValue)
      {
        retObj = ParseDouble(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      }
      else if (iter.Current is DateTimeValue)
      {
        retObj = ParseDateTime(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      }
      else if (iter.Current is NilValue)
      {
        retObj = ParseNilValue(valueNode.Value, valType, parseStack, mappingAction,
          out parsedType);
      }

      return retObj;
    }

    private object ParseDateTime(string value, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      CheckExpectedType(valType, typeof(DateTime), parseStack);
      parsedType = typeof(DateTime);
      return OnStack("dateTime", parseStack, delegate()
      {
        if (value == "" && MapEmptyDateTimeToMinValue)
          return DateTime.MinValue;
        DateTime retVal;
        if (!DateTime8601.TryParseDateTime8601(value, out retVal))
        {
          if (MapZerosDateTimeToMinValue && value.StartsWith("0000")
            && (value == "00000000T00:00:00" || value == "0000-00-00T00:00:00Z"
            || value == "00000000T00:00:00Z" || value == "0000-00-00T00:00:00"))
            retVal = DateTime.MinValue;
          else
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains invalid dateTime value "
              + StackDump(parseStack));
        }
        return retVal;
      });
    }

    private object ParseDouble(string value, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      CheckExpectedType(valType, typeof(double), parseStack);
      parsedType = typeof(double);
      return OnStack("double", parseStack, delegate()
      {
        try
        {
          double ret = Double.Parse(value, CultureInfo.InvariantCulture.NumberFormat);
          return ret;
        }
        catch (Exception)
        {
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid double value " + StackDump(parseStack));
        }
      });
    }

    private object ParseBoolean(string value, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      CheckExpectedType(valType, typeof(bool), parseStack);
      parsedType = typeof(bool);
      return OnStack("boolean", parseStack, delegate()
      {
        if (value == "1")
          return true;
        else if (value == "0")
          return false;
        else
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid boolean value "
            + StackDump(parseStack));
      });
    }

    private object ParseString(string value, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      CheckExpectedType(valType, typeof(string), parseStack);
      parsedType = typeof(string);
      return OnStack("string", parseStack, delegate()
      { return value; });
    }

    private object ParseLong(string value, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      CheckExpectedType(valType, typeof(long), parseStack);
      parsedType = typeof(long);
      return OnStack("i8", parseStack, delegate()
      {
        long ret;
        if (!Int64.TryParse(value, out ret))
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid i8 value " + StackDump(parseStack));
        return ret;
      });
    }

    private object ParseInt(string value, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      CheckExpectedType(valType, typeof(int), parseStack);
      parsedType = typeof(int);
      return OnStack("integer", parseStack, delegate()
      { 
        int ret;
        if (!Int32.TryParse(value, out ret))
          throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
            + " contains invalid int value " + StackDump(parseStack));
        return ret;
      });
    }

    private object ParseBase64(string value, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      CheckExpectedType(valType, typeof(byte[]), parseStack);
      parsedType = typeof(int);
      return OnStack("base64", parseStack, delegate()
      { 
        if (value == "")
          return new byte[0];
        else
        {
          try
          {
            byte[] ret = Convert.FromBase64String(value);
            return ret;
          }
          catch (Exception)
          {
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains invalid base64 value "
              + StackDump(parseStack));
          }
        }
      });
    }

    private object ParseNilValue(string p, Type type, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
      {
        parsedType = type;
        return null;
      }
      else if (!type.IsPrimitive || !type.IsValueType)
      {
        parsedType = type;
        return null;
      }
      else
      {
        parsedType = null;
        throw new NotImplementedException(); // TODO: fix
      }
    }

    protected object ParseHashtable(IEnumerator<Node> iter, Type valType, ParseStack parseStack, MappingAction mappingAction, out Type parsedType)
    {
      parsedType = null;
      XmlRpcStruct retObj = new XmlRpcStruct();
      parseStack.Push("struct mapped to XmlRpcStruct");
      try
      {
        while (iter.MoveNext() && iter.Current is StructMember)
        {
          string rpcName = (iter.Current as StructMember).Value;
          if (retObj.ContainsKey(rpcName)
            && !IgnoreDuplicateMembers)
            throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
              + " contains struct value with duplicate member "
              + rpcName
              + " " + StackDump(parseStack));
          iter.MoveNext();

          object value = OnStack(String.Format("member {0}", rpcName),
            parseStack, delegate()
            {
              return ParseValueNode(iter, null, parseStack, mappingAction);
            });
          if (!retObj.ContainsKey(rpcName))
            retObj[rpcName] = value;
        }
      }
      finally
      {
        parseStack.Pop();
      }
      return retObj;
    }

    private object ParseStruct(IEnumerator<Node> iter, Type valueType, ParseStack parseStack, 
      MappingAction mappingAction, out Type parsedType)
    {
      parsedType = null;

      if (valueType.IsPrimitive)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains struct value where "
          + XmlRpcTypeInfo.GetXmlRpcTypeString(valueType)
          + " expected " + StackDump(parseStack));
      }
      if (valueType.IsGenericType
        && valueType.GetGenericTypeDefinition() == typeof(Nullable<>))
      {
        valueType = valueType.GetGenericArguments()[0];
      }
      object retObj;
      try
      {
        retObj = Activator.CreateInstance(valueType);
      }
      catch (Exception)
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains struct value where "
          + XmlRpcTypeInfo.GetXmlRpcTypeString(valueType)
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
        while (iter.MoveNext())
        {
          if (!(iter.Current is StructMember))
            break;
          string rpcName = (iter.Current as StructMember).Value;
          if (rpcNames.Contains(rpcName))
          {
            if (!IgnoreDuplicateMembers)
              throw new XmlRpcInvalidXmlRpcException(parseStack.ParseType
                + " contains struct value with duplicate member "
                + rpcName
                + " " + StackDump(parseStack));
            else
              continue;
          }
          else
            rpcNames.Add(rpcName);

          string name = GetStructName(valueType, rpcName) ?? rpcName;
          MemberInfo mi = valueType.GetField(name);
          if (mi == null) mi = valueType.GetProperty(name);
          if (mi == null)
          {
            iter.MoveNext();  // value not required
            continue;
          }
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

          iter.MoveNext();
          object valObj = OnStack(parseMsg, parseStack, delegate()
          {
              return ParseValueNode(iter, memberType, parseStack, mappingAction);
          });

          if (mi.MemberType == MemberTypes.Field)
            (mi as FieldInfo).SetValue(retObj, valObj);
          else
            (mi as PropertyInfo).SetValue(retObj, valObj, null);
          fieldCount++;
        }

        if (localAction == MappingAction.Error && names.Count > 0)
          ReportMissingMembers(valueType, names, parseStack);
        return retObj;
      }
      finally
      {
        parseStack.Pop();
      }
    }

    private object ParseArray(IEnumerator<Node> iter, Type valType, 
      ParseStack parseStack, MappingAction mappingAction, 
      out Type parsedType)
    {
      parsedType = null;
      // required type must be an array
      if (valType != null
        && !(valType.IsArray == true
            || valType == typeof(Array)
            || valType == typeof(object)))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains array value where "
          + XmlRpcTypeInfo.GetXmlRpcTypeString(valType)
          + " expected " + StackDump(parseStack));
      }
      if (valType != null)
      {
        XmlRpcType xmlRpcType = XmlRpcTypeInfo.GetXmlRpcType(valType);
        if (xmlRpcType == XmlRpcType.tMultiDimArray)
        {
          parseStack.Push("array mapped to type " + valType.Name);
          Object ret = ParseMultiDimArray(iter, valType, parseStack,
            mappingAction);
          return ret;
        }
        parseStack.Push("array mapped to type " + valType.Name);
      }
      else
        parseStack.Push("array");

      var values = new List<object>();
      Type elemType = DetermineArrayItemType(valType);

      bool bGotType = false;
      Type useType = null;

      while (iter.MoveNext() && iter.Current is ValueNode)
      {
        parseStack.Push(String.Format("element {0}", values.Count));
        object value = ParseValueNode(iter, elemType, parseStack, mappingAction);
        values.Add(value);
        parseStack.Pop();
      }

      foreach (object value in values)
      {
        if (value == null)
          continue;
        if (bGotType == false)
        {
          useType = value.GetType();
          bGotType = true;
        }
        else
        {
          if (useType != value.GetType())
            useType = null;
        }
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
          retObj = Array.CreateInstance(useType, (int)args[0]); ;
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


    private void CheckImplictString(Type valType, ParseStack parseStack)
    {
      if (valType != null && valType != typeof(string))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType
          + " contains implicit string value where "
          + XmlRpcTypeInfo.GetXmlRpcTypeString(valType)
          + " expected " + StackDump(parseStack));
      }
    }

    Object ParseMultiDimArray(IEnumerator<Node> iter, Type ValueType,
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
      var elements = new List<object>();
      // create array to store length of each dimension - initialize to 
      // all zeroes so that when parsing we can determine if an array for 
      // that dimension has been parsed already
      int[] dimLengths = new int[rank];
      dimLengths.Initialize();
      ParseMultiDimElements(iter, rank, 0, elemType, elements, dimLengths,
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

    void ParseMultiDimElements(IEnumerator<Node> iter, int Rank, int CurRank,
      Type elemType, List<object> elements, int[] dimLengths,
      ParseStack parseStack, MappingAction mappingAction)
    {
      //XmlNode dataNode = SelectSingleNode(node, "data");
      //XmlNode[] childNodes = SelectNodes(dataNode, "value");
      //int nodeCount = childNodes.Length;
      ////!! check that multi dim array is not jagged
      //if (dimLengths[CurRank] != 0 && nodeCount != dimLengths[CurRank])
      //{
      //  throw new XmlRpcNonRegularArrayException(
      //    "Multi-dimensional array must not be jagged.");
      //}
      //dimLengths[CurRank] = nodeCount;  // in case first array at this rank
      int nodeCount = 0;
      if (CurRank < (Rank - 1))
      {
        while (iter.MoveNext() && iter.Current is ArrayValue)
        {
          nodeCount++;
          ParseMultiDimElements(iter, Rank, CurRank + 1, elemType,
            elements, dimLengths, parseStack, mappingAction);
        }
      }
      else
      {
        while (iter.MoveNext() && iter.Current is ValueNode)
        {
          nodeCount++;
          object value = ParseValueNode(iter, elemType, parseStack, mappingAction);
          elements.Add(value);
        }
      }
      dimLengths[CurRank] = nodeCount;
    }






    public Object ParseValueElement(
      XmlReader rdr,
      Type valType,
      ParseStack parseStack,
      MappingAction mappingAction)
    {
      var iter = XmlRpcParser.ParseValue(rdr).GetEnumerator();
      iter.MoveNext();

      return ParseValueNode(iter, valType, parseStack, mappingAction);
    }


//    private object ParseArray(XmlReader rdr, Type valType, ParseStack parseStack,
//      MappingAction mappingAction, out Type ParsedType, out Type ParsedArrayType)
//    {
//      ParsedType = null;
//      ParsedArrayType = null;
//      // required type must be an array
//      if (valType != null
//        && !(valType.IsArray == true
//            || valType == typeof(Array)
//            || valType == typeof(object)))
//      {
//        throw new XmlRpcTypeMismatchException(parseStack.ParseType
//          + " contains array value where "
//          + XmlRpcServiceInfo.GetXmlRpcTypeString(valType)
//          + " expected " + StackDump(parseStack));
//      }
//      if (valType != null)
//      {
//        XmlRpcType xmlRpcType = XmlRpcServiceInfo.GetXmlRpcType(valType);
//        if (xmlRpcType == XmlRpcType.tMultiDimArray)
//        {
//          parseStack.Push("array mapped to type " + valType.Name);
//          Object ret = ParseMultiDimArray(rdr, valType, parseStack,
//            mappingAction);
//          return ret;
//        }
//        parseStack.Push("array mapped to type " + valType.Name);
//      }
//      else
//        parseStack.Push("array");

//      MoveToChild(rdr, "data");

//      //XmlNode[] childNodes = SelectNodes(dataNode, "value");
//      //int nodeCount = childNodes.Length;
//      //Object[] elements = new Object[nodeCount];

//      var values = new List<object>();
//      Type elemType = DetermineArrayItemType(valType);



//      bool bGotType = false;
//      Type useType = null;

//      bool gotValue = MoveToChild(rdr, "value");
//      while (gotValue)
//      {
//        parseStack.Push(String.Format("element {0}", values.Count));
//        object value = ParseValueElement(rdr, elemType, parseStack, mappingAction);
//        values.Add(value);
//        MoveToEndElement(rdr, "value", 0);
//        gotValue = rdr.ReadToNextSibling("value");
//        parseStack.Pop();
//      }


//      foreach (object value in values)
//      {
//        //if (bGotType == false)
//        //{
//        //  useType = parsedArrayType;
//        //  bGotType = true;
//        //}
//        //else
//        //{
//        //  if (useType != parsedArrayType)
//        //    useType = null;
//        //}
//      }

//      Object[] args = new Object[1];
//      args[0] = values.Count;
//      Object retObj = null;
//      if (valType != null
//        && valType != typeof(Array)
//        && valType != typeof(object))
//      {
//        retObj = CreateArrayInstance(valType, args);
//      }
//      else
//      {
//        if (useType == null)
//          retObj = CreateArrayInstance(typeof(object[]), args);
//        else
//          retObj = CreateArrayInstance(useType, args);
//      }
//      for (int j = 0; j < values.Count; j++)
//      {
//        ((Array)retObj).SetValue(values[j], j);
//      }

//      parseStack.Pop();

//      return retObj;
//    }

//    private static Type DetermineArrayItemType(Type valType)
//    {
//      Type elemType = null;
//      if (valType != null
//        && valType != typeof(Array)
//        && valType != typeof(object))
//      {
//#if (!COMPACT_FRAMEWORK)
//        elemType = valType.GetElementType();
//#else
//        string[] checkMultiDim = Regex.Split(ValueType.FullName, 
//          "\\[\\]$");
//        // determine assembly of array element type
//        Assembly asmbly = ValueType.Assembly;
//        string[] asmblyName = asmbly.FullName.Split(',');
//        string elemTypeName = checkMultiDim[0] + ", " + asmblyName[0]; 
//        elemType = Type.GetType(elemTypeName);
//#endif
//      }
//      else
//      {
//        elemType = typeof(object);
//      }
//      return elemType;
//    }

//    private object ParseMultiDimArray(XmlReader rdr, Type valType, ParseStack parseStack, MappingAction mappingAction)
//    {
//      throw new NotImplementedException();
//    }

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


    private void CheckExpectedType(Type actualType, Type expectedType, ParseStack parseStack)
    {
      if (actualType != null && actualType != typeof(Object)
        && actualType != expectedType 
        && (expectedType.IsValueType 
          && actualType != typeof(Nullable<>).MakeGenericType(expectedType)))
      {
        throw new XmlRpcTypeMismatchException(parseStack.ParseType +
          " contains "
          + XmlRpcTypeInfo.GetXmlRpcTypeString(expectedType)
          + "value where "
          + XmlRpcTypeInfo.GetXmlRpcTypeString(actualType)
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
  }
}


