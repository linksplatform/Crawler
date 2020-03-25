using System;
using System.Threading;
using Abot2.Crawler;
using Abot2.Poco;
using log4net;
using Platform.Threading;
using Platform.Exceptions;

namespace Platform.Web.Crawler
{
    public class Crawler
    {
        private static readonly CrawlConfiguration DefaultCrawlConfiguration;

        private readonly PagesService _repository;
        private readonly CancellationToken _cancellationToken;
        private readonly Action<object> _pageCrawled;
        private readonly ILog _logger;
        private Uri _uri;

        public Crawler(PagesService repository, CancellationToken cancellationToken, Action<object> pageCrawled = null)
        {
            _repository = repository;
            _cancellationToken = cancellationToken;
            _pageCrawled = pageCrawled;

            _logger = LogManager.GetLogger("default");
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
                MaxPagesToCrawl = 4,
                MaxPagesToCrawlPerDomain = 0,
                MaxPageSizeInBytes = 0,
                UserAgentString = "Mozilla/5.0 (Windows NT 6.3; Trident/7.0; rv:11.0) like Gecko",
                CrawlTimeoutSeconds = 0,
                DownloadableContentTypes = "text/html, text/plain",
                IsUriRecrawlingEnabled = false,
                IsExternalPageCrawlingEnabled = false,
                IsExternalPageLinksCrawlingEnabled = false,
                MaxLinksPerPage = 2,
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
                MaxCrawlDepth = 5,
                IsForcedLinkParsingEnabled = false,
                MaxRetryCount = 0,
                MinRetryDelayInMilliseconds = 50,
                HttpProtocolVersion = HttpProtocolVersion.Version11,

                // Authorization
                UseDefaultCredentials = false,
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
                MinCrawlDelayPerDomainMilliSeconds = 100,
            };
        }

        public void Start(Uri uri)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                _logger.Info("Cannot start crawl, cancellation is requested.");
                return;
            }

            _uri = uri;

            var crawler = new PoliteWebCrawler(DefaultCrawlConfiguration);

            _logger.Info("PoliteWebCrawler created.");

            crawler.PageCrawlCompleted += crawler_ProcessPageCrawlCompleted;

            crawler.RobotsDotTextParseCompleted += Crawler_RobotsDotTextParseCompleted;
            crawler.PageLinksCrawlDisallowed += Crawler_PageLinksCrawlDisallowed;
            crawler.PageCrawlStarting += Crawler_PageCrawlStarting;
            crawler.PageCrawlDisallowed += Crawler_PageCrawlDisallowed;

            crawler.ShouldCrawlPageDecisionMaker = DecisionMaker;

            CrawlResult result = crawler.CrawlAsync(uri).AwaitResult();

            if (result.ErrorException != null)
                _logger.Error(result.ErrorException.Message, result.ErrorException);
        }

        private void Crawler_PageCrawlDisallowed(object sender, PageCrawlDisallowedArgs e)
        {
            _logger.Info($"Crawler_PageCrawlDisallowed");
        }

        private void Crawler_PageCrawlStarting(object sender, PageCrawlStartingArgs e)
        {
            _logger.Info($"Crawler_PageCrawlStarting");
        }

        private void Crawler_PageLinksCrawlDisallowed(object sender, PageLinksCrawlDisallowedArgs e)
        {
            _logger.Info($"Crawler_PageLinksCrawlDisallowed");
            _logger.Info(e.DisallowedReason);
        }

        private void Crawler_RobotsDotTextParseCompleted(object sender, RobotsDotTextParseCompletedArgs e)
        {
            _logger.Info($"Crawler_RobotsDotTextParseCompleted");
        }

        private CrawlDecision DecisionMaker(PageToCrawl pageToCrawl, CrawlContext crawlContext)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                _logger.Info($"{pageToCrawl.Uri} Decision: Stop.");
                return CrawlDecisions.Stop;
            }

            var maxTimestamp = _repository.GetMaxCrawledTimestampByUri(pageToCrawl.Uri);

            if ((DateTime.UtcNow - maxTimestamp) < TimeSpan.FromDays(1))
            {
                _logger.Info($"{pageToCrawl.Uri} Decision: CrawledToday.");
                return CrawlDecisions.CrawledToday;
            }

            _logger.Info($"{pageToCrawl.Uri} Decision: Allow.");
            return CrawlDecisions.Allow;
        }

        private void crawler_ProcessPageCrawlCompleted(object sender, PageCrawlCompletedArgs e)
        {
            _logger.Info("Page received.");

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
