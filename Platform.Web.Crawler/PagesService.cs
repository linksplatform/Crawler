using System;
using System.Diagnostics;
using Abot2.Poco;
using log4net;
using Platform.Data;
using Platform.Data.Doublets;
using Platform.Data.Doublets.Sequences;
using Platform.Data.Doublets.Unicode;

namespace Platform.Web.Crawler
{
    public class PagesService
    {
        private readonly ILinks<ulong> _links;
        private readonly Sequences _sequences;
        private readonly ulong _pageMarker;
        private readonly ILog _logger;

        public PagesService(ILinks<ulong> links, Sequences sequences, ulong pageMarker)
        {
            _links = links;
            _sequences = sequences;
            _pageMarker = pageMarker;

            _logger = LogManager.GetLogger("default");
        }

        public ulong Link(ulong source, ulong target)
        {
            return _links.GetOrCreate(source, target);
        }

        public ulong Save(Uri uri)
        {
            return Save(uri.ToString());
        }

        public ulong Save(DateTime dateTime)
        {
            return Save(dateTime.ToString("u"));
        }

        public ulong Save(string @string)
        {
            return _sequences.Create(UnicodeMap.FromStringToLinkArray(@string));
        }

        public void Save(CrawledPage page)
        {
            Link(
                 _pageMarker,
                 Link(
                     Save(page.Uri),
                     Link(
                         Save(DateTime.UtcNow),
                         Save(page.Content.Text)
                     )
                 )
            );
        }

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

        public DateTime? LoadDateTimeOrNull(ulong link)
        {
            var dateTimeString = UnicodeMap.FromSequenceLinkToString(link, _links);

            DateTime dateTime;
            if (DateTime.TryParse(dateTimeString, out dateTime))
                return dateTime;

            return null;
        }

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
