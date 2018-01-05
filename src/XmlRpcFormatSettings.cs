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

using System.Text;

namespace CookComputing.XmlRpc
{
    public class XmlRpcFormatSettings
    {
        public int Indentation { get; set; } = 2;

        public bool UseEmptyElementTags { get; set; } = true;

        public bool UseEmptyParamsTag { get; set; } = true;

        public bool UseIndentation { get; set; } = true;

        public bool UseIntTag { get; set; }

        public bool UseStringTag { get; set; } = true;

        public Encoding XmlEncoding { get; set; } = null;

        public bool OmitXmlDeclaration { get; set; }

        public string DateTimeFormat { get; set; } = "yyyyMMdd'T'HH':'mm':'ss";
    }
}
