using System;
using System.Threading;
using Abot2.Crawler;
using Abot2.Poco;
using Platform.Threading;

namespace Platform.Web.Crawler
{
    public class Crawler
    {
        private static readonly CrawlConfiguration DefaultCrawlConfiguration;

        private readonly PagesService _repository;
        private readonly CancellationToken _cancellationToken;
        private readonly Action<object> _pageCrawled;
        private Uri _uri;

        public Crawler(PagesService repository, CancellationToken cancellationToken, Action<object> pageCrawled = null)
        {
            _repository = repository;
            _cancellationToken = cancellationToken;
            _pageCrawled = pageCrawled;
        }

        static Crawler()
        {
            DefaultCrawlConfiguration = CreateDefaultConfiguration();
        }

        private static CrawlConfiguration CreateDefaultConfiguration()
        {
            return new CrawlConfiguration
            {
                // CrawlBehavior
                MaxConcurrentThreads = 1,
                MaxPagesToCrawl = 2147483646,
                MaxPagesToCrawlPerDomain = 0,
                MaxPageSizeInBytes = 0,
                UserAgentString = "Mozilla/5.0 (Windows NT 6.3; Trident/7.0; rv:11.0) like Gecko",
                CrawlTimeoutSeconds = 0,
                DownloadableContentTypes = "text/html, text/plain",
                IsUriRecrawlingEnabled = false,
                IsExternalPageCrawlingEnabled = false,
                IsExternalPageLinksCrawlingEnabled = false,
                HttpServicePointConnectionLimit = 200,
                HttpRequestTimeoutInSeconds = 15,
                HttpRequestMaxAutoRedirects = 13,
                IsHttpRequestAutoRedirectsEnabled = true,
                IsHttpRequestAutomaticDecompressionEnabled = true,
                IsSendingCookiesEnabled = true,
                IsSslCertificateValidationEnabled = false,
                IsRespectUrlNamedAnchorOrHashbangEnabled = false,
                MinAvailableMemoryRequiredInMb = 0,
                MaxMemoryUsageInMb = 0,
                MaxMemoryUsageCacheTimeInSeconds = 0,
                MaxCrawlDepth = 2147483646,
                IsForcedLinkParsingEnabled = false,
                MaxRetryCount = 0,
                MinRetryDelayInMilliseconds = 50,

                // Authorization
                IsAlwaysLogin = false,
                LoginUser = "",
                LoginPassword = "",

                // Politeness
                IsRespectRobotsDotTextEnabled = false,
                IsRespectMetaRobotsNoFollowEnabled = false,
                IsRespectHttpXRobotsTagHeaderNoFollowEnabled = false,
                IsRespectAnchorRelNoFollowEnabled = false,
                IsIgnoreRobotsDotTextIfRootDisallowedEnabled = true,
                RobotsDotTextUserAgentString = "abot",
                MaxRobotsDotTextCrawlDelayInSeconds = 5,
                MinCrawlDelayPerDomainMilliSeconds = 100
            };
        }

        public void Start(Uri uri)
        {
            if (_cancellationToken.IsCancellationRequested)
                return;

            _uri = uri;

            var crawler = new PoliteWebCrawler(DefaultCrawlConfiguration, null, null, null, null, null, null, null, null);

            crawler.PageCrawlCompleted += crawler_ProcessPageCrawlCompleted;

            crawler.ShouldCrawlPageDecisionMaker = DecisionMaker;

            crawler.CrawlAsync(uri).AwaitResult();
        }

        private CrawlDecision DecisionMaker(PageToCrawl pageToCrawl, CrawlContext crawlContext)
        {
            if (_cancellationToken.IsCancellationRequested)
                return CrawlDecisions.Stop;

            var maxTimestamp = _repository.GetMaxCrawledTimestampByUri(pageToCrawl.Uri);

            if ((DateTime.UtcNow - maxTimestamp) < TimeSpan.FromDays(1))
                return CrawlDecisions.CrawledToday;

            return CrawlDecisions.Allow;
        }

        private void crawler_ProcessPageCrawlCompleted(object sender, PageCrawlCompletedArgs e)
        {
            _repository.Save(e.CrawledPage);

            if (_pageCrawled != null)
                _pageCrawled(new
                {
                    SiteUrl = _uri.ToString(),
                    PageUrl = e.CrawledPage.Uri.ToString(),
                    PageContent = e.CrawledPage.Content.Text
                });

            if (_cancellationToken.IsCancellationRequested)
                e.CrawlContext.IsCrawlStopRequested = true;
        }
    }
}
