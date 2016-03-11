using System;
using System.Diagnostics;
using Abot.Poco;
using log4net;
using Platform.Data.Core.Pairs;
using Platform.Data.Core.Sequences;

namespace Platform.Web.Crawler
{
    public class PagesService
    {
        private readonly Links _links;
        private readonly Sequences _sequences;
        private readonly ulong _pageMarker;
        private readonly ILog _logger;

        public PagesService(Links links, Sequences sequences, ulong pageMarker)
        {
            _links = links;
            _sequences = sequences;
            _pageMarker = pageMarker;

            _logger = LogManager.GetLogger("default");
        }

        public ulong Link(ulong source, ulong target)
        {
            return _links.Create(source, target);
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

            if (pageLink.Source == _pageMarker)
            {
                var uriSequence = _links.GetSource(pageLink.Target);

                // TODO: Решить что делать с элементами, которые не являются символами юникода и не могут быть в них явно конвертированы
                var uriString = UnicodeMap.FromSequenceLinkToString(uriSequence, _links);

                Uri pageUri;
                if (Uri.TryCreate(uriString, UriKind.Absolute, out pageUri))
                    return pageUri;
            }

            return null;
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

            _links.Each(Save(uri), LinksConstants.Any, contentAtDate =>
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
                    if (_links.GetSourceCore(page) == _pageMarker)
                    {
                        if(!pageHandler(page))
                            return false;

                        counter++;
                    }

                    return true;
                }, UnicodeMap.FromStringToLinkArray(partialSequence));

                sw.Stop();

                _logger.InfoFormat("Найдено {0} частичных соответствий по запросу {1} за {2} мс.", counter, partialSequence, sw.ElapsedMilliseconds);
            }

            if (searchFinishedHandler != null)
                searchFinishedHandler();
        }
    }
}
