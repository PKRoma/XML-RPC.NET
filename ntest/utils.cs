using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using CookComputing.XmlRpc;

namespace ntest
{

  public class Utils
  {
    public static XmlReader Serialize(
      string testName,
      object obj, 
      Encoding encoding,
      NullMappingAction action)
    {
      Stream stm = new MemoryStream();
      XmlTextWriter xtw = new XmlTextWriter(stm, Encoding.UTF8);
      xtw.Formatting = Formatting.Indented;
      xtw.Indentation = 2;
      xtw.WriteStartDocument();      
      XmlRpcSerializer ser = new XmlRpcSerializer();
      ser.Serialize(xtw, obj, action); 
      xtw.Flush();
      //Console.WriteLine(testName);
      stm.Position = 0;    
      TextReader trdr = new StreamReader(stm, new UTF8Encoding(), true, 4096);
      String s = trdr.ReadLine();
      while (s != null)
      {
        //Console.WriteLine(s);
        s = trdr.ReadLine();
      }            
      stm.Position = 0;
      XmlReader rdr = XmlRpcXmlReader.Create(stm);
      return rdr;
    }
    	
    public static string SerializeToString(
      string testName,
      object obj, 
      NullMappingAction action)
    {
      StringWriter strwrtr = new StringWriter();
      XmlTextWriter xtw = new XmlTextWriter(strwrtr);
      //      xtw.Formatting = formatting;
      //      xtw.Indentation = indentation;
      xtw.WriteStartDocument();      
      XmlRpcSerializer ser = new XmlRpcSerializer();
      ser.Serialize(xtw, obj, action); 
      xtw.Flush();
      //Console.WriteLine(testName);
      //Console.WriteLine(strwrtr.ToString());
      return strwrtr.ToString();
    }

    //----------------------------------------------------------------------// 
    public static object Parse(
      string xml, 
      Type valueType, 
      MappingAction action,
      out Type parsedType,
      out Type parsedArrayType)
    {
      StringReader sr = new StringReader(xml);
      XmlReader rdr = XmlRpcXmlReader.Create(sr);
      return Parse(rdr, valueType, action, 
        out parsedType, out parsedArrayType);
    }
    
    public static object Parse(
      XmlReader rdr, 
      Type valueType, 
      MappingAction action,
      out Type parsedType,
      out Type parsedArrayType)
    {
      parsedType = parsedArrayType = null;
      rdr.ReadToDescendant("value");            
      MappingStack mappingStack = new MappingStack("request");
      XmlRpcDeserializer ser = new XmlRpcDeserializer();
      object obj = ser.ParseValueElement(rdr, valueType, mappingStack, action);
      return obj;
    }

    public static object Parse(
      string xml,
      Type valueType,
      MappingAction action,
      XmlRpcDeserializer serializer,
      out Type parsedType,
      out Type parsedArrayType)
    {
      StringReader sr = new StringReader(xml);
      XmlReader rdr = XmlRpcXmlReader.Create(sr);
      return Parse(rdr, valueType, action, serializer,
        out parsedType, out parsedArrayType);
    }

    public static object Parse(
      XmlReader rdr,
      Type valueType,
      MappingAction action,
      XmlRpcDeserializer deserializer,
      out Type parsedType,
      out Type parsedArrayType)
    {
      parsedType = parsedArrayType = null;
      rdr.ReadToDescendant("value");
      MappingStack parseStack = new MappingStack("request");
      object obj = deserializer.ParseValueElement(rdr, valueType, parseStack, action);
      return obj;
    }

    public static object ParseValue(string xml, Type valueType)
    {
      MappingAction action = MappingAction.Error;
      //Type parsedType;
      //Type parsedArrayType;

      StringReader sr = new StringReader(xml);
      XmlReader rdr = XmlRpcXmlReader.Create(sr);
      rdr.MoveToContent();
      MappingStack parseStack = new MappingStack("value");
      var deser = new XmlRpcDeserializer();
      object obj = deser.ParseValueElement(rdr, valueType, parseStack, action);
      return obj;
    }
    


    static XmlNode SelectValueNode(XmlNode valueNode)
    {
      // an XML-RPC value is either held as the child node of a <value> element
      // or is just the text of the value node as an implicit string value
      XmlNode vvNode = valueNode.SelectSingleNode("*");
      if (vvNode == null)
        vvNode = valueNode.FirstChild;
      return vvNode;
    }

    public static string[] GetLocales()
    {
      return new string[] 
      {
        "af-ZA", 
        "sq-AL",
        "ar-DZ",
        "ar-BH",
        "ar-EG",
        "ar-IQ",
        "ar-JO",
        "ar-KW",
        "ar-LB",
        "ar-LY",
        "ar-MA",
        "ar-OM",
        "ar-QA",
        "ar-SA",
        "ar-SY",
        "ar-TN",
        "ar-AE",
        "ar-YE",
        "hy-AM",
        "az-Cyrl-AZ",
        "az-Latn-AZ",
        "eu-ES",
        "be-BY",
        "bg-BG",
        "ca-ES",
        "zh-HK",
        "zh-MO",
        "zh-CN",
        "zh-SG",
        "zh-TW",
        "hr-HR",
        "cs-CZ",
        "da-DK",
        "dv-MV",
        "nl-BE",
        "nl-NL",
        "en-AU",
        "en-BZ",
        "en-CA",
        "en-029",
        "en-IE",
        "en-JM",
        "en-NZ",
        "en-PH",
        "en-ZA",
        "en-TT",
        "en-GB",
        "en-US",
        "en-ZW",
        "et-EE",
        "fo-FO",
        "fa-IR",
        "fi-FI",
        "fr-BE",
        "fr-CA",
        "fr-FR",
        "fr-LU",
        "fr-MC",
        "fr-CH",
        "gl-ES",
        "ka-GE",
        "de-AT",
        "de-DE",
        "de-LI",
        "de-LU",
        "de-CH",
        "el-GR",
        "gu-IN",
        "he-IL",
        "hi-IN",
        "hu-HU",
        "is-IS",
        "id-ID",
        "it-IT",
        "it-CH",
        "ja-JP",
        "kn-IN",
        "kk-KZ",
        "kok-IN",
        "ko-KR",
        "lv-LV",
        "lt-LT",
        "mk-MK",
        "ms-BN",
        "ms-MY",
        "mr-IN",
        "mn-MN",
        "nb-NO",
        "nn-NO",
        "pl-PL",
        "pt-BR",
        "pt-PT",
        "pa-IN",
        "ro-RO",
        "ru-RU",
        "sa-IN",
        "sr-Cyrl-CS",
        "sr-Latn-CS",
        "sk-SK",
        "sl-SI",
        "es-AR",
        "es-BO",
        "es-CL",
        "es-CO",
        "es-CR",
        "es-DO",
        "es-EC",
        "es-SV",
        "es-GT",
        "es-HN",
        "es-MX",
        "es-NI",
        "es-PA",
        "es-PY",
        "es-PE",
        "es-PR",
        "es-ES",
        "es-UY",
        "es-VE",
        "sw-KE",
        "sv-FI",
        "sv-SE",
        "syr-SY",
        "ta-IN",
        "tt-RU",
        "te-IN",
        "th-TH",
        "tr-TR",
        "uk-UA",
        "ur-PK",
        "uz-Cyrl-UZ",
        "uz-Latn-UZ",
        "vi-VN"
      };
    }
  }
}
