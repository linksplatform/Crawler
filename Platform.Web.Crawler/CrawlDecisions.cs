using Abot2.Poco;

namespace Platform.Web.Crawler
{
    public static class CrawlDecisions
    {
        public static readonly CrawlDecision Stop = new CrawlDecision
        {
            ShouldStopCrawl = true,
            Reason = "Сбор данных остановлен."
        };

        public static readonly CrawlDecision CrawledToday = new CrawlDecision
        {
            Allow = false,
            Reason = "Страница уже запрашивалась в течение 24 часов."
        };

        public static readonly CrawlDecision Allow = new CrawlDecision { Allow = true, Reason = "ok", ShouldStopCrawl = false, ShouldHardStopCrawl = false };
    }
}
