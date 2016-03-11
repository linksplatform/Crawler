﻿using System;
using System.Threading;
using Abot.Crawler;
using Abot.Poco;

namespace Platform.Web.Crawler
{
    public class Crawler
    {
        private static readonly CrawlConfiguration DefaultCrawlConfiguration;

        private readonly PagesService _repository;
        private readonly CancellationToken _cancellationToken;

        public Crawler(PagesService repository, CancellationToken cancellationToken)
        {
            _repository = repository;
            _cancellationToken = cancellationToken;
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
                IsRespectUrlNamedAnchorOrHashbangEnabled = true,
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
            var crawler = new PoliteWebCrawler(DefaultCrawlConfiguration, null, null, null, null, null, null, null, null);

            crawler.PageCrawlCompleted += crawler_ProcessPageCrawlCompleted;

            crawler.ShouldCrawlPage(DecisionMaker);

            crawler.Crawl(uri);
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

            if (_cancellationToken.IsCancellationRequested)
                e.CrawlContext.IsCrawlStopRequested = true;
        }
    }
}
