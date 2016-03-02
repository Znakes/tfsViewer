using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;
using HTMLConverter;

namespace TfsTaskViewer.Converters
{
    public static class HtmlTextBoxProperties
    {
        public static readonly DependencyProperty HtmlTextProperty =
            DependencyProperty.RegisterAttached("HtmlText", typeof (string), typeof (HtmlTextBoxProperties),
                new UIPropertyMetadata("", OnHtmlTextChanged));

        public static string GetHtmlText(RichTextBox wb)
        {
            return wb.GetValue(HtmlTextProperty) as string;
        }

        public static void SetHtmlText(RichTextBox wb, string html)
        {
            wb.SetValue(HtmlTextProperty, html);
        }

        private static void OnHtmlTextChanged(
            DependencyObject depObj, DependencyPropertyChangedEventArgs e)
        {
            // Go ahead and return out if we set the property on something other than a textblock, or set a value that is not a string. 
            var txtBox = depObj as RichTextBox;
            if (txtBox == null)
                return;
            if (!(e.NewValue is string))
                return;
            var html = e.NewValue as string;
            try
            {
                string xaml = HtmlToXamlConverter.ConvertHtmlToXaml(html, false);
                txtBox.Document = SetRTF(xaml);
            }
            catch
            {
                // There was a problem parsing the html, return out. 
                return;
            }

        }

        private static FlowDocument SetRTF(string xamlString)
        {
            StringReader stringReader = new StringReader(xamlString);
            XmlReader xmlReader = XmlReader.Create(stringReader);
            Section sec = XamlReader.Load(xmlReader) as Section;
            FlowDocument doc = new FlowDocument();
            while (sec.Blocks.Count > 0)
                doc.Blocks.Add(sec.Blocks.FirstBlock);
            return doc;
        }
    }


    public class BrowserBehavior
    {
        public static readonly DependencyProperty HtmlProperty = DependencyProperty.RegisterAttached(
            "Html", typeof(string), typeof(BrowserBehavior), new FrameworkPropertyMetadata(OnHtmlChanged));

        [AttachedPropertyBrowsableForType(typeof(WebBrowser))]
        public static string GetHtml(WebBrowser browser)
        {
            return (string)browser.GetValue(HtmlProperty);
        }

        public static void SetHtml(WebBrowser browser, string value)
        {
            browser.SetValue(HtmlProperty, value);
        }

        static void OnHtmlChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var browser = dependencyObject as WebBrowser;
            if (browser != null)
            {
                if (e.NewValue == null || String.IsNullOrEmpty(e.NewValue.ToString()))
                    return;
                browser.NavigateToString(e.NewValue.ToString());
            }
        }
    }
}