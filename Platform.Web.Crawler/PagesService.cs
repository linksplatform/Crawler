using System;
using System.Diagnostics;
using Abot2.Poco;
using log4net;
using Platform.Collections;
using Platform.Data;
using Platform.Data.Doublets;
using Platform.Data.Doublets.Sequences;
using Platform.Data.Doublets.Unicode;

namespace Platform.Web.Crawler
{
    /// <summary>
    /// <para>
    /// Represents the pages service.
    /// </para>
    /// <para></para>
    /// </summary>
    public class PagesService
    {
        /// <summary>
        /// <para>
        /// The links.
        /// </para>
        /// <para></para>
        /// </summary>
        private readonly ILinks<ulong> _links;
        /// <summary>
        /// <para>
        /// The sequences.
        /// </para>
        /// <para></para>
        /// </summary>
        private readonly Sequences _sequences;
        /// <summary>
        /// <para>
        /// The page marker.
        /// </para>
        /// <para></para>
        /// </summary>
        private readonly ulong _pageMarker;
        /// <summary>
        /// <para>
        /// The logger.
        /// </para>
        /// <para></para>
        /// </summary>
        private readonly ILog _logger;

        /// <summary>
        /// <para>
        /// Initializes a new <see cref="PagesService"/> instance.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="links">
        /// <para>A links.</para>
        /// <para></para>
        /// </param>
        /// <param name="sequences">
        /// <para>A sequences.</para>
        /// <para></para>
        /// </param>
        /// <param name="pageMarker">
        /// <para>A page marker.</para>
        /// <para></para>
        /// </param>
        public PagesService(ILinks<ulong> links, Sequences sequences, ulong pageMarker)
        {
            _links = links;
            _sequences = sequences;
            _pageMarker = pageMarker;

            _logger = LogManager.GetLogger("default");
        }

        /// <summary>
        /// <para>
        /// Links the source.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="source">
        /// <para>The source.</para>
        /// <para></para>
        /// </param>
        /// <param name="target">
        /// <para>The target.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The ulong</para>
        /// <para></para>
        /// </returns>
        public ulong Link(ulong source, ulong target) => _links.GetOrCreate(source, target);

        /// <summary>
        /// <para>
        /// Saves the uri.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="uri">
        /// <para>The uri.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The ulong</para>
        /// <para></para>
        /// </returns>
        public ulong Save(Uri uri) => Save(uri.ToString());

        /// <summary>
        /// <para>
        /// Saves the date time.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="dateTime">
        /// <para>The date time.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The ulong</para>
        /// <para></para>
        /// </returns>
        public ulong Save(DateTime dateTime) => Save(dateTime.ToString("u"));

        /// <summary>
        /// <para>
        /// Saves the string.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="@string">
        /// <para>The string.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The ulong</para>
        /// <para></para>
        /// </returns>
        public ulong Save(string @string) => _sequences.Create(UnicodeMap.FromStringToLinkArray(@string));

        /// <summary>
        /// <para>
        /// Saves the page.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="page">
        /// <para>The page.</para>
        /// <para></para>
        /// </param>
        public void Save(CrawledPage page)
        {
            var linksBefore = _links.Count();
            var now = DateTime.UtcNow;
            Link(
                 _pageMarker,
                 Link(
                     Save(page.Uri),
                     Link(
                         Save(now),
                         Save(page.Content.Text)
                     )
                 )
            );
            var linksUsed = _links.Count() - linksBefore;
            _logger.InfoFormat($"Page {page.Uri} (bytes: {page.Content?.Text?.Length}, links: {linksUsed}) saved at {now}.");
            _logger.InfoFormat($"Page text: {page.Content?.Text.Truncate(50)} ...");
        }

        /// <summary>
        /// <para>
        /// Loads the page uri or null using the specified page.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="page">
        /// <para>The page.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The uri</para>
        /// <para></para>
        /// </returns>
        public Uri LoadPageUriOrNull(ulong page)
        {
            var pageLink = _links.GetLink(page);

            if (pageLink[_links.Constants.SourcePart] == _pageMarker)
            {
                var uriSequence = _links.GetSource(pageLink[_links.Constants.TargetPart]);

                // TODO: Решить что делать с элементами, которые не являются символами юникода и не могут быть в них явно конвертированы
                var uriString = UnicodeMap.FromSequenceLinkToString(uriSequence, _links);

                Uri pageUri;
                if (Uri.TryCreate(uriString, UriKind.Absolute, out pageUri))
                    return pageUri;
            }

            return null;
        }

        /// <summary>
        /// <para>
        /// Loads the page content or null using the specified uri.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="uri">
        /// <para>The uri.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The result.</para>
        /// <para></para>
        /// </returns>
        public string LoadPageContentOrNull(Uri uri)
        {
            int target = _links.Constants.TargetPart;

            var uriLink = Save(uri);

            string result = null;

            _links.Each(uriLink, _links.Constants.Any, uriAndContent =>
            {
                return _links.Each(_links.Constants.Any, uriAndContent[_links.Constants.IndexPart], page =>
                {
                    if (_links.GetSource(page[_links.Constants.IndexPart]) == _pageMarker)
                    {
                        var contentLink = _links.GetByKeys(page[_links.Constants.IndexPart], target, target, target);
                        // "Get page, than it's target, than it's (page's target) target, than it's (page's target's target) target, and return."
                        // "Get page's target's target's target"
                        // The same as:
                        //var contentLink = _links.GetTarget(_links.GetTarget(_links.GetTarget(page)));

                        result = UnicodeMap.FromSequenceLinkToString(contentLink, _links);
                        return _links.Constants.Break;
                    }
                    return _links.Constants.Continue;
                }) ? _links.Constants.Continue : _links.Constants.Break;
            });

            return result;
        }

        /// <summary>
        /// <para>
        /// Loads the date time or null using the specified link.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="link">
        /// <para>The link.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The date time</para>
        /// <para></para>
        /// </returns>
        public DateTime? LoadDateTimeOrNull(ulong link)
        {
            var dateTimeString = UnicodeMap.FromSequenceLinkToString(link, _links);

            DateTime dateTime;
            if (DateTime.TryParse(dateTimeString, out dateTime))
                return dateTime;

            return null;
        }

        /// <summary>
        /// <para>
        /// Gets the max crawled timestamp by uri using the specified uri.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="uri">
        /// <para>The uri.</para>
        /// <para></para>
        /// </param>
        /// <returns>
        /// <para>The max timestamp.</para>
        /// <para></para>
        /// </returns>
        public DateTime GetMaxCrawledTimestampByUri(Uri uri)
        {
            var maxTimestamp = default(DateTime);

            _links.Each(Save(uri), _links.Constants.Any, contentAtDate =>
            {
                var timestampSequence = _links.GetSource(_links.GetTarget(contentAtDate));

                var timestamp = LoadDateTimeOrNull(timestampSequence);
                if (timestamp != null && maxTimestamp < timestamp)
                    maxTimestamp = (DateTime)timestamp;

                return true;
            });

            return maxTimestamp;
        }

        /// <summary>
        /// <para>
        /// Searches the pages using the specified partial sequence.
        /// </para>
        /// <para></para>
        /// </summary>
        /// <param name="partialSequence">
        /// <para>The partial sequence.</para>
        /// <para></para>
        /// </param>
        /// <param name="pageHandler">
        /// <para>The page handler.</para>
        /// <para></para>
        /// </param>
        /// <param name="searchFinishedHandler">
        /// <para>The search finished handler.</para>
        /// <para></para>
        /// </param>
        public void SearchPages(string partialSequence, Func<ulong, bool> pageHandler, Action searchFinishedHandler = null)
        {
            if (pageHandler != null)
            {
                var sw = Stopwatch.StartNew();

                var counter = 0;

                _sequences.GetAllPartiallyMatchingSequences2(page =>
                {
                    if (_links.GetSource(page[_links.Constants.IndexPart]) == _pageMarker)
                    {
                        if (!pageHandler(page[_links.Constants.IndexPart]))
                            return _links.Constants.Break;

                        counter++;
                    }

                    return _links.Constants.Continue;
                }, UnicodeMap.FromStringToLinkArray(partialSequence));

                sw.Stop();

                _logger.InfoFormat("Найдено {0} частичных соответствий по запросу {1} за {2} мс.", counter, partialSequence, sw.ElapsedMilliseconds);
            }

            if (searchFinishedHandler != null)
                searchFinishedHandler();
        }
    }
}
