using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System.Text;

namespace Platform.Web.Crawler
{
    /// <summary>
    /// <para>
    /// Represents the log service.
    /// </para>
    /// <para></para>
    /// </summary>
    public class LogService
    {
        /// <summary>
        /// <para>
        /// The default log file.
        /// </para>
        /// <para></para>
        /// </summary>
        private const string DefaultLogFile = "log.txt";

        /// <summary>
        /// <para>
        /// Configures the log file.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="logFile">
        /// <para>The log file.</para>
        /// <para></para>
        /// </param>
        public void Configure(string logFile = null)
        {
            var hierarchy = (Hierarchy)LogManager.GetRepository();

            var patternLayout = new PatternLayout
            {
                ConversionPattern = "[%date] [%thread] [%-5level] - %message - [%logger]%newline"
            };
            patternLayout.ActivateOptions();

            if (string.IsNullOrWhiteSpace(logFile))
                logFile = DefaultLogFile;

            var roller = new RollingFileAppender
            {
                AppendToFile = true,
                File = logFile,
                Layout = patternLayout,
                MaxSizeRollBackups = 10,
                MaximumFileSize = "50MB",
                RollingStyle = RollingFileAppender.RollingMode.Size,
                StaticLogFileName = true,
                Encoding = Encoding.UTF8,
                PreserveLogFileNameExtension = true
            };
            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);

            var console = new ConsoleAppender { Layout = patternLayout };
            console.ActivateOptions();
            hierarchy.Root.AddAppender(console);

            hierarchy.Root.Level = Level.All;
            hierarchy.Configured = true;
        }
    }
}
