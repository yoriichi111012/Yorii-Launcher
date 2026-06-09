using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Yorii_Launcher
{
    public sealed partial class Console : Window
    {
        public static Console? Instance { get; private set; }
        private readonly DispatcherTimer scrollTimer;
        private bool insideLog4jEvent;
        private bool insideThrowable;

        public Console()
        {
            InitializeComponent();

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1176, 661));
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(titleBar);

            scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            scrollTimer.Tick += (_, _) =>
            {
                scrollTimer.Stop();
                logScroller.ScrollToVerticalOffset(logScroller.ScrollableHeight);
            };

            Instance = this;
        }

        public void AppendLine(string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var formatted = FormatLogLine(text);
                if (formatted != null)
                    consoleOutput.Text += formatted + "\n";
                scrollTimer.Stop();
                scrollTimer.Start();
            });
        }

        public void Clear()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                consoleOutput.Text = string.Empty;
                insideLog4jEvent = false;
                insideThrowable = false;
            });
        }

        // stateful log4j xml parser, strips tags and cdata to show clean log lines
        private string? FormatLogLine(string line)
        {
            line = line.TrimEnd();

            if (string.IsNullOrEmpty(line))
                return null;

            if (insideThrowable)
            {
                if (line.Contains("</log4j:Throwable>"))
                {
                    insideThrowable = false;
                    return null;
                }
                return "  " + line.Trim();
            }

            if (insideLog4jEvent)
            {
                if (line.Contains("</log4j:Event>"))
                {
                    insideLog4jEvent = false;
                    return null;
                }

                if (line.Contains("<log4j:Throwable>"))
                {
                    insideThrowable = true;
                    return null;
                }

                var cdataMatch = Regex.Match(line, @"CDATA\[(.*?)\]\]");
                if (cdataMatch.Success)
                    return cdataMatch.Groups[1].Value;

                var msgMatch = Regex.Match(line, @"<log4j:Message>(.*?)</log4j:Message>");
                if (msgMatch.Success)
                    return msgMatch.Groups[1].Value;

                return null;
            }

            if (line.Contains("<log4j:Event "))
            {
                insideLog4jEvent = true;

                var loggerMatch = Regex.Match(line, @"logger=""(.*?)""");
                var levelMatch = Regex.Match(line, @"level=""(.*?)""");

                string logger = loggerMatch.Success ? loggerMatch.Groups[1].Value : "";
                string level = levelMatch.Success ? levelMatch.Groups[1].Value : "";

                var cdataMatch = Regex.Match(line, @"CDATA\[(.*?)\]\]");
                if (cdataMatch.Success)
                    return cdataMatch.Groups[1].Value;

                var msgMatch = Regex.Match(line, @"<log4j:Message>(.*?)</log4j:Message>");
                if (msgMatch.Success)
                    return msgMatch.Groups[1].Value;

                if (line.Contains("</log4j:Event>"))
                    insideLog4jEvent = false;

                return null;
            }

            return line;
        }
    }
}
