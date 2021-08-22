using Abot2.Poco;

namespace Platform.Web.Crawler
{
    /// <summary>
    /// <para>
    /// Represents the crawl decisions.
    /// </para>
    /// <para></para>
    /// </summary>
    public static class CrawlDecisions
    {
        /// <summary>
        /// <para>
        /// The reason.
        /// </para>
        /// <para></para>
        /// </summary>
        public static readonly CrawlDecision Stop = new CrawlDecision
        {
            ShouldStopCrawl = true,
            Reason = "Сбор данных остановлен."
        };

        /// <summary>
        /// <para>
        /// The reason.
        /// </para>
        /// <para></para>
        /// </summary>
        public static readonly CrawlDecision CrawledToday = new CrawlDecision
        {
            Allow = false,
            Reason = "Страница уже запрашивалась в течение 24 часов."
        };

        /// <summary>
        /// <para>
        /// The should hard stop crawl.
        /// </para>
        /// <para></para>
        /// </summary>
        public static readonly CrawlDecision Allow = new CrawlDecision { Allow = true, Reason = "ok", ShouldStopCrawl = false, ShouldHardStopCrawl = false };
    }
}
