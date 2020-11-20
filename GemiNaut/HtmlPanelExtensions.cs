//===================================================
//    GemiNaut, a friendly browser for Gemini space on Windows

//    Copyright (C) 2020, Luke Emmet 

//    Email: luke [dot] emmet [at] gmail [dot] com

//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.

//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.

//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <https://www.gnu.org/licenses/>.
//===================================================

using System.Linq;
using System.Xml.Linq;
using TheArtOfDev.HtmlRenderer.WPF;

namespace GemiNaut
{
    public static class HtmlPanelExtensions
    {
        public static string GetTitle(this HtmlPanel panel)
        {
            return null;

            if (string.IsNullOrWhiteSpace(panel.Text))
                return null;

            var doc = XDocument.Parse(panel.Text);

            var titleTag = doc.Descendants("title").First();

            if (titleTag != null)
                return titleTag.Value;

            return null;
        }

        public static string SetCharsetUtf8(string html)
        {
            return html;

            var doc = XDocument.Parse(html);

            var metaTag = doc.Descendants("meta").First(a => a.Attributes("charset").Any());

            if (metaTag != null)
                metaTag.Attributes("charset").First().SetValue("UTF-8");
            else
            {
                metaTag = new XElement("meta");
                metaTag.SetAttributeValue("charset", "UTF-8");
                doc.Add(metaTag);
            }

            return doc.ToString();
        }
    }
}
